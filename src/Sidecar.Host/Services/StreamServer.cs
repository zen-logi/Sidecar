// <copyright file="StreamServer.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Sidecar.Host.Interfaces;
using Sidecar.Shared;
using Sidecar.Shared.Models;

namespace Sidecar.Host.Services;

/// <summary>
/// TCPベースのMJPEGストリーミングサーバー。
/// </summary>
/// <remarks>
/// <see cref="StreamServer"/> クラスの新しいインスタンスを初期化します。
/// </remarks>
/// <param name="cameraService">カメラサービス。</param>
/// <param name="logger">ロガー。</param>
public sealed class StreamServer(ICameraService cameraService, ILogger<StreamServer> logger) : IStreamServer
{
    private readonly ICameraService _cameraService = cameraService ?? throw new ArgumentNullException(nameof(cameraService));
    private readonly ILogger<StreamServer> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly ConcurrentDictionary<Guid, TcpClient> _clients = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _serverTokenSource;
    private Task? _acceptTask;
    private bool _disposed;

    /// <inheritdoc />
    public int ConnectedClientCount => _clients.Count;

    /// <inheritdoc />
    public bool IsRunning => _acceptTask is not null && !_acceptTask.IsCompleted;

    /// <inheritdoc />
    public Task StartAsync(int port, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (IsRunning)
        {
            throw new InvalidOperationException("サーバーは既に実行中です。");
        }

        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();

        _serverTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // フレーム受信イベントを購読
        _cameraService.FrameAvailable += OnFrameAvailable;

        _acceptTask = AcceptClientsAsync(_serverTokenSource.Token);

        _logger.LogInformation("ストリーミングサーバーがポート {Port} で開始しました", port);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _cameraService.FrameAvailable -= OnFrameAvailable;

        if (_serverTokenSource is not null)
        {
            await _serverTokenSource.CancelAsync();
        }

        // すべてのクライアントを切断（スナップショットを使用）
        foreach (var client in _clients.Values.ToArray())
        {
            try
            {
                client.Close();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "クライアント切断中のエラー");
            }
        }

        _clients.Clear();

        try
        {
            _listener?.Stop();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "リスナー停止中のエラー");
        }

        if (_acceptTask is not null)
        {
            try
            {
                await _acceptTask.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("接続受付タスクの停止がタイムアウトしました");
            }
            catch (OperationCanceledException)
            {
                // キャンセルは正常
            }
        }

        _serverTokenSource?.Dispose();
        _serverTokenSource = null;
        _listener = null;
        _acceptTask = null;

        _logger.LogInformation("ストリーミングサーバーが停止しました");
    }

    /// <summary>
    /// クライアント接続を受け付けるループ。
    /// </summary>
    private async Task AcceptClientsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener is not null)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(cancellationToken);

                // Nagleアルゴリズム無効化（低遅延のため必須）
                client.NoDelay = true;

                var clientId = Guid.NewGuid();
                _ = _clients.TryAdd(clientId, client);

                _logger.LogInformation("クライアント接続: {RemoteEndPoint} (ID: {ClientId})", client.Client.RemoteEndPoint, clientId);

                // ハンドシェイクを別タスクで実行
                _ = HandleClientAsync(clientId, client, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "クライアント受付エラー");
            }
        }
    }

    /// <summary>
    /// クライアントのハンドシェイクを処理します。
    /// </summary>
    private async Task HandleClientAsync(Guid clientId, TcpClient client, CancellationToken cancellationToken)
    {
        try
        {
            var stream = client.GetStream();

            // HTTP風レスポンスヘッダーを送信
            var header = $"HTTP/1.1 200 OK\r\n" +
                         $"Content-Type: {StreamingConstants.MjpegContentType}\r\n" +
                         $"Cache-Control: no-cache\r\n" +
                         $"Connection: keep-alive\r\n" +
                         $"\r\n";

            var headerBytes = Encoding.ASCII.GetBytes(header);
            await stream.WriteAsync(headerBytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);

            _logger.LogDebug("クライアント {ClientId} へハンドシェイク完了", clientId);

            // 接続を維持（フレーム送信はイベントで行う）
            while (!cancellationToken.IsCancellationRequested && client.Connected)
            {
                await Task.Delay(100, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // キャンセルは正常
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "クライアント {ClientId} エラー", clientId);
        }
        finally
        {
            _ = _clients.TryRemove(clientId, out _);
            client.Close();
            _logger.LogInformation("クライアント切断: {ClientId}", clientId);
        }
    }

    /// <summary>
    /// フレーム受信時にすべてのクライアントへブロードキャストします。
    /// </summary>
    private void OnFrameAvailable(object? sender, FrameEventArgs e)
    {
        var frame = e.Frame;
        var boundary = $"\r\n{StreamingConstants.MjpegBoundary}\r\n" +
                       $"Content-Type: image/jpeg\r\n" +
                       $"Content-Length: {frame.JpegData.Length}\r\n" +
                       $"\r\n";

        var boundaryBytes = Encoding.ASCII.GetBytes(boundary);

        // スナップショットを使用してイテレーション中の変更を防ぐ
        foreach (var (clientId, client) in _clients.ToArray())
        {
            if (!client.Connected)
            {
                _ = _clients.TryRemove(clientId, out _);
                continue;
            }

            try
            {
                var stream = client.GetStream();

                // 境界文字列を送信
                stream.Write(boundaryBytes);

                // フレームデータを送信
                stream.Write(frame.JpegData);

                stream.Flush();
            }
            catch (Exception ex)
            {
                // 送信エラーの場合はクライアントを削除
                _logger.LogDebug(ex, "クライアント {ClientId} へのフレーム送信エラー", clientId);
                _ = _clients.TryRemove(clientId, out _);
                try
                {
                    client.Close();
                }
                catch
                {
                    // 無視
                }
            }
        }
    }

    /// <summary>
    /// リソースを解放します。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // 同期的に停止
        _cameraService.FrameAvailable -= OnFrameAvailable;
        _serverTokenSource?.Cancel();
        _ = (_acceptTask?.Wait(TimeSpan.FromSeconds(2)));

        foreach (var client in _clients.Values)
        {
            try
            {
                client.Close();
            }
            catch
            {
                // 無視
            }
        }

        _clients.Clear();
        _listener?.Stop();
        _serverTokenSource?.Dispose();

        _disposed = true;
    }
}

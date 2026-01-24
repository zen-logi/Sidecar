// <copyright file="StreamServer.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Sidecar.Host.Interfaces;
using Sidecar.Shared;
using Sidecar.Shared.Models;

namespace Sidecar.Host.Services;

/// <summary>
/// TCPベースのMJPEGストリーミングサーバー
/// </summary>
/// <remarks>
/// <see cref="StreamServer"/> クラスの新しいインスタンスを初期化
/// </remarks>
/// <param name="cameraService">カメラサービス</param>
/// <param name="logger">ロガー</param>
public sealed class StreamServer(ICameraService cameraService, ILogger<StreamServer> logger) : IStreamServer {
    private readonly ConcurrentDictionary<Guid, TcpClient> _clients = new();

    private TcpListener? _listener;
    private CancellationTokenSource? _serverTokenSource;
    private Task? _acceptTask;
    private Task? _broadcastTask;
    private readonly Channel<byte[]> _broadcastChannel = Channel.CreateBounded<byte[]>(
        new BoundedChannelOptions(StreamingConstants.VideoQueueLimit) {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });
    private bool _disposed;

    /// <inheritdoc />
    public int ConnectedClientCount => _clients.Count;

    /// <inheritdoc />
    public bool IsRunning => _acceptTask is not null && !_acceptTask.IsCompleted;

    /// <inheritdoc />
    public Task StartAsync(int port, CancellationToken cancellationToken = default) {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (IsRunning) {
            throw new InvalidOperationException("サーバーは既に実行中");
        }

        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();

        _serverTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // フレーム受信イベントを購読
        cameraService.FrameAvailable += OnFrameAvailable;

        _broadcastTask = ProcessBroadcastQueueAsync(_serverTokenSource.Token);
        _acceptTask = AcceptClientsAsync(_serverTokenSource.Token);

        logger.LogInformation("ストリーミングサーバーをポート {Port} で開始", port);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default) {
        cameraService.FrameAvailable -= OnFrameAvailable;

        if (_serverTokenSource is not null) {
            await _serverTokenSource.CancelAsync();
        }

        // 先にリスナーを停止して新規接続とAcceptLoopを中断
        try {
            _listener?.Stop();
        } catch (Exception ex) {
            logger.LogDebug(ex, "リスナー停止中のエラー");
        }

        // すべてのクライアントを切断
        foreach (var client in _clients.Values.ToArray()) {
            try { client.Close(); } catch { /* 無視 */ }
        }
        _clients.Clear();

        if (_broadcastTask is not null) {
            _ = _broadcastChannel.Writer.TryComplete();
            try {
                await _broadcastTask.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);
            } catch (OperationCanceledException) { } catch (Exception ex) { logger.LogDebug(ex, "ブロードキャストタスク停止中の例外"); }
        }

        if (_acceptTask is not null) {
            try {
                await _acceptTask.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);
            } catch (OperationCanceledException) { } catch (Exception ex) { logger.LogDebug(ex, "受付タスク停止中の例外"); }
        }

        _serverTokenSource?.Dispose();
        _serverTokenSource = null;
        _listener = null;
        _acceptTask = null;
        _broadcastTask = null;

        logger.LogInformation("ストリーミングサーバーを停止");
    }

    /// <summary>
    /// クライアント接続を受け付けるループ
    /// </summary>
    private async Task AcceptClientsAsync(CancellationToken cancellationToken) {
        while (!cancellationToken.IsCancellationRequested && _listener is not null) {
            try {
                var client = await _listener.AcceptTcpClientAsync(cancellationToken);

                // Nagleアルゴリズム無効化（低遅延のため必須）
                client.NoDelay = true;

                var clientId = Guid.NewGuid();
                _ = _clients.TryAdd(clientId, client);

                logger.LogInformation("クライアント接続: {RemoteEndPoint} (ID: {ClientId})", client.Client.RemoteEndPoint, clientId);

                // ハンドシェイクを別タスクで実行
                _ = HandleClientAsync(clientId, client, cancellationToken);
            } catch (OperationCanceledException) {
                break;
            } catch (Exception ex) {
                logger.LogError(ex, "クライアント受付エラー");
            }
        }
    }

    /// <summary>
    /// クライアントのハンドシェイクを処理
    /// </summary>
    private async Task HandleClientAsync(Guid clientId, TcpClient client, CancellationToken cancellationToken) {
        try {
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

            logger.LogDebug("クライアント {ClientId} へハンドシェイク完了", clientId);

            // 接続を維持（フレーム送信はイベントで行う）
            while (!cancellationToken.IsCancellationRequested && client.Connected) {
                await Task.Delay(100, cancellationToken);
            }
        } catch (OperationCanceledException) {
            // キャンセルは正常
        } catch (Exception ex) {
            logger.LogWarning(ex, "クライアント {ClientId} エラー", clientId);
        } finally {
            _ = _clients.TryRemove(clientId, out _);
            client.Close();
            logger.LogInformation("クライアント切断: {ClientId}", clientId);
        }
    }

    /// <summary>
    /// フレーム受信時にすべてのクライアントへブロードキャスト
    /// </summary>
    private void OnFrameAvailable(object? sender, FrameEventArgs e) {
        var frame = e.Frame;
        var boundary = $"\r\n{StreamingConstants.MjpegBoundary}\r\n" +
                       $"Content-Type: image/jpeg\r\n" +
                       $"Content-Length: {frame.JpegData.Length}\r\n" +
                       $"\r\n";

        var boundaryBytes = Encoding.ASCII.GetBytes(boundary);

        // 境界とフレームデータを結合
        var buffer = new byte[boundaryBytes.Length + frame.JpegData.Length];
        boundaryBytes.CopyTo(buffer, 0);
        frame.JpegData.CopyTo(buffer, boundaryBytes.Length);

        // キューに追加
        _ = _broadcastChannel.Writer.TryWrite(buffer);
    }

    private async Task ProcessBroadcastQueueAsync(CancellationToken cancellationToken) {
        try {
            await foreach (var data in _broadcastChannel.Reader.ReadAllAsync(cancellationToken)) {
                // すべてのクライアントに配信
                var clients = _clients.ToArray();
                foreach (var (clientId, client) in clients) {
                    if (!client.Connected || cancellationToken.IsCancellationRequested) {
                        _ = _clients.TryRemove(clientId, out _);
                        continue;
                    }

                    try {
                        var stream = client.GetStream();
                        await stream.WriteAsync(data, cancellationToken);
                        await stream.FlushAsync(cancellationToken);
                    } catch (Exception ex) {
                        logger.LogDebug(ex, "クライアント {ClientId} へのフレーム送信エラー", clientId);
                        _ = _clients.TryRemove(clientId, out _);
                        try { client.Close(); } catch { /* 無視 */ }
                    }
                }
            }
        } catch (OperationCanceledException) { } catch (Exception ex) { logger.LogError(ex, "ビデオブロードキャストキュー処理エラー"); }
    }

    /// <summary>
    /// リソースを解放
    /// </summary>
    public void Dispose() {
        if (_disposed) {
            return;
        }

        // 同期的に停止
        cameraService.FrameAvailable -= OnFrameAvailable;
        _serverTokenSource?.Cancel();
        _ = (_acceptTask?.Wait(TimeSpan.FromSeconds(2)));

        foreach (var client in _clients.Values) {
            try {
                client.Close();
            } catch {
                // 無視
            }
        }

        _clients.Clear();
        _listener?.Stop();
        _serverTokenSource?.Dispose();

        _disposed = true;
    }
}

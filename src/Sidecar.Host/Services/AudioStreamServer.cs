// <copyright file="AudioStreamServer.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Sidecar.Host.Interfaces;
using Sidecar.Shared;
using Sidecar.Shared.Models;

namespace Sidecar.Host.Services;

/// <summary>
/// TCPベースの音声ストリーミングサーバー
/// </summary>
public sealed class AudioStreamServer : IAudioStreamServer
{
    private readonly IAudioService _audioService;
    private readonly ILogger<AudioStreamServer> _logger;
    private readonly ConcurrentDictionary<Guid, TcpClient> _clients = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _serverTokenSource;
    private Task? _acceptTask;
    private bool _disposed;

    /// <inheritdoc/>
    public int ConnectedClientCount => _clients.Count;

    /// <inheritdoc/>
    public bool IsRunning => _acceptTask is not null && !_acceptTask.IsCompleted;

    /// <summary>
    /// <see cref="AudioStreamServer"/> クラスの新しいインスタンスを初期化
    /// </summary>
    /// <param name="audioService">音声サービス</param>
    /// <param name="logger">ロガー</param>
    public AudioStreamServer(IAudioService audioService, ILogger<AudioStreamServer> logger)
    {
        _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public Task StartAsync(int port, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (IsRunning)
        {
            throw new InvalidOperationException("音声サーバーは既に実行中");
        }

        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();

        _serverTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // 音声受信イベントを購読
        _audioService.AudioAvailable += OnAudioAvailable;

        _acceptTask = AcceptClientsAsync(_serverTokenSource.Token);

        _logger.LogInformation("音声ストリーミングサーバーをポート {Port} で開始", port);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _audioService.AudioAvailable -= OnAudioAvailable;

        if (_serverTokenSource is not null)
        {
            await _serverTokenSource.CancelAsync();
        }

        // すべてのクライアントを切断
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

        _logger.LogInformation("音声ストリーミングサーバーを停止");
    }

    private async Task AcceptClientsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener is not null)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(cancellationToken);
                client.NoDelay = true;

                var clientId = Guid.NewGuid();
                _ = _clients.TryAdd(clientId, client);

                _logger.LogInformation("音声クライアント接続: {RemoteEndPoint} (ID: {ClientId})",
                    client.Client.RemoteEndPoint, clientId);

                // クライアント接続を維持
                _ = MonitorClientAsync(clientId, client, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "音声クライアント受付エラー");
            }
        }
    }

    private async Task MonitorClientAsync(Guid clientId, TcpClient client, CancellationToken cancellationToken)
    {
        try
        {
            // 接続を維持
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
            _logger.LogWarning(ex, "音声クライアント {ClientId} エラー", clientId);
        }
        finally
        {
            _ = _clients.TryRemove(clientId, out _);
            client.Close();
            _logger.LogInformation("音声クライアント切断: {ClientId}", clientId);
        }
    }

    private void OnAudioAvailable(object? sender, AudioEventArgs e)
    {
        // 配信処理 (キャプチャスレッドのブロッキングを避けるためバックグラウンドで実行)
        _ = Task.Run(async () =>
        {
            var audio = e.Audio;

            // フレーミング: [4byte長さ][8byteタイムスタンプ][PCMデータ]
            var dataLength = audio.PcmData.Length;
            var frameBuffer = new byte[4 + 8 + dataLength];

            // 長さ (Little Endian)
            BitConverter.TryWriteBytes(frameBuffer.AsSpan(0, 4), dataLength);

            // タイムスタンプ (Little Endian)
            BitConverter.TryWriteBytes(frameBuffer.AsSpan(4, 8), audio.Timestamp);

            // PCMデータ
            audio.PcmData.CopyTo(frameBuffer, 12);

            // すべてのクライアントに配信
            var clients = _clients.ToArray();
            foreach (var (clientId, client) in clients)
            {
                if (!client.Connected || _serverTokenSource?.IsCancellationRequested == true)
                {
                    _ = _clients.TryRemove(clientId, out _);
                    continue;
                }

                try
                {
                    var stream = client.GetStream();
                    await stream.WriteAsync(frameBuffer, _serverTokenSource?.Token ?? CancellationToken.None);
                    await stream.FlushAsync(_serverTokenSource?.Token ?? CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "音声クライアント {ClientId} へのデータ送信エラー", clientId);
                    _ = _clients.TryRemove(clientId, out _);
                    try { client.Close(); } catch { /* 無視 */ }
                }
            }
        });
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;

        _audioService.AudioAvailable -= OnAudioAvailable;
        _serverTokenSource?.Cancel();
        _ = _acceptTask?.Wait(TimeSpan.FromSeconds(2));

        foreach (var client in _clients.Values)
        {
            try { client.Close(); } catch { /* 無視 */ }
        }

        _clients.Clear();
        _listener?.Stop();
        _serverTokenSource?.Dispose();

        _disposed = true;
    }
}

// <copyright file="AudioStreamServer.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Sidecar.Host.Interfaces;
using Sidecar.Shared;
using Sidecar.Shared.Models;

namespace Sidecar.Host.Services;

/// <summary>
/// TCPベースの音声ストリーミングサーバー
/// </summary>
/// <remarks>
/// <see cref="AudioStreamServer"/> クラスの新しいインスタンスを初期化
/// </remarks>
/// <param name="audioService">音声サービス</param>
/// <param name="logger">ロガー</param>
public sealed class AudioStreamServer(IAudioService audioService, ILogger<AudioStreamServer> logger) : IAudioStreamServer {
    private readonly ConcurrentDictionary<Guid, TcpClient> _clients = new();

    private TcpListener? _listener;
    private CancellationTokenSource? _serverTokenSource;
    private Task? _acceptTask;
    private Task? _broadcastTask;
    private readonly Channel<byte[]> _broadcastChannel = Channel.CreateBounded<byte[]>(
        new BoundedChannelOptions(StreamingConstants.AudioQueueLimit) {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });
    private long _totalBytesSent;
    private long _totalPacketsSent;
    private bool _disposed;
#if DEBUG
    private Task? _statsTask;
#endif

    /// <inheritdoc/>
    public int ConnectedClientCount => _clients.Count;

    /// <inheritdoc/>
    public bool IsRunning => _acceptTask is not null && !_acceptTask.IsCompleted;

    /// <inheritdoc/>
    public Task StartAsync(int port, CancellationToken cancellationToken = default) {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (IsRunning) {
            throw new InvalidOperationException("音声サーバーは既に実行中");
        }

        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();

        _serverTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // 音声受信イベントを購読
        audioService.AudioAvailable += OnAudioAvailable;

        _broadcastTask = ProcessBroadcastQueueAsync(_serverTokenSource.Token);
        _acceptTask = AcceptClientsAsync(_serverTokenSource.Token);

#if DEBUG
        _statsTask = LogStatsAsync(_serverTokenSource.Token);
#endif

        logger.LogInformation("音声ストリーミングサーバーをポート {Port} で開始", port);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken = default) {
        audioService.AudioAvailable -= OnAudioAvailable;

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

        logger.LogInformation("音声ストリーミングサーバーを停止");
    }

    private async Task AcceptClientsAsync(CancellationToken cancellationToken) {
        while (!cancellationToken.IsCancellationRequested && _listener is not null) {
            try {
                var client = await _listener.AcceptTcpClientAsync(cancellationToken);
                client.NoDelay = true;

                var clientId = Guid.NewGuid();
                _ = _clients.TryAdd(clientId, client);

                logger.LogInformation("音声クライアント接続: {RemoteEndPoint} (ID: {ClientId})",
                    client.Client.RemoteEndPoint, clientId);

                // クライアント接続を維持
                _ = MonitorClientAsync(clientId, client, cancellationToken);
            } catch (OperationCanceledException) {
                break;
            } catch (Exception ex) {
                logger.LogError(ex, "音声クライアント受付エラー");
            }
        }
    }

    private async Task MonitorClientAsync(Guid clientId, TcpClient client, CancellationToken cancellationToken) {
        try {
            // 接続を維持
            while (!cancellationToken.IsCancellationRequested && client.Connected) {
                await Task.Delay(100, cancellationToken);
            }
        } catch (OperationCanceledException) {
            // キャンセルは正常
        } catch (Exception ex) {
            logger.LogWarning(ex, "音声クライアント {ClientId} エラー", clientId);
        } finally {
            _ = _clients.TryRemove(clientId, out _);
            client.Close();
            logger.LogInformation("音声クライアント切断: {ClientId}", clientId);
        }
    }

    private void OnAudioAvailable(object? sender, AudioEventArgs e) {
        var audio = e.Audio;

        // フレーミング: [4byte長さ][8byteタイムスタンプ][PCMデータ]
        var dataLength = audio.PcmData.Length;
        var frameBuffer = new byte[4 + 8 + dataLength];

        _ = BitConverter.TryWriteBytes(frameBuffer.AsSpan(0, 4), dataLength);
        _ = BitConverter.TryWriteBytes(frameBuffer.AsSpan(4, 8), audio.Timestamp);
        audio.PcmData.CopyTo(frameBuffer, 12);

        // キューに追加
        _ = _broadcastChannel.Writer.TryWrite(frameBuffer);
    }

    private async Task ProcessBroadcastQueueAsync(CancellationToken cancellationToken) {
        try {
            await foreach (var frameBuffer in _broadcastChannel.Reader.ReadAllAsync(cancellationToken)) {
                _ = Interlocked.Add(ref _totalBytesSent, frameBuffer.Length);
                _ = Interlocked.Increment(ref _totalPacketsSent);

                // すべてのクライアントに配信
                var clients = _clients.ToArray();
                foreach (var (clientId, client) in clients) {
                    if (!client.Connected || cancellationToken.IsCancellationRequested) {
                        _ = _clients.TryRemove(clientId, out _);
                        continue;
                    }

                    try {
                        var stream = client.GetStream();
                        await stream.WriteAsync(frameBuffer, cancellationToken);
                        await stream.FlushAsync(cancellationToken);
                    } catch (Exception ex) {
                        logger.LogDebug(ex, "音声クライアント {ClientId} へのデータ送信エラー", clientId);
                        _ = _clients.TryRemove(clientId, out _);
                        try { client.Close(); } catch { /* 無視 */ }
                    }
                }
            }
        } catch (OperationCanceledException) { } catch (Exception ex) { logger.LogError(ex, "ブロードキャストキュー処理エラー"); }
    }

    private async Task LogStatsAsync(CancellationToken cancellationToken) {
        while (!cancellationToken.IsCancellationRequested) {
            try {
                await Task.Delay(1000, cancellationToken);
                var bytes = Interlocked.Exchange(ref _totalBytesSent, 0);
                var packets = Interlocked.Exchange(ref _totalPacketsSent, 0);
                if (packets > 0 || !_clients.IsEmpty) {
                    logger.LogDebug("音声配信統計: {Packets} パケット, {Bytes} バイト, 接続数 {Clients}",
                        packets, bytes, _clients.Count);
                }
            } catch (OperationCanceledException) { break; } catch (Exception ex) { logger.LogError(ex, "統計ログ出力エラー"); }
        }
    }

    /// <inheritdoc/>
    public void Dispose() {
        if (_disposed)
            return;

        audioService.AudioAvailable -= OnAudioAvailable;
        _serverTokenSource?.Cancel();
        _ = _acceptTask?.Wait(TimeSpan.FromSeconds(2));

        foreach (var client in _clients.Values) {
            try { client.Close(); } catch { /* 無視 */ }
        }

        _clients.Clear();
        _listener?.Stop();
        _serverTokenSource?.Dispose();

        _disposed = true;
    }
}

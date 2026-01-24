// <copyright file="AudioClient.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

using System.Net.Sockets;
using Sidecar.Client.Interfaces;
using Sidecar.Shared;
using Sidecar.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Sidecar.Client.Services;

/// <summary>
/// 音声ストリーミングクライアントの実装
/// </summary>
public sealed class AudioClient(ILogger<AudioClient> logger) : IAudioClient
{
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts; // Renamed from _receiveTokenSource
    private Task? _receiveTask;
    private long _totalBytesReceived; // Added
    private long _totalPacketsReceived; // Added
#if DEBUG
    private Task? _statsTask; // Added
#endif
    private ConnectionState _state = ConnectionState.Disconnected;
    private bool _disposed;

    /// <inheritdoc />
    public event EventHandler<AudioEventArgs>? AudioReceived;

    /// <inheritdoc />
    public ConnectionState State
    {
        get => _state;
        private set => _state = value;
    }

    /// <inheritdoc />
    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (State == ConnectionState.Connected || State == ConnectionState.Connecting)
        {
            throw new InvalidOperationException("既に接続中");
        }

        State = ConnectionState.Connecting;

        try
        {
            _client = new TcpClient
            {
                NoDelay = true,
            };

            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(StreamingConstants.ConnectionTimeoutMs);

            await _client.ConnectAsync(host, port, connectCts.Token);

            _stream = _client.GetStream();
            State = ConnectionState.Connected;

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); // Renamed
            _receiveTask = Task.Run(() => ReceiveLoop(_cts.Token), _cts.Token); // Renamed
#if DEBUG
            _statsTask = Task.Run(() => LogStatsAsync(_cts.Token), _cts.Token); // Added
#endif
            logger.LogInformation("音声サーバーに接続: {Host}:{Port}", host, port); // Added
        }
        catch (OperationCanceledException)
        {
            State = ConnectionState.Disconnected;
            throw;
        }
        catch (Exception ex) // Added ex
        {
            logger.LogError(ex, "音声サーバーへの接続に失敗: {Host}:{Port}", host, port); // Added
            State = ConnectionState.Error;
            throw;
        }
    }

    /// <inheritdoc />
    public void Disconnect() {
        _cts?.Cancel(); // Renamed
        _receiveTask?.Wait(TimeSpan.FromSeconds(2));
#if DEBUG
        _statsTask?.Wait(TimeSpan.FromSeconds(2)); // Added
#endif

        _stream?.Dispose();
        _stream = null;

        _client?.Dispose();
        _client = null;

        _cts?.Dispose(); // Renamed
        _cts = null;
        _receiveTask = null;
#if DEBUG
        _statsTask = null; // Added
#endif

        State = ConnectionState.Disconnected;
        logger.LogInformation("音声サーバーから切断"); // Added
    }

    private async Task ReceiveLoop(CancellationToken cancellationToken)
    {
        var headerBuffer = new byte[12]; // 4byte length + 8byte timestamp

        try
        {
            while (!cancellationToken.IsCancellationRequested && _stream is not null)
            {
                // ヘッダーを読み込む
                if (!await ReadExactAsync(_stream, headerBuffer, cancellationToken)) // Kept ReadExactAsync for header
                {
                    break;
                }

                var dataLength = BitConverter.ToInt32(headerBuffer, 0);
                var timestamp = BitConverter.ToInt64(headerBuffer, 4);

                if (dataLength <= 0 || dataLength > 1024 * 1024) // 1MB制限（異常データ防止）
                {
                    logger.LogWarning("無効なデータ長を受信: {DataLength}", dataLength); // Added
                    continue;
                }

                // PCMデータを読み込む
                var pcmData = new byte[dataLength];
                // if (!await ReadExactAsync(_stream, pcmData, cancellationToken)) // Original
                // {
                //     break;
                // }
                await _stream.ReadExactlyAsync(pcmData, cancellationToken); // Changed to ReadExactlyAsync

                Interlocked.Add(ref _totalBytesReceived, dataLength); // Added
                Interlocked.Increment(ref _totalPacketsReceived); // Added

                var audioData = new AudioData(
                    pcmData,
                    StreamingConstants.AudioSampleRate,
                    StreamingConstants.AudioChannels,
                    timestamp);

                AudioReceived?.Invoke(this, new AudioEventArgs(audioData));
            }
        }
        catch (OperationCanceledException)
        {
            // 正常終了
        }
        catch (Exception)
        {
            State = ConnectionState.Error;
        }
        finally
        {
            State = ConnectionState.Disconnected;
        }
    }

    private static async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), cancellationToken);
            if (read == 0) return false;
            totalRead += read;
        }
        return true;
    }

    private async Task LogStatsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(1000, cancellationToken);
                var bytes = Interlocked.Exchange(ref _totalBytesReceived, 0);
                var packets = Interlocked.Exchange(ref _totalPacketsReceived, 0);
                if (packets > 0)
                {
                    logger.LogDebug("音声受信統計: {Packets} パケット, {Bytes} バイト", packets, bytes);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { logger.LogError(ex, "統計ログ出力エラー"); }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        Disconnect();
        _disposed = true;
    }
}

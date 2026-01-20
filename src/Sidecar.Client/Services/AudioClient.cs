// <copyright file="AudioClient.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

using System.Net.Sockets;
using Sidecar.Client.Interfaces;
using Sidecar.Shared;
using Sidecar.Shared.Models;

namespace Sidecar.Client.Services;

/// <summary>
/// 音声ストリーミングクライアントの実装
/// </summary>
public sealed class AudioClient : IAudioClient
{
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _receiveTokenSource;
    private Task? _receiveTask;
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

            _receiveTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _receiveTask = Task.Run(() => ReceiveLoop(_receiveTokenSource.Token), _receiveTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
            State = ConnectionState.Disconnected;
            throw;
        }
        catch (Exception)
        {
            State = ConnectionState.Error;
            throw;
        }
    }

    /// <inheritdoc />
    public void Disconnect()
    {
        _receiveTokenSource?.Cancel();
        _receiveTask?.Wait(TimeSpan.FromSeconds(2));

        _stream?.Dispose();
        _stream = null;

        _client?.Dispose();
        _client = null;

        _receiveTokenSource?.Dispose();
        _receiveTokenSource = null;
        _receiveTask = null;

        State = ConnectionState.Disconnected;
    }

    private async Task ReceiveLoop(CancellationToken cancellationToken)
    {
        var headerBuffer = new byte[12]; // 4byte length + 8byte timestamp

        try
        {
            while (!cancellationToken.IsCancellationRequested && _stream is not null)
            {
                // ヘッダーを読み込む
                if (!await ReadExactAsync(_stream, headerBuffer, cancellationToken))
                {
                    break;
                }

                var dataLength = BitConverter.ToInt32(headerBuffer, 0);
                var timestamp = BitConverter.ToInt64(headerBuffer, 4);

                if (dataLength <= 0 || dataLength > 1024 * 1024) // 1MB制限（異常データ防止）
                {
                    continue;
                }

                // PCMデータを読み込む
                var pcmData = new byte[dataLength];
                if (!await ReadExactAsync(_stream, pcmData, cancellationToken))
                {
                    break;
                }

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

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        Disconnect();
        _disposed = true;
    }
}

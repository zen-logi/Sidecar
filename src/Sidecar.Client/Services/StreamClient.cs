// <copyright file="StreamClient.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

using System.Net.Sockets;
using System.Text;
using Sidecar.Client.Interfaces;
using Sidecar.Shared;
using Sidecar.Shared.Models;

namespace Sidecar.Client.Services;

/// <summary>
/// MJPEGストリームを受信するTCPクライアント。
/// </summary>
public sealed class StreamClient : IStreamClient
{
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _receiveTokenSource;
    private Task? _receiveTask;
    private byte[]? _latestFrame;
    private ConnectionState _state = ConnectionState.Disconnected;
    private bool _disposed;

    /// <inheritdoc />
    public event EventHandler<FrameEventArgs>? FrameReceived;

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
            throw new InvalidOperationException("既に接続中です。");
        }

        State = ConnectionState.Connecting;

        try
        {
            _client = new TcpClient
            {
                NoDelay = true, // Nagleアルゴリズム無効化（低遅延のため必須）
            };

            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(StreamingConstants.ConnectionTimeoutMs);

            await _client.ConnectAsync(host, port, connectCts.Token);

            _stream = _client.GetStream();

            // HTTPヘッダーを読み飛ばす
            await SkipHttpHeaderAsync(_stream, cancellationToken);

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
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_receiveTokenSource is not null)
        {
            await _receiveTokenSource.CancelAsync();
        }

        if (_receiveTask is not null)
        {
            try
            {
                await _receiveTask.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
            }
            catch (TimeoutException)
            {
                // タイムアウトは無視
            }
            catch (OperationCanceledException)
            {
                // キャンセルは正常
            }
        }

        _stream?.Dispose();
        _stream = null;

        _client?.Dispose();
        _client = null;

        _receiveTokenSource?.Dispose();
        _receiveTokenSource = null;
        _receiveTask = null;

        Volatile.Write(ref _latestFrame, null);

        State = ConnectionState.Disconnected;
    }

    /// <inheritdoc />
    public void Disconnect()
    {
        // 同期的な切断（後方互換性のため）
        _receiveTokenSource?.Cancel();
        _receiveTask?.Wait(TimeSpan.FromSeconds(2));

        _stream?.Dispose();
        _stream = null;

        _client?.Dispose();
        _client = null;

        _receiveTokenSource?.Dispose();
        _receiveTokenSource = null;
        _receiveTask = null;

        Volatile.Write(ref _latestFrame, null);

        State = ConnectionState.Disconnected;
    }

    /// <inheritdoc />
    public byte[]? GetLatestFrame() => Volatile.Read(ref _latestFrame);

    /// <summary>
    /// HTTPヘッダーを読み飛ばします。
    /// </summary>
    private static async Task SkipHttpHeaderAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[1];
        var headerEnd = new byte[4];

        // \r\n\r\n を検出するまで読み込み
        while (!cancellationToken.IsCancellationRequested)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, 1), cancellationToken);
            if (read == 0)
            {
                throw new IOException("接続が閉じられました。");
            }

            // バッファをシフト
            headerEnd[0] = headerEnd[1];
            headerEnd[1] = headerEnd[2];
            headerEnd[2] = headerEnd[3];
            headerEnd[3] = buffer[0];

            // \r\n\r\n を検出
            if (headerEnd[0] == '\r' && headerEnd[1] == '\n' && headerEnd[2] == '\r' && headerEnd[3] == '\n')
            {
                break;
            }
        }
    }

    /// <summary>
    /// フレーム受信ループ。
    /// </summary>
    private void ReceiveLoop(CancellationToken cancellationToken)
    {
        var buffer = new byte[StreamingConstants.ReceiveBufferSize];
        using var frameBuffer = new MemoryStream();
        var boundaryBytes = Encoding.ASCII.GetBytes(StreamingConstants.MjpegBoundary);
        long frameNumber = 0;

        try
        {
            while (!cancellationToken.IsCancellationRequested && _stream is not null)
            {
                var read = _stream.Read(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    break;
                }

                // フレームバッファが大きくなりすぎた場合はクリア（Fast-Forward）
                if (frameBuffer.Length > StreamingConstants.ReceiveBufferSize * 2)
                {
                    frameBuffer.SetLength(0);
                }

                frameBuffer.Write(buffer, 0, read);

                // バッファからフレームを抽出
                ExtractFrames(frameBuffer, boundaryBytes, ref frameNumber);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常キャンセル
        }
        catch (IOException)
        {
            State = ConnectionState.Error;
        }
        catch (ObjectDisposedException)
        {
            // 接続終了
        }
    }

    /// <summary>
    /// バッファからフレームを抽出します。
    /// </summary>
    private void ExtractFrames(MemoryStream buffer, byte[] boundaryBytes, ref long frameNumber)
    {
        // GetBuffer() を使用してコピーを避ける
        var bufferArray = buffer.GetBuffer();
        var bufferLength = (int)buffer.Length;
        var data = bufferArray.AsSpan(0, bufferLength).ToArray();

        while (true)
        {
            // 境界文字列を検索
            var boundaryIndex = FindPattern(data, boundaryBytes);
            if (boundaryIndex < 0)
            {
                break;
            }

            // ヘッダー終了位置を検索
            var headerEnd = FindPattern(data, "\r\n\r\n"u8.ToArray(), boundaryIndex);
            if (headerEnd < 0)
            {
                break;
            }

            var contentStart = headerEnd + 4;

            // Content-Length を解析
            var headerSection = Encoding.ASCII.GetString(data, boundaryIndex, headerEnd - boundaryIndex);
            var contentLength = ParseContentLength(headerSection);

            if (contentLength <= 0 || contentStart + contentLength > data.Length)
            {
                break;
            }

            // JPEGデータを抽出
            var jpegData = new byte[contentLength];
            Array.Copy(data, contentStart, jpegData, 0, contentLength);

            // 最新フレームのみ保持（Fast-Forward: 古いフレームは破棄）
            Volatile.Write(ref _latestFrame, jpegData);

            frameNumber++;
            var frame = new FrameData(jpegData, DateTime.UtcNow, frameNumber);

            // イベント発火（スレッドセーフ）
            var handler = FrameReceived;
            handler?.Invoke(this, new FrameEventArgs(frame));

            // 処理済みデータを削除
            var nextStart = contentStart + contentLength;
            data = data[nextStart..];
        }

        // 残りのデータをバッファに戻す
        buffer.SetLength(0);
        buffer.Write(data, 0, data.Length);
    }

    /// <summary>
    /// バイト配列内でパターンを検索します。
    /// </summary>
    private static int FindPattern(byte[] data, byte[] pattern, int startIndex = 0)
    {
        for (var i = startIndex; i <= data.Length - pattern.Length; i++)
        {
            var found = true;
            for (var j = 0; j < pattern.Length; j++)
            {
                if (data[i + j] != pattern[j])
                {
                    found = false;
                    break;
                }
            }

            if (found)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// ヘッダーからContent-Lengthを解析します。
    /// </summary>
    private static int ParseContentLength(string header)
    {
        var lines = header.Split('\n');
        foreach (var line in lines)
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                var value = line["Content-Length:".Length..].Trim();
                if (int.TryParse(value, out var length))
                {
                    return length;
                }
            }
        }

        return -1;
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

        Disconnect();
        _disposed = true;
    }
}

// <copyright file="RelayReceiverService.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Sidecar.Host.Interfaces;
using Sidecar.Shared;
using Sidecar.Shared.Models;

namespace Sidecar.Host.Services;

/// <summary>
/// Mac SenderからTCP経由でJPEGフレームを受信し、IFrameSourceとして公開するリレー受信サービス
/// </summary>
/// <remarks>
/// Length-Prefixedプロトコル（4バイトBig Endianヘッダ + JPEGペイロード）で受信する
/// </remarks>
/// <param name="logger">ロガー</param>
public sealed class RelayReceiverService(ILogger<RelayReceiverService> logger) : IRelayReceiverService {
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;
    private TcpClient? _currentSender;
    private byte[]? _latestFrame;
    private long _frameNumber;
    private bool _disposed;
    private Timer? _noSignalTimer;

    /// <summary>
    /// NO SIGNAL時に使用するJPEG画像のバイト配列
    /// </summary>
    private static readonly Lazy<byte[]> NoSignalImage = new(GenerateNoSignalImage);

    /// <inheritdoc/>
    public event EventHandler<FrameEventArgs>? FrameAvailable;

    /// <inheritdoc/>
    public bool IsActive => IsSenderConnected;

    /// <inheritdoc/>
    public bool IsSenderConnected => _currentSender?.Connected == true;

    /// <inheritdoc/>
    public byte[]? GetLatestFrame() => Volatile.Read(ref _latestFrame);

    /// <inheritdoc/>
    public Task StartAsync(int port, CancellationToken cancellationToken = default) {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_listener is not null)
            throw new InvalidOperationException("リレー受信サービスは既に実行中");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();

        // 接続待ち受けを開始
        _acceptTask = AcceptSenderLoopAsync(_cts.Token);

        // 初期状態はNO SIGNAL
        StartNoSignalTimer();

        logger.LogInformation("リレー受信サービスをポート {Port} で開始", port);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken = default) {
        StopNoSignalTimer();

        if (_cts is not null)
            await _cts.CancelAsync();

        try {
            _listener?.Stop();
        } catch (Exception ex) {
            logger.LogDebug(ex, "リスナー停止中のエラー");
        }

        DisconnectCurrentSender();

        if (_acceptTask is not null) {
            try {
                await _acceptTask.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
            } catch (OperationCanceledException) {
            } catch (Exception ex) {
                logger.LogDebug(ex, "受付タスク停止中の例外");
            }
        }

        _cts?.Dispose();
        _cts = null;
        _listener = null;
        _acceptTask = null;

        logger.LogInformation("リレー受信サービスを停止");
    }

    /// <inheritdoc/>
    public void Dispose() {
        if (_disposed)
            return;

        StopNoSignalTimer();
        _cts?.Cancel();
        DisconnectCurrentSender();
        _listener?.Stop();
        _cts?.Dispose();

        _disposed = true;
    }

    /// <summary>
    /// Sender接続を待ち受けるループ
    /// 同時に1つのSenderのみ接続可能（新しい接続が来たら既存を切断）
    /// </summary>
    private async Task AcceptSenderLoopAsync(CancellationToken cancellationToken) {
        while (!cancellationToken.IsCancellationRequested && _listener is not null) {
            try {
                var client = await _listener.AcceptTcpClientAsync(cancellationToken);
                client.NoDelay = true;
                client.ReceiveBufferSize = StreamingConstants.ReceiveBufferSize;

                logger.LogInformation("Sender接続: {RemoteEndPoint}", client.Client.RemoteEndPoint);

                // 既存のSenderを切断
                DisconnectCurrentSender();
                _currentSender = client;

                // NO SIGNALタイマーを停止
                StopNoSignalTimer();

                // 受信ループを開始
                _ = ReceiveFrameLoopAsync(client, cancellationToken);
            } catch (OperationCanceledException) {
                break;
            } catch (Exception ex) {
                logger.LogError(ex, "Sender受付エラー");
            }
        }
    }

    /// <summary>
    /// Length-Prefixedプロトコルでフレームを受信するループ
    /// ヘッダー（4バイトBig Endian）のサイズ分だけペイロードを読み取る
    /// </summary>
    private async Task ReceiveFrameLoopAsync(TcpClient client, CancellationToken cancellationToken) {
        var headerBuffer = new byte[4];

        try {
            var stream = client.GetStream();

            while (!cancellationToken.IsCancellationRequested && client.Connected) {
                // 1. ヘッダーを読み取り（4バイト = ペイロードサイズ）
                await ReadExactlyAsync(stream, headerBuffer, cancellationToken);
                var payloadSize = BinaryPrimitives.ReadInt32BigEndian(headerBuffer);

                // サイズの妥当性チェック
                if (payloadSize <= 0 || payloadSize > StreamingConstants.MaxRelayPayloadSize) {
                    logger.LogWarning("不正なペイロードサイズ: {Size} bytes", payloadSize);
                    break;
                }

                // 2. ペイロードを読み取り（JPEGデータ）
                var jpegData = new byte[payloadSize];
                await ReadExactlyAsync(stream, jpegData, cancellationToken);

                // 3. 最新フレームを更新
                var frameNumber = Interlocked.Increment(ref _frameNumber);
                var frameData = new FrameData(jpegData, DateTime.UtcNow, frameNumber);

                Volatile.Write(ref _latestFrame, jpegData);
                FrameAvailable?.Invoke(this, new FrameEventArgs(frameData));
            }
        } catch (OperationCanceledException) {
            // キャンセルは正常
        } catch (IOException ex) {
            logger.LogInformation("Sender接続が切断: {Message}", ex.Message);
        } catch (Exception ex) {
            logger.LogError(ex, "フレーム受信エラー");
        } finally {
            logger.LogInformation("Senderとの接続を終了");
            DisconnectCurrentSender();

            // NO SIGNALタイマーを再開
            if (!cancellationToken.IsCancellationRequested)
                StartNoSignalTimer();
        }
    }

    /// <summary>
    /// ストリームから指定サイズ分のデータを確実に読み取る
    /// </summary>
    private static async Task ReadExactlyAsync(NetworkStream stream, byte[] buffer, CancellationToken cancellationToken) {
        var totalRead = 0;
        while (totalRead < buffer.Length) {
            var bytesRead = await stream.ReadAsync(
                buffer.AsMemory(totalRead, buffer.Length - totalRead),
                cancellationToken);

            if (bytesRead == 0)
                throw new IOException("接続が閉じられた");

            totalRead += bytesRead;
        }
    }

    /// <summary>
    /// 現在接続中のSenderを切断
    /// </summary>
    private void DisconnectCurrentSender() {
        if (_currentSender is not null) {
            try {
                _currentSender.Close();
            } catch {
                // 無視
            }
            _currentSender = null;
        }
    }

    /// <summary>
    /// NO SIGNALフレームを定期発行するタイマーを開始
    /// </summary>
    private void StartNoSignalTimer() {
        StopNoSignalTimer();
        _noSignalTimer = new Timer(
            EmitNoSignalFrame,
            null,
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(StreamingConstants.NoSignalIntervalMs));
    }

    /// <summary>
    /// NO SIGNALタイマーを停止
    /// </summary>
    private void StopNoSignalTimer() {
        _noSignalTimer?.Dispose();
        _noSignalTimer = null;
    }

    /// <summary>
    /// NO SIGNAL画像をフレームとして発行するタイマーコールバック
    /// </summary>
    private void EmitNoSignalFrame(object? state) {
        try {
            var jpegData = NoSignalImage.Value;
            var frameNumber = Interlocked.Increment(ref _frameNumber);
            var frameData = new FrameData(jpegData, DateTime.UtcNow, frameNumber);

            Volatile.Write(ref _latestFrame, jpegData);
            FrameAvailable?.Invoke(this, new FrameEventArgs(frameData));
        } catch (Exception ex) {
            logger.LogDebug(ex, "NO SIGNALフレーム発行エラー");
        }
    }

    /// <summary>
    /// OpenCvSharpを使用してNO SIGNAL画像をJPEGバイト配列として生成
    /// </summary>
    private static byte[] GenerateNoSignalImage() {
        using var mat = new OpenCvSharp.Mat(720, 1280, OpenCvSharp.MatType.CV_8UC3, new OpenCvSharp.Scalar(32, 32, 32));

        // "NO SIGNAL" テキストを描画
        OpenCvSharp.Cv2.PutText(
            mat,
            "NO SIGNAL",
            new OpenCvSharp.Point(340, 380),
            OpenCvSharp.HersheyFonts.HersheySimplex,
            2.5,
            new OpenCvSharp.Scalar(200, 200, 200),
            3);

        _ = OpenCvSharp.Cv2.ImEncode(".jpg", mat, out var jpegData,
            [(int)OpenCvSharp.ImwriteFlags.JpegQuality, StreamingConstants.JpegQuality]);

        return jpegData;
    }
}

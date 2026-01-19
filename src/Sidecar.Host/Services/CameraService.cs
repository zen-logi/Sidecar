// <copyright file="CameraService.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Sidecar.Host.Interfaces;
using Sidecar.Shared;
using Sidecar.Shared.Models;

namespace Sidecar.Host.Services;

/// <summary>
/// OpenCVを使用してカメラデバイスからフレームをキャプチャするサービス。
/// </summary>
/// <remarks>
/// <see cref="CameraService"/> クラスの新しいインスタンスを初期化します。
/// </remarks>
/// <param name="logger">ロガー。</param>
public sealed class CameraService(ILogger<CameraService> logger) : ICameraService
{
    private readonly ILogger<CameraService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private VideoCapture? _capture;
    private byte[]? _latestFrame;
    private long _frameNumber;
    private CancellationTokenSource? _captureTokenSource;
    private Task? _captureTask;
    private bool _disposed;

    /// <inheritdoc />
    public event EventHandler<FrameEventArgs>? FrameAvailable;

    /// <inheritdoc />
    public bool IsCapturing => _captureTask is not null && !_captureTask.IsCompleted;

    /// <inheritdoc />
    public IReadOnlyList<CameraDevice> GetAvailableDevices()
    {
        var devices = new List<CameraDevice>();

        // OpenCVでは直接デバイス名を取得できないため、
        // インデックスを試行して利用可能なデバイスを検出
        for (var i = 0; i < 10; i++)
        {
            using var testCapture = new VideoCapture(i);
            if (testCapture.IsOpened())
            {
                devices.Add(new CameraDevice(i, $"Camera {i}"));
                testCapture.Release();
            }
        }

        _logger.LogDebug("{Count} 個のカメラデバイスが見つかりました", devices.Count);
        return devices.AsReadOnly();
    }

    /// <inheritdoc />
    public Task StartCaptureAsync(int deviceIndex, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (IsCapturing)
        {
            throw new InvalidOperationException("キャプチャは既に実行中です。");
        }

        // DirectShowバックエンドを使用（色味の問題回避のため）
        _capture = new VideoCapture(deviceIndex, VideoCaptureAPIs.DSHOW);

        if (!_capture.IsOpened())
        {
            // エラーの詳細を出力
            _logger.LogError("カメラデバイス {DeviceIndex} (DSHOW) のオープンに失敗しました。他のアプリが使用している可能性があります。", deviceIndex);
            throw new InvalidOperationException($"カメラデバイス {deviceIndex} を開けませんでした。");
        }

        // 低遅延設定: バッファサイズを最小化
        _ = _capture.Set(VideoCaptureProperties.BufferSize, 1);

        // フォーマット設定: YUY2 (YUYV) を指定
        // キャプチャボードのRawデータ形式として一般的
        _ = _capture.Set(VideoCaptureProperties.FourCC, VideoWriter.FourCC('Y', 'U', 'Y', '2'));

        _captureTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _captureTask = Task.Run(() => CaptureLoop(_captureTokenSource.Token), _captureTokenSource.Token);

        _logger.LogInformation("カメラ {DeviceIndex} でキャプチャを開始しました (DSHOW/YUY2)", deviceIndex);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopCaptureAsync(CancellationToken cancellationToken = default)
    {
        if (_captureTokenSource is not null)
        {
            await _captureTokenSource.CancelAsync();
        }

        if (_captureTask is not null)
        {
            try
            {
                await _captureTask.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("キャプチャタスクの停止がタイムアウトしました");
            }
            catch (OperationCanceledException)
            {
                // キャンセルは正常
            }
        }

        _capture?.Release();
        _capture?.Dispose();
        _capture = null;

        _captureTokenSource?.Dispose();
        _captureTokenSource = null;
        _captureTask = null;

        _logger.LogInformation("カメラキャプチャを停止しました");
    }

    /// <inheritdoc />
    public byte[]? GetLatestFrame() => Volatile.Read(ref _latestFrame);

    /// <summary>
    /// キャプチャループを実行します。
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    private void CaptureLoop(CancellationToken cancellationToken)
    {
        using var frame = new Mat();
        using var rgbFrame = new Mat(); // 変換用バッファ
        var jpegParams = new[] { (int)ImwriteFlags.JpegQuality, StreamingConstants.JpegQuality };

        while (!cancellationToken.IsCancellationRequested)
        {
            if (_capture is null || !_capture.Read(frame) || frame.Empty())
            {
                continue;
            }

            // YUY2 (YUYV) -> BGR 変換
            // 色が変（紫/緑）な場合、YUV信号がそのまま来ている可能性が高いため変換を噛ませる
            try
            {
                Cv2.CvtColor(frame, rgbFrame, ColorConversionCodes.YUV2BGR_YUY2);
            }
            catch
            {
                // 変換に失敗した場合（既にRGBになっているなど）、そのまま使う
                frame.CopyTo(rgbFrame);
            }

            // JPEG圧縮
            _ = Cv2.ImEncode(".jpg", rgbFrame, out var jpegData, jpegParams);

            var frameNumber = Interlocked.Increment(ref _frameNumber);
            var frameData = new FrameData(jpegData, DateTime.UtcNow, frameNumber);

            // 最新フレームを保持（古いフレームは破棄）
            Volatile.Write(ref _latestFrame, jpegData);

            // イベント発火（スレッドセーフ）
            var handler = FrameAvailable;
            handler?.Invoke(this, new FrameEventArgs(frameData));
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

        // 同期的に停止（Dispose は同期メソッドのため）
        _captureTokenSource?.Cancel();
        _ = (_captureTask?.Wait(TimeSpan.FromSeconds(2)));

        _capture?.Release();
        _capture?.Dispose();
        _captureTokenSource?.Dispose();

        _disposed = true;
    }
}

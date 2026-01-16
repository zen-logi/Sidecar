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
public sealed class CameraService : ICameraService
{
    private readonly ILogger<CameraService> _logger;
    private VideoCapture? _capture;
    private byte[]? _latestFrame;
    private long _frameNumber;
    private CancellationTokenSource? _captureTokenSource;
    private Task? _captureTask;
    private bool _disposed;

    /// <summary>
    /// <see cref="CameraService"/> クラスの新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="logger">ロガー。</param>
    public CameraService(ILogger<CameraService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

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

        _capture = new VideoCapture(deviceIndex);

        if (!_capture.IsOpened())
        {
            throw new InvalidOperationException($"カメラデバイス {deviceIndex} を開けませんでした。");
        }

        // 低遅延設定: バッファサイズを最小化
        _capture.Set(VideoCaptureProperties.BufferSize, 1);

        _captureTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _captureTask = Task.Run(() => CaptureLoop(_captureTokenSource.Token), _captureTokenSource.Token);

        _logger.LogInformation("カメラ {DeviceIndex} でキャプチャを開始しました", deviceIndex);
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
        var jpegParams = new[] { (int)ImwriteFlags.JpegQuality, StreamingConstants.JpegQuality };

        while (!cancellationToken.IsCancellationRequested)
        {
            if (_capture is null || !_capture.Read(frame) || frame.Empty())
            {
                continue;
            }

            // JPEG圧縮
            Cv2.ImEncode(".jpg", frame, out var jpegData, jpegParams);

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
        _captureTask?.Wait(TimeSpan.FromSeconds(2));

        _capture?.Release();
        _capture?.Dispose();
        _captureTokenSource?.Dispose();

        _disposed = true;
    }
}

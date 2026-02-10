using FlashCap;
using Microsoft.Extensions.Logging;
using Sidecar.Host.Interfaces;
using Sidecar.Shared;
using Sidecar.Shared.Models;

namespace Sidecar.Host.Services;

/// <summary>
/// FlashCapを使用したカメラキャプチャサービス
/// </summary>
/// <remarks>
/// <see cref="CameraService"/> クラスの新しいインスタンスを初期化
/// </remarks>
/// <param name="logger">ロガー</param>
/// <param name="formatInterceptor">フォーマット決定サービス</param>
/// <param name="gpuPipeline">GPU処理パイプライン</param>
public sealed class CameraService(
    ILogger<CameraService> logger,
    IFormatInterceptor formatInterceptor,
    IGpuPipelineService gpuPipeline) : ICameraService, IDisposable {
    private CaptureDevice? _captureDevice;
    private VideoCharacteristics? _characteristics;
    private byte[]? _latestFrame;
    private long _frameNumber;
    private bool _disposed;


    /// <inheritdoc/>
    public event EventHandler<FrameEventArgs>? FrameAvailable;

    /// <inheritdoc/>
    public bool IsCapturing => _captureDevice is not null;

    /// <inheritdoc/>
    public IReadOnlyList<CameraDevice> GetAvailableDevices() {

        var devices = new List<CameraDevice>();
        var descriptors = new CaptureDevices();
        var index = 0;
        foreach (var descriptor in descriptors.EnumerateDescriptors()) {
            // FlashCap uses unique IDs, but we'll map to index for compatibility with existing UI
            devices.Add(new CameraDevice(index++, descriptor.Name));
        }
        return devices.AsReadOnly();
    }

    /// <inheritdoc/>
    public async Task StartCaptureAsync(int deviceIndex, CancellationToken cancellationToken = default) {
        if (_captureDevice is not null)
            throw new InvalidOperationException("Capturing already started.");

        var descriptors = new CaptureDevices();
        var descriptorList = descriptors.EnumerateDescriptors().ToList();

        if (deviceIndex < 0 || deviceIndex >= descriptorList.Count) {
            throw new ArgumentOutOfRangeException(nameof(deviceIndex));
        }

        var targetDescriptor = descriptorList[deviceIndex];

        // 優先度順に候補を列挙: JPEG > YUYV > NV12 > その他、同形式内は高解像度優先
        var candidates = targetDescriptor.Characteristics
            .OrderByDescending(c => c.PixelFormat == PixelFormats.JPEG)
            .ThenByDescending(c => c.PixelFormat == PixelFormats.YUYV)
            .ThenByDescending(c => c.Width * c.Height)
            .ToList();

        if (candidates.Count == 0)
            throw new InvalidOperationException("デバイスの有効な特性が見つからない");

        // GPU処理パイプラインを初期化
        gpuPipeline.Initialize();

        // フォールバック付きでデバイスをオープン (設定不可能な候補はスキップ)
        foreach (var candidate in candidates) {
            try {
                logger.LogInformation(
                    "フォーマット試行: {Format}, {Width}x{Height} @ {Fps}",
                    candidate.PixelFormat, candidate.Width, candidate.Height, candidate.FramesPerSecond);

                _captureDevice = await targetDescriptor.OpenAsync(candidate, pixelBufferArrived: OnPixelBufferArrived, transcodeFormat: TranscodeFormats.DoNotTranscode, ct: cancellationToken);
                await _captureDevice.StartAsync(cancellationToken);

                // 成功した場合のみ保持
                _characteristics = candidate;
                formatInterceptor.DetermineFormat(candidate.PixelFormat.ToString());

                logger.LogInformation("Started FlashCap on {DeviceName} ({Format} {Width}x{Height})",
                    targetDescriptor.Name, candidate.PixelFormat, candidate.Width, candidate.Height);
                return;
            } catch (ArgumentException ex) {
                logger.LogWarning("フォーマット設定失敗 ({Format} {Width}x{Height}): {Message}",
                    candidate.PixelFormat, candidate.Width, candidate.Height, ex.Message);
                // 失敗した場合、次の候補へフォールバック
                if (_captureDevice is not null) {
                    _captureDevice.Dispose();
                    _captureDevice = null;
                }
            }
        }

        throw new InvalidOperationException("すべてのフォーマット候補が失敗した");
    }

    /// <summary>
    /// カメラからフレームデータが到着した際のコールバック処理
    /// </summary>
    /// <param name="scope">ピクセルバッファのスコープ</param>
    private void OnPixelBufferArrived(PixelBufferScope scope) {
        try {
            byte[] jpegData;

            // 1. MJPG/JPEGの場合、高速パス (そのままコピー)
            if (_characteristics?.PixelFormat == PixelFormats.JPEG) {
                jpegData = scope.Buffer.CopyImage();
            } else {
                // 2. YUY2/NV12/RGBの場合、GPU処理パイプラインで変換
                var imageData = scope.Buffer.CopyImage();
                var width = _characteristics?.Width ?? 0;
                var height = _characteristics?.Height ?? 0;

                // Interceptorから現在のフォーマット設定を取得
                var inputFormat = formatInterceptor.InputFormat;
                var enableToneMap = formatInterceptor.EnableToneMap;

                // GPU処理実行
                jpegData = gpuPipeline.ProcessFrame(imageData, width, height, inputFormat, enableToneMap);
            }

            var frameNumber = Interlocked.Increment(ref _frameNumber);
            var frameData = new FrameData(jpegData, DateTime.UtcNow, frameNumber);

            Volatile.Write(ref _latestFrame, jpegData);
            FrameAvailable?.Invoke(this, new FrameEventArgs(frameData));
        } catch (Exception ex) {
            logger.LogError(ex, "Error processing frame");
        }
    }

    /// <inheritdoc/>
    public async Task StopCaptureAsync(CancellationToken cancellationToken = default) {
        if (_captureDevice != null) {
            await _captureDevice.StopAsync(cancellationToken);
            _captureDevice.Dispose();
            _captureDevice = null;
        }
        logger.LogInformation("キャプチャを停止");
    }

    /// <inheritdoc/>
    public byte[]? GetLatestFrame() => Volatile.Read(ref _latestFrame);

    /// <inheritdoc/>
    public void Dispose() {
        if (_disposed)
            return;
        _captureDevice?.Dispose();
        _disposed = true;
    }
}

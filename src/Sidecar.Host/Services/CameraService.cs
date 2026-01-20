using FlashCap;
using Microsoft.Extensions.Logging;
using Sidecar.Host.Interfaces;
using Sidecar.Shared;
using Sidecar.Shared.Models;

namespace Sidecar.Host.Services;

public sealed class CameraService : ICameraService, IDisposable
{
    private readonly ILogger<CameraService> _logger;
    private CaptureDevice? _captureDevice;
    private VideoCharacteristics? _characteristics;
    private byte[]? _latestFrame;
    private long _frameNumber;
    private bool _disposed;
    private readonly object _lock = new();

    public event EventHandler<FrameEventArgs>? FrameAvailable;

    public bool IsCapturing => _captureDevice is not null;

    public CameraService(ILogger<CameraService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IReadOnlyList<CameraDevice> GetAvailableDevices()
    {
        var devices = new List<CameraDevice>();
        var descriptors = new CaptureDevices();
        int index = 0;
        foreach (var descriptor in descriptors.EnumerateDescriptors())
        {
            // FlashCap uses unique IDs, but we'll map to index for compatibility with existing UI
            devices.Add(new CameraDevice(index++, descriptor.Name));
        }
        return devices.AsReadOnly();
    }

    public async Task StartCaptureAsync(int deviceIndex, CancellationToken cancellationToken = default)
    {
        if (_captureDevice is not null) throw new InvalidOperationException("Capturing already started.");

        var descriptors = new CaptureDevices();
        var descriptorList = descriptors.EnumerateDescriptors().ToList();
        
        if (deviceIndex < 0 || deviceIndex >= descriptorList.Count)
        {
             throw new ArgumentOutOfRangeException(nameof(deviceIndex));
        }

        var targetDescriptor = descriptorList[deviceIndex];
        
        // 戦略: JPEG/MJPGを優先 (パススルー) > YUYV (変換) > その他
        var characteristics = targetDescriptor.Characteristics
            .OrderByDescending(c => c.PixelFormat == PixelFormats.JPEG)
            .ThenByDescending(c => c.PixelFormat == PixelFormats.YUYV)
            .ThenByDescending(c => c.Width * c.Height) // 高解像度を優先
            .FirstOrDefault();

        if (characteristics == null)
        {
             // フォールバック: 最初に見つかったものを使用
             characteristics = targetDescriptor.Characteristics.FirstOrDefault();
        }

        if (characteristics == null)
        {
             throw new InvalidOperationException("デバイスの有効な特性が見つからない");
        }

        // コールバックで使用するために保持
        _characteristics = characteristics;

        _logger.LogInformation($"Selected Format: {characteristics.PixelFormat}, {characteristics.Width}x{characteristics.Height} @ {characteristics.FramesPerSecond}");

        _captureDevice = await targetDescriptor.OpenAsync(characteristics, OnPixelBufferArrived);
        await _captureDevice.StartAsync(cancellationToken);
        
        _logger.LogInformation($"Started FlashCap on {targetDescriptor.Name}");
    }

    private void OnPixelBufferArrived(PixelBufferScope scope)
    {
        try
        {
            byte[] jpegData;



            // 1. MJPG/JPEGの場合、高速パス (そのままコピー)
            if (_characteristics?.PixelFormat == PixelFormats.JPEG)
            {
                jpegData = scope.Buffer.CopyImage(); 
            }
            else
            {
                // 2. YUY2などの場合、変換を確認
                // FlashCapのExtractImageは通常BMP/RGBに変換する
                // ストリーム用にJPEGに圧縮する必要がある
                // 注意: これはCPU負荷が高い
                
                // FlashCapのデフォルト変換が良くない場合は手動でYUY2を処理する可能性があるが
                // まずは標準のトランスコードを試す
                var imageData = scope.Buffer.ExtractImage();
                
                // imageDataはおそらくBMP形式 (ヘッダー + データ)
                // BMPをJPEGに変換する必要がある
                
                // 参照により OpenCvSharp があるため、柔軟なエンコードに使用可能
                // scope.Buffer.ExtractImage() は完全なBMPファイル配列を返す
                
                // 現状は基本的な抽出に依存する
                // BMPであれば、OpenCVを使用してバッファをデコードし、JPEGにエンコードできる
                using var mat = OpenCvSharp.Cv2.ImDecode(imageData, OpenCvSharp.ImreadModes.Color);
                if (mat.Empty()) return; // デコード失敗
                
                OpenCvSharp.Cv2.ImEncode(".jpg", mat, out jpegData, new[] { (int)OpenCvSharp.ImwriteFlags.JpegQuality, StreamingConstants.JpegQuality });
            }

            var frameNumber = Interlocked.Increment(ref _frameNumber);
            var frameData = new FrameData(jpegData, DateTime.UtcNow, frameNumber);

            Volatile.Write(ref _latestFrame, jpegData);
            FrameAvailable?.Invoke(this, new FrameEventArgs(frameData));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing frame");
        }
    }

    public async Task StopCaptureAsync(CancellationToken cancellationToken = default)
    {
        if (_captureDevice != null)
        {
            await _captureDevice.StopAsync(cancellationToken);
            _captureDevice.Dispose();
            _captureDevice = null;
        }
        _logger.LogInformation("キャプチャを停止");
    }

    public byte[]? GetLatestFrame() => Volatile.Read(ref _latestFrame);

    public void Dispose()
    {
        if (_disposed) return;
        _captureDevice?.Dispose();
        _disposed = true;
    }
}



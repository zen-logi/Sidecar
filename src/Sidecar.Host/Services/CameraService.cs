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
        
        // Strategy: Prefer MJPG (pass-through) > YUYV (convert) > Any
        var characteristics = targetDescriptor.Characteristics
            .OrderByDescending(c => c.PixelFormat == PixelFormats.MJPG)
            .ThenByDescending(c => c.PixelFormat == PixelFormats.YUYV)
            .ThenByDescending(c => c.Width * c.Height) // Prefer higher res
            .FirstOrDefault();

        if (characteristics == null)
        {
             // Fallback to identity (first available)
             characteristics = targetDescriptor.Characteristics.FirstOrDefault();
        }

        if (characteristics == null)
        {
             throw new InvalidOperationException("No valid characteristics found for device.");
        }

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

            // 1. If MJPG, Fast Path! (Pass-through)
            if (scope.Buffer.FrameType == PixelFormats.MJPG)
            {
                jpegData = scope.Buffer.CopyImage(); 
            }
            else
            {
                // 2. If YUY2 or others, verify conversion
                // FlashCap's ExtractImage usually converts to BMP/RGB
                // We then need to compress to JPEG for the stream
                // Note: This is heavier on CPU.
                
                // For direct YUY2 handling, we might want to manually process if FlashCap's default isn't good.
                // But let's try standard transcode first.
                var imageData = scope.Buffer.ExtractImage();
                
                // imageData is likely BMP format (header + data).
                // We need to convert BMP to JPEG.
                // Using OpenCvSharp just for encoding if we have raw bytes? 
                
                // Actually, since we still have OpenCvSharp referenced, we can use it for flexible encoding!
                // scope.Buffer.ExtractImage() returns a full BMP file array.
                // It's better to get raw samples if possible (ReferImage).
                
                // Let's rely on basic extraction for now.
                // If it's BMP, we can use OpenCV to decode buffer and encode JPEG.
                using var mat = OpenCvSharp.Cv2.ImDecode(imageData, OpenCvSharp.ImreadModes.Color);
                if (mat.Empty()) return; // Decode failed
                
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
        _logger.LogInformation("Stopped capture.");
    }

    public byte[]? GetLatestFrame() => Volatile.Read(ref _latestFrame);

    public void Dispose()
    {
        if (_disposed) return;
        _captureDevice?.Dispose();
        _disposed = true;
    }
}



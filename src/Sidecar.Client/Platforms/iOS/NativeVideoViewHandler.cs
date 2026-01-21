#if IOS
using AVFoundation;
using AVKit;
using CoreMedia;
using CoreVideo;
using Foundation;
using Microsoft.Maui.Handlers;
using Sidecar.Client.Controls;
using UIKit;

namespace Sidecar.Client.Platforms.iOS;

public class NativeVideoViewHandler : ViewHandler<NativeVideoView, UIView>
{
    private AVSampleBufferDisplayLayer? _videoLayer;
    private AVPictureInPictureController? _pipController;
    private bool _isPipSupported;
    private PiPPlaybackDelegate? _pipDelegate;

    public NativeVideoViewHandler() : base(ViewMapper)
    {
    }

    public static new IPropertyMapper<NativeVideoView, NativeVideoViewHandler> ViewMapper = new PropertyMapper<NativeVideoView, NativeVideoViewHandler>(ViewHandler.ViewMapper)
    {
        [nameof(NativeVideoView.IsPiPActive)] = MapIsPiPActive,
    };

    protected override UIView CreatePlatformView()
    {
        var view = new UIView();
        _videoLayer = new AVSampleBufferDisplayLayer();
        _videoLayer.VideoGravity = "AVLayerVideoGravityResizeAspect";
        view.Layer.AddSublayer(_videoLayer);

        _isPipSupported = AVPictureInPictureController.IsPictureInPictureSupported;
        if (_isPipSupported)
        {
            _pipDelegate = new PiPPlaybackDelegate();
            var contentSource = new AVPictureInPictureControllerContentSource(_videoLayer, _pipDelegate);
            _pipController = new AVPictureInPictureController(contentSource);
            _pipController.CanStartPictureInPictureAutomaticallyFromInline = true;
        }

        return view;
    }

    protected override void ConnectHandler(UIView platformView)
    {
        base.ConnectHandler(platformView);
        VirtualView.FrameReceived += OnFrameReceived;
    }

    protected override void DisconnectHandler(UIView platformView)
    {
        VirtualView.FrameReceived -= OnFrameReceived;
        _pipController?.Dispose();
        _videoLayer?.Dispose();
        _pipDelegate?.Dispose();
        base.DisconnectHandler(platformView);
    }

    public override void PlatformArrange(Microsoft.Maui.Graphics.Rect rect)
    {
        base.PlatformArrange(rect);
        if (_videoLayer != null)
        {
            _videoLayer.Frame = new CoreGraphics.CGRect(0, 0, rect.Width, rect.Height);
        }
    }

    private void OnFrameReceived(object? sender, byte[] jpegData)
    {
        if (_videoLayer == null || (_videoLayer.Status == AVQueuedSampleBufferRenderingStatus.Failed)) return;

        Task.Run(() => {
            using var data = NSData.FromArray(jpegData);
            using var image = UIImage.LoadFromData(data);
            if (image == null) return;

            var pixelBuffer = CreatePixelBufferFromImage(image);
            if (pixelBuffer == null) return;

            var sampleBuffer = CreateSampleBufferFromPixelBuffer(pixelBuffer);
            if (sampleBuffer == null) return;

            _videoLayer.Enqueue(sampleBuffer);
            pixelBuffer.Dispose();
            sampleBuffer.Dispose();
        });
    }

    private CVPixelBuffer? CreatePixelBufferFromImage(UIImage image)
    {
        var width = (nint)image.Size.Width;
        var height = (nint)image.Size.Height;

        // コンストラクタを使用
        var pixelBuffer = new CVPixelBuffer(width, height, CVPixelFormatType.CV32BGRA);
        if (pixelBuffer == null) return null;

        pixelBuffer.Lock(CVPixelBufferLock.None);
        var baseAddress = pixelBuffer.BaseAddress;
        var bytesPerRow = pixelBuffer.BytesPerRow;

        using (var colorSpace = CoreGraphics.CGColorSpace.CreateDeviceRGB())
        using (var cgContext = new CoreGraphics.CGBitmapContext(
            baseAddress,
            (int)width,
            (int)height,
            8,
            bytesPerRow,
            colorSpace,
            CoreGraphics.CGImageAlphaInfo.PremultipliedFirst))
        {
            cgContext.DrawImage(new CoreGraphics.CGRect(0, 0, (double)width, (double)height), image.CGImage);
        }
        
        pixelBuffer.Unlock(CVPixelBufferLock.None);
        return pixelBuffer;
    }

    private CMSampleBuffer? CreateSampleBufferFromPixelBuffer(CVPixelBuffer pixelBuffer)
    {
        // 前回のエラーに基づいた正確なシグネチャ: CreateForImageBuffer(buffer, out error)
        var videoInfo = CMVideoFormatDescription.CreateForImageBuffer(pixelBuffer, out var error);
        if (error != CMFormatDescriptionError.None || videoInfo == null) return null;

        var timingInfo = new CMSampleTimingInfo
        {
            Duration = CMTime.Invalid,
            PresentationTimeStamp = CMTime.FromSeconds(NSDate.Now.SecondsSinceReferenceDate, 1000),
            DecodeTimeStamp = CMTime.Invalid
        };

        // 前回のエラーに基づいた正確なシグネチャ: CreateForImageBuffer(..., out error)
        return CMSampleBuffer.CreateForImageBuffer(pixelBuffer, true, videoInfo, timingInfo, out var serror);
    }

    private static void MapIsPiPActive(NativeVideoViewHandler handler, NativeVideoView view)
    {
        if (handler._pipController == null) return;

        if (view.IsPiPActive)
        {
            if (!handler._pipController.PictureInPictureActive)
                handler._pipController.StartPictureInPicture();
        }
        else
        {
            if (handler._pipController.PictureInPictureActive)
                handler._pipController.StopPictureInPicture();
        }
    }

    private sealed class PiPPlaybackDelegate : NSObject, IAVPictureInPictureSampleBufferPlaybackDelegate
    {
        public bool IsPlaybackPaused(AVPictureInPictureController pictureInPictureController) => false;
        public void SetPlaying(AVPictureInPictureController pictureInPictureController, bool playing) {}
        public void SkipByInterval(AVPictureInPictureController pictureInPictureController, CMTime skipInterval, Action completionHandler) => completionHandler?.Invoke();
        public void DidTransitionToRenderSize(AVPictureInPictureController pictureInPictureController, CMVideoDimensions newRenderSize) {}
    }
}
#endif

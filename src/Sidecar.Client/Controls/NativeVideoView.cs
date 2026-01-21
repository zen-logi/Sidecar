using Sidecar.Shared.Models;

namespace Sidecar.Client.Controls;

/// <summary>
/// ネイティブの動画表示レイヤーを提供するビュー。
/// iOSでは PiP 対応に AVSampleBufferDisplayLayer を使用するために必要。
/// </summary>
public class NativeVideoView : View
{
    public static readonly BindableProperty IsPiPActiveProperty =
        BindableProperty.Create(nameof(IsPiPActive), typeof(bool), typeof(NativeVideoView), false);

    public bool IsPiPActive
    {
        get => (bool)GetValue(IsPiPActiveProperty);
        set => SetValue(IsPiPActiveProperty, value);
    }

    /// <summary>
    /// 受信したフレームデータをレイヤーに供給する
    /// </summary>
    public void EnqueueFrame(byte[] jpegData)
    {
        FrameReceived?.Invoke(this, jpegData);
    }

    public event EventHandler<byte[]>? FrameReceived;
}

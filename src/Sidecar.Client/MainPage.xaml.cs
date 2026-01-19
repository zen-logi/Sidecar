// <copyright file="MainPage.xaml.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

using Sidecar.Client.ViewModels;
using SkiaSharp;
using SkiaSharp.Views.Maui;

namespace Sidecar.Client;

/// <summary>
/// メインページのコードビハインド。
/// </summary>
public partial class MainPage : ContentPage
{
    private readonly MainPageViewModel _viewModel;
    private SKBitmap? _currentBitmap;
    private readonly object _bitmapLock = new();
    private bool _isRendering;

    /// <summary>
    /// <see cref="MainPage"/> クラスの新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="viewModel">ViewModel。</param>
    public MainPage(MainPageViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        BindingContext = _viewModel;

        _viewModel.FrameUpdated += OnFrameUpdated;
    }

    private volatile int _isDecoding;

    /// <summary>
    /// フレーム更新時のイベントハンドラ。
    /// </summary>
    private void OnFrameUpdated(object? sender, byte[] jpegData)
    {
        // 既にデコード中の場合はスキップ（フレームドロップ）して遅延を防ぐ
        if (Interlocked.CompareExchange(ref _isDecoding, 1, 0) == 1)
        {
            return;
        }

        // 重いデコード処理をスレッドプールで実行
        _ = Task.Run(() =>
        {
            try
            {
                var newBitmap = SKBitmap.Decode(jpegData);
                if (newBitmap is not null)
                {
                    Dispatcher.Dispatch(() =>
                    {
                        lock (_bitmapLock)
                        {
                            _currentBitmap?.Dispose();
                            _currentBitmap = newBitmap;
                        }
                        CanvasView.InvalidateSurface();
                    });
                }
            }
            catch
            {
                // デコードエラーは無視
            }
            finally
            {
                Interlocked.Exchange(ref _isDecoding, 0);
            }
        });
    }

    // Timer Loop Removed

    /// <summary>
    /// キャンバス描画イベントハンドラ。
    /// </summary>
    private void OnCanvasViewPaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        _isRendering = true;

        try
        {
            var canvas = e.Surface.Canvas;
            canvas.Clear(SKColors.Black);

            lock (_bitmapLock)
            {
                if (_currentBitmap is null)
                {
                    return;
                }

                // アスペクト比を維持してスケーリング
                var destRect = CalculateDestRect(_currentBitmap.Width, _currentBitmap.Height, e.Info.Width, e.Info.Height);

                using var paint = new SKPaint();
                paint.IsAntialias = true;
                paint.FilterQuality = SKFilterQuality.Medium;

                // カラーフィルターの適用
                var mode = _viewModel.SelectedColorModeOption.Mode;
                if (mode != ColorMode.Default)
                {
                    paint.ColorFilter = CreateColorFilter(mode);
                }

                canvas.DrawBitmap(_currentBitmap, destRect, paint);
            }
        }
        catch
        {
            // 描画エラーは無視
        }
        finally
        {
            _isRendering = false;
        }
    }

    /// <summary>
    /// 指定されたモードに応じたカラーフィルターを作成します。
    /// </summary>
    private static SKColorFilter? CreateColorFilter(ColorMode mode)
    {
        // R, G, B, A の順序
        // Matrix:
        // R' = R*m0 + G*m1 + B*m2 + A*m3 + m4
        // ...

        float[] matrix;

        switch (mode)
        {
            case ColorMode.HDRToSDR:
                // HDR (Rec.2020) -> SDR (Rec.709) Tone Mapping approximation
                // This is a simplified matrix to compress the gamut and adjust luminance.
                // Assuming the source is delivering BT.2020 values.
                matrix = new float[]
                {
                    1.6605f, -0.5876f, -0.0728f, 0, 0,
                    -0.1246f, 1.1329f, -0.0083f, 0, 0,
                    -0.0182f, -0.1006f, 1.1187f, 0, 0,
                    0, 0, 0, 1, 0
                };
                // NOTE: Real tone mapping requires non-linear processing which ColorMatrix can't fully do,
                // but this matrix does a gamut remap and slight luminance scale.
                break;

            case ColorMode.RescuePurple:
                // YUV to RGB Conversion.
                // If the source sent YUV (YCrCb) data but we treated it as RGB:
                // R channel has Y (Luma), G channel has U (Chroma Blue), B channel has V (Chroma Red)
                // (Or some variation depending on planar packing).
                // Standard YUV conversion:
                // R = Y + 1.402 * (V - 128)
                // G = Y - 0.344136 * (U - 128) - 0.714136 * (V - 128)
                // B = Y + 1.772 * (U - 128)
                // Since inputs are 0-255 normalized to 0-1, we offset 0.5 for 128.
                // Matrix input channel mapping: R=Y, G=U, B=V (Hypothesis: G/B might be swapped)

                // Try Standard Rec.601 YUV -> RGB
                // Y is typically Green in these mixups because it has most detail?
                // Let's assume input R=Y, G=U, B=V.

                // R = 1*R + 0*G + 1.402*B - (1.402 * 0.5)
                // G = 1*R - 0.344*G - 0.714*B + (0.344*0.5 + 0.714*0.5)
                // B = 1*R + 1.772*G + 0*B - (1.772 * 0.5)

                matrix = new float[]
                {
                    1f, 0f, 1.402f, 0f, -0.701f, // R
                    1f, -0.344f, -0.714f, 0f, 0.529f, // G
                    1f, 1.772f, 0f, 0f, -0.886f, // B
                    0f, 0f, 0f, 1f, 0f
                };
                break;

            case ColorMode.SwapRedBlue:
            case ColorMode.BRG: // B is R, R is B
                matrix = new float[]
                {
                    0, 0, 1, 0, 0, // R' <= B
                    0, 1, 0, 0, 0, // G' <= G
                    1, 0, 0, 0, 0, // B' <= R
                    0, 0, 0, 1, 0
                };
                break;

            case ColorMode.SDRDisplayLike:
                // コントラスト強化 + ブライトネス
                matrix = new float[]
                {
                    1.3f, 0, 0, 0, 0.1f,
                    0, 1.3f, 0, 0, 0.1f,
                    0, 0, 1.3f, 0, 0.1f,
                    0, 0, 0, 1, 0
                };
                break;
            
            case ColorMode.RGB:
                return null; // Identity

            case ColorMode.RBG: // Green is Blue, Blue is Green
                matrix = new float[]
                {
                    1, 0, 0, 0, 0,
                    0, 0, 1, 0, 0,
                    0, 1, 0, 0, 0,
                    0, 0, 0, 1, 0
                };
                break;
            
            case ColorMode.GRB: // Red is Green, Green is Red
                matrix = new float[]
                {
                    0, 1, 0, 0, 0,
                    1, 0, 0, 0, 0,
                    0, 0, 1, 0, 0,
                    0, 0, 0, 1, 0
                };
                break;

            case ColorMode.GBR: // Red is Green, Green is Blue, Blue is Red (Cycle)
                matrix = new float[]
                {
                    0, 1, 0, 0, 0, // R' <= G
                    0, 0, 1, 0, 0, // G' <= B
                    1, 0, 0, 0, 0, // B' <= R
                    0, 0, 0, 1, 0
                };
                break;

            case ColorMode.BGR: // Red is Blue, Green is Green, Blue is Red (Wait, SwapRedBlue is Red<->Blue)
                // Actually usually BGR implies the source sends B G R but we read R G B.
                // So R_read = B_actual, B_read = R_actual. This IS SwapRedBlue.
                // Let's assume standard SwapRedBlue logic for BGR.
                matrix = new float[]
                {
                    0, 0, 1, 0, 0,
                    0, 1, 0, 0, 0,
                    1, 0, 0, 0, 0,
                    0, 0, 0, 1, 0
                };
                break;

            case ColorMode.Grayscale:
                matrix = new float[]
                {
                    0.33f, 0.33f, 0.33f, 0, 0,
                    0.33f, 0.33f, 0.33f, 0, 0,
                    0.33f, 0.33f, 0.33f, 0, 0,
                    0, 0, 0, 1, 0
                };
                break;

            case ColorMode.GrayscaleRed:
            case ColorMode.InspectRed:
                // Use Red channel for everything (assuming Red = Y)
                matrix = new float[]
                {
                    1, 0, 0, 0, 0,
                    1, 0, 0, 0, 0,
                    1, 0, 0, 0, 0,
                    0, 0, 0, 1, 0
                };
                break;

            case ColorMode.GrayscaleGreen:
            case ColorMode.InspectGreen:
                // Use Green channel for everything
                matrix = new float[]
                {
                    0, 1, 0, 0, 0,
                    0, 1, 0, 0, 0,
                    0, 1, 0, 0, 0,
                    0, 0, 0, 1, 0
                };
                break;

            case ColorMode.RescueGreen:
                // Hypothesis: Input Green is Y (Luma).
                // Screen is Purple (Red+Blue), so Input Red and Blue are strong.
                // Standard YUV: Y is Luma, U/V are Chroma.
                // Maybe Input Red is Cr (V) and Input Blue is Cb (U)?
                // R = Y + 1.402 * (V - 0.5)
                // G = Y - 0.344 * (U - 0.5) - 0.714 * (V - 0.5)
                // B = Y + 1.772 * (U - 0.5)
                // Mapping: Y=G, V=R, U=B

                matrix = new float[]
                {
                    1.402f, 1f, 0f, 0f, -0.701f, // R = 1*G + 1.402*R - offset
                    -0.714f, 1f, -0.344f, 0f, 0.529f, // G = 1*G - ...
                    0f, 1f, 1.772f, 0f, -0.886f, // B = 1*G + 1.772*B - offset
                    0, 0, 0, 1, 0
                };
                break;

            case ColorMode.RescueGreenVivid:
                // Tuned for Sharpness & Contrast (User reported foggy/washed out image)
                // Contrast: 1.35 (Boosted)
                // Saturation: 0.65 (Slightly relaxed constraint)
                // Brightness Offset: -0.2 (Darken shadows to remove fog)
                
                // Base calculation (Contrast 1.35, Sat 0.65)
                // R = 1.35*G + (1.35*1.402*0.65)*(R-0.5) = 1.35G + 1.23R - 0.615
                // B = 1.35*G + (1.35*1.772*0.65)*(B-0.5) = 1.35G + 1.55B - 0.775
                // G = 1.35*G - (1.35*0.344*0.65)*(B-0.5) - (1.35*0.714*0.65)*(R-0.5)
                //   = 1.35G - 0.30B - 0.63R - (-0.15 - 0.315 = -0.465) -> +0.465
                
                // Applying Brightness Offset of -0.2 to all
                // R_offset = -0.615 - 0.2 = -0.815
                // B_offset = -0.775 - 0.2 = -0.975
                // G_offset = +0.465 - 0.2 = +0.265

                matrix = new float[]
                {
                    1.23f, 1.35f, 0f, 0f, -0.815f,    // R
                    -0.63f, 1.35f, -0.30f, 0f, 0.265f,  // G
                    0f, 1.35f, 1.55f, 0f, -0.975f,    // B
                    0, 0, 0, 1, 0
                };
                break;

            case ColorMode.InspectBlue:
                // Use Blue channel for everything
                matrix = new float[]
                {
                    0, 0, 1, 0, 0,
                    0, 0, 1, 0, 0,
                    0, 0, 1, 0, 0,
                    0, 0, 0, 1, 0
                };
                break;

            default:
                return null;
        }

        return SKColorFilter.CreateColorMatrix(matrix);
    }

    /// <summary>
    /// オーバーレイUIの表示切り替え。
    /// </summary>
    private async void OnOverlayTapped(object? sender, EventArgs e)
    {
        if (ControlsOverlay.Opacity > 0)
        {
            await ControlsOverlay.FadeTo(0, 250);
            ControlsOverlay.InputTransparent = true;
        }
        else
        {
            ControlsOverlay.InputTransparent = false;
            await ControlsOverlay.FadeTo(1, 250);
        }
    }

    /// <summary>
    /// アスペクト比を維持した描画先矩形を計算します。
    /// </summary>
    private static SKRect CalculateDestRect(int srcWidth, int srcHeight, int destWidth, int destHeight)
    {
        var srcAspect = (float)srcWidth / srcHeight;
        var destAspect = (float)destWidth / destHeight;

        float scale;
        float offsetX = 0;
        float offsetY = 0;

        if (srcAspect > destAspect)
        {
            // 横に合わせる
            scale = (float)destWidth / srcWidth;
            offsetY = (destHeight - (srcHeight * scale)) / 2f;
        }
        else
        {
            // 縦に合わせる
            scale = (float)destHeight / srcHeight;
            offsetX = (destWidth - (srcWidth * scale)) / 2f;
        }

        return new SKRect(
            offsetX,
            offsetY,
            offsetX + (srcWidth * scale),
            offsetY + (srcHeight * scale));
    }

    /// <summary>
    /// ページ消滅時のクリーンアップ。
    /// </summary>
    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        _viewModel.FrameUpdated -= OnFrameUpdated;

        lock (_bitmapLock)
        {
            _currentBitmap?.Dispose();
            _currentBitmap = null;
        }
    }
}

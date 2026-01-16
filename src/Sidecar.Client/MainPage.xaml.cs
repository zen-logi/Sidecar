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

        // 定期的な再描画（フレーム更新ポーリング用）
        Dispatcher.StartTimer(TimeSpan.FromMilliseconds(16), OnRenderTick); // ~60fps
    }

    /// <summary>
    /// フレーム更新時のイベントハンドラ。
    /// </summary>
    private void OnFrameUpdated(object? sender, byte[] jpegData)
    {
        // 描画中は新しいフレームをスキップ（Fast-Forward）
        if (_isRendering)
        {
            return;
        }

        try
        {
            var newBitmap = SKBitmap.Decode(jpegData);
            if (newBitmap is not null)
            {
                lock (_bitmapLock)
                {
                    _currentBitmap?.Dispose();
                    _currentBitmap = newBitmap;
                }
            }
        }
        catch
        {
            // デコードエラーは無視
        }
    }

    /// <summary>
    /// レンダリングティック。
    /// </summary>
    private bool OnRenderTick()
    {
        if (_currentBitmap is not null)
        {
            CanvasView.InvalidateSurface();
        }

        return true; // タイマーを継続
    }

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

            SKBitmap? bitmap;
            lock (_bitmapLock)
            {
                bitmap = _currentBitmap;
            }

            if (bitmap is null)
            {
                // 接続待ちメッセージを表示
                using var paint = new SKPaint
                {
                    Color = SKColors.White,
                    TextSize = 24,
                    IsAntialias = true,
                    TextAlign = SKTextAlign.Center,
                };

                canvas.DrawText(
                    "ストリーミング待機中...",
                    e.Info.Width / 2f,
                    e.Info.Height / 2f,
                    paint);

                return;
            }

            // アスペクト比を維持してスケーリング
            var destRect = CalculateDestRect(bitmap.Width, bitmap.Height, e.Info.Width, e.Info.Height);

            canvas.DrawBitmap(bitmap, destRect);
        }
        finally
        {
            _isRendering = false;
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

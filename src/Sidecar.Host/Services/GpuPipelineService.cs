// <copyright file="GpuPipelineService.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

using System.Runtime.InteropServices;
using ComputeSharp;
using Microsoft.Extensions.Logging;
using Sidecar.Host.Interfaces;
using Sidecar.Host.Shaders;
using Sidecar.Shared;

namespace Sidecar.Host.Services;

/// <summary>
/// GPU処理パイプラインサービス (ComputeSharpを使用した映像変換)
/// </summary>
/// <remarks>
/// <see cref="GpuPipelineService"/> クラスの新しいインスタンスを初期化
/// </remarks>
/// <param name="logger">ロガー</param>
public sealed class GpuPipelineService(ILogger<GpuPipelineService> logger) : IGpuPipelineService {
    private GraphicsDevice? _device;
    private ReadWriteBuffer<uint>? _inputBuffer;
    private ReadWriteTexture2D<Float4>? _outputTexture;
    private int _cachedWidth;
    private int _cachedHeight;
    private int _cachedBufferSize;
    private bool _disposed;

    /// <inheritdoc/>
    public void Initialize() {
        if (_device is not null)
            return;

        _device = GraphicsDevice.GetDefault();
        logger.LogInformation("GPU処理パイプライン初期化完了: {DeviceName}", _device.Name);
    }

    /// <inheritdoc/>
    public byte[] ProcessFrame(
        ReadOnlySpan<byte> rawBuffer,
        int width,
        int height,
        VideoInputFormat inputFormat,
        bool enableToneMap) {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_device is null)
            throw new InvalidOperationException("GPU処理パイプラインが初期化されていない");

        // テクスチャ/バッファの再利用 (解像度やバッファサイズが変わった場合のみ再生成)
        EnsureResources(rawBuffer.Length, width, height);

        // RAWバッファをuint配列にパックしてGPUへアップロード
        var uintCount = (rawBuffer.Length + 3) / 4;
        var packed = new uint[uintCount];
        var byteSpan = MemoryMarshal.AsBytes(packed.AsSpan());
        rawBuffer.CopyTo(byteSpan);
        _inputBuffer!.CopyFrom(packed);

        // フォーマットモードの決定
        var formatMode = inputFormat switch {
            VideoInputFormat.Yuy2 => 1,
            VideoInputFormat.Nv12 => 2,
            VideoInputFormat.Uyvy => 3,
            VideoInputFormat.Yvyu => 4,
            VideoInputFormat.Vyuy => 5,
            _ => 0
        };

        // RGBの場合のバイト/ピクセル数を計算
        var bytesPerPixel = formatMode == 0
            ? Math.Max(rawBuffer.Length / (width * height), 3)
            : 3;

        // GPUシェーダーを実行
        var shader = new VideoConversionShader(
            _inputBuffer!,
            _outputTexture!,
            width,
            height,
            formatMode,
            enableToneMap,
            bytesPerPixel);
        _device.For(width, height, shader);

        // GPUからCPUへダウンロード
        var rgbData = new Float4[width * height];
        _outputTexture!.CopyTo(rgbData);

        // Float4からRGB24へ変換
        var rgb24 = ConvertToRgb24(rgbData, width, height);

        // RGB24をJPEGに圧縮
        return EncodeToJpeg(rgb24, width, height);
    }

    /// <inheritdoc/>
    public void Dispose() {
        if (_disposed)
            return;

        _inputBuffer?.Dispose();
        _outputTexture?.Dispose();
        _device?.Dispose();
        _disposed = true;
    }

    /// <summary>
    /// GPU入力バッファと出力テクスチャを確保 (サイズ変更時のみ再生成)
    /// </summary>
    /// <param name="bufferSize">入力バッファのバイト数</param>
    /// <param name="width">画像幅</param>
    /// <param name="height">画像高さ</param>
    private void EnsureResources(int bufferSize, int width, int height) {
        var uintCount = (bufferSize + 3) / 4;

        // 入力バッファの再生成チェック
        if (_inputBuffer is null || _cachedBufferSize != bufferSize) {
            _inputBuffer?.Dispose();
            _inputBuffer = _device!.AllocateReadWriteBuffer<uint>(uintCount);
            _cachedBufferSize = bufferSize;
            logger.LogDebug("入力バッファ再生成: {Size} bytes ({UintCount} uints)", bufferSize, uintCount);
        }

        // 出力テクスチャの再生成チェック
        if (_outputTexture is null || _cachedWidth != width || _cachedHeight != height) {
            _outputTexture?.Dispose();
            _outputTexture = _device!.AllocateReadWriteTexture2D<Float4>(width, height);
            _cachedWidth = width;
            _cachedHeight = height;
            logger.LogDebug("出力テクスチャ再生成: {Width}x{Height}", width, height);
        }
    }

    /// <summary>
    /// Float4配列をRGB24バイト配列に変換
    /// </summary>
    /// <param name="pixels">Float4ピクセル配列</param>
    /// <param name="width">画像幅</param>
    /// <param name="height">画像高さ</param>
    /// <returns>RGB24バイト配列</returns>
    private static byte[] ConvertToRgb24(Float4[] pixels, int width, int height) {
        var rgb24 = new byte[width * height * 3];

        for (var i = 0; i < pixels.Length; i++) {
            var pixel = pixels[i];
            rgb24[i * 3] = (byte)(pixel.X * 255f);
            rgb24[(i * 3) + 1] = (byte)(pixel.Y * 255f);
            rgb24[(i * 3) + 2] = (byte)(pixel.Z * 255f);
        }

        return rgb24;
    }

    /// <summary>
    /// RGB24バイト配列をJPEGに圧縮
    /// </summary>
    /// <param name="rgb24">RGB24バイト配列</param>
    /// <param name="width">画像幅</param>
    /// <param name="height">画像高さ</param>
    /// <returns>JPEG圧縮されたバイト配列</returns>
    private static byte[] EncodeToJpeg(byte[] rgb24, int width, int height) {
        using var mat = new OpenCvSharp.Mat(height, width, OpenCvSharp.MatType.CV_8UC3);
        Marshal.Copy(rgb24, 0, mat.Data, rgb24.Length);

        _ = OpenCvSharp.Cv2.ImEncode(
            ".jpg",
            mat,
            out var jpegData,
            [(int)OpenCvSharp.ImwriteFlags.JpegQuality, StreamingConstants.JpegQuality]);

        return jpegData;
    }
}

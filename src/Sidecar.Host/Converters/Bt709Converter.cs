// <copyright file="Bt709Converter.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

using System.Runtime.CompilerServices;
using Sidecar.Host.Interfaces;

namespace Sidecar.Host.Converters;

/// <summary>
/// BT.709規格に基づいたYUY2からRGBへの高速色空間変換を提供するクラス
/// </summary>
/// <remarks>
/// キャプチャボード等からのHD映像（Switch、PS5等）はBT.709規格を使用する
/// 整数演算とビットシフトにより高速なリアルタイム変換を実現する
/// </remarks>
public sealed class Bt709Converter : IBt709Converter {
    // BT.709 YCbCr→RGB 変換係数（1024倍）
    // R = Y + 1.5748 * V  → 1613
    // G = Y - 0.1873 * U - 0.4681 * V → 192, 479
    // B = Y + 1.8556 * U → 1900
    private const int CoeffRv = 1613;
    private const int CoeffGu = 192;
    private const int CoeffGv = 479;
    private const int CoeffBu = 1900;

    // TVレンジ→フルレンジ変換用係数
    // Y: (Y - 16) * 255 / 219 ≈ (Y - 16) * 298 / 256
    // UV: (UV - 16) * 255 / 224 ≈ (UV - 16) * 291 / 256
    private const int TvRangeYOffset = 16;
    private const int TvRangeYScale = 298;
    private const int TvRangeUvScale = 291;

    /// <inheritdoc/>
    public byte[] ConvertYuy2ToBgr(ReadOnlySpan<byte> yuy2Data, int width, int height, int stride, bool expandTvRange = true) {
        // Strideベースでバッファサイズチェック
        if (yuy2Data.Length < stride * height) {
            throw new ArgumentException($"YUY2データサイズが不正: 期待={stride * height}, 実際={yuy2Data.Length}", nameof(yuy2Data));
        }

        // BGR出力（OpenCVはBGR順序を使用）
        var bgrData = new byte[width * height * 3];

        if (expandTvRange) {
            ConvertWithTvRangeExpansion(yuy2Data, bgrData, width, height, stride);
        } else {
            ConvertFullRange(yuy2Data, bgrData, width, height, stride);
        }

        return bgrData;
    }

    /// <summary>
    /// TVレンジ(16-235)からフルレンジ(0-255)に拡張しながら変換する
    /// </summary>
    /// <param name="yuy2Data">YUY2形式の入力データ</param>
    /// <param name="bgrData">BGR形式の出力バッファ</param>
    /// <param name="width">画像の幅</param>
    /// <param name="height">画像の高さ</param>
    /// <param name="stride">1行あたりのバイト数（パディング含む）</param>
    private static void ConvertWithTvRangeExpansion(ReadOnlySpan<byte> yuy2Data, Span<byte> bgrData, int width, int height, int stride) {
        var bgrIndex = 0;

        for (var y = 0; y < height; y++) {
            // 行の先頭位置を計算（パディング対応）
            var yuy2RowStart = y * stride;

            // 行内でのループ（パディング部分は読まないように width * 2 で止める）
            for (var x = 0; x < width * 2; x += 4) {
                var idx = yuy2RowStart + x;

                // YUY2: [Y0, U, Y1, V] で2ピクセル分
                var y0Raw = yuy2Data[idx];
                var u = yuy2Data[idx + 1];
                var y1Raw = yuy2Data[idx + 2];
                var v = yuy2Data[idx + 3];

                // TVレンジからフルレンジに拡張
                var y0 = ExpandYTvRange(y0Raw);
                var y1 = ExpandYTvRange(y1Raw);
                var uVal = ExpandUvTvRange(u) - 128;
                var vVal = ExpandUvTvRange(v) - 128;

                // 最初のピクセル (Y0, U, V)
                ConvertPixelToBgr(y0, uVal, vVal, bgrData, bgrIndex);
                bgrIndex += 3;

                // 2番目のピクセル (Y1, U, V) - 同じUV値を共有
                ConvertPixelToBgr(y1, uVal, vVal, bgrData, bgrIndex);
                bgrIndex += 3;
            }
        }
    }

    /// <summary>
    /// フルレンジとして変換する（レンジ拡張なし）
    /// </summary>
    /// <param name="yuy2Data">YUY2形式の入力データ</param>
    /// <param name="bgrData">BGR形式の出力バッファ</param>
    /// <param name="width">画像の幅</param>
    /// <param name="height">画像の高さ</param>
    /// <param name="stride">1行あたりのバイト数（パディング含む）</param>
    private static void ConvertFullRange(ReadOnlySpan<byte> yuy2Data, Span<byte> bgrData, int width, int height, int stride) {
        var bgrIndex = 0;

        for (var y = 0; y < height; y++) {
            // 行の先頭位置を計算（パディング対応）
            var yuy2RowStart = y * stride;

            // 行内でのループ（パディング部分は読まないように width * 2 で止める）
            for (var x = 0; x < width * 2; x += 4) {
                var idx = yuy2RowStart + x;

                var y0 = yuy2Data[idx];
                var u = yuy2Data[idx + 1];
                var y1 = yuy2Data[idx + 2];
                var v = yuy2Data[idx + 3];

                var uVal = u - 128;
                var vVal = v - 128;

                ConvertPixelToBgr(y0, uVal, vVal, bgrData, bgrIndex);
                bgrIndex += 3;

                ConvertPixelToBgr(y1, uVal, vVal, bgrData, bgrIndex);
                bgrIndex += 3;
            }
        }
    }

    /// <summary>
    /// BT.709係数を使用してYUV値をBGRに変換する
    /// </summary>
    /// <param name="y">Y（輝度）値</param>
    /// <param name="u">U（青色差）値（128を引いた後）</param>
    /// <param name="v">V（赤色差）値（128を引いた後）</param>
    /// <param name="bgrData">出力BGR配列</param>
    /// <param name="index">書き込み開始インデックス</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ConvertPixelToBgr(int y, int u, int v, Span<byte> bgrData, int index) {
        // BT.709変換（整数演算、1024倍してシフト）
        var r = y + ((CoeffRv * v) >> 10);
        var g = y - ((CoeffGu * u + CoeffGv * v) >> 10);
        var b = y + ((CoeffBu * u) >> 10);

        // BGR順序で出力（OpenCV互換）
        bgrData[index] = ClampToByte(b);
        bgrData[index + 1] = ClampToByte(g);
        bgrData[index + 2] = ClampToByte(r);
    }

    /// <summary>
    /// Y値をTVレンジからフルレンジに拡張する
    /// </summary>
    /// <param name="y">TVレンジのY値</param>
    /// <returns>フルレンジのY値</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ExpandYTvRange(int y) {
        // (Y - 16) * 255 / 219 ≈ (Y - 16) * 298 >> 8
        var result = ((y - TvRangeYOffset) * TvRangeYScale) >> 8;
        return Math.Clamp(result, 0, 255);
    }

    /// <summary>
    /// UV値をTVレンジからフルレンジに拡張する
    /// </summary>
    /// <param name="uv">TVレンジのUV値</param>
    /// <returns>フルレンジのUV値</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ExpandUvTvRange(int uv) {
        // (UV - 16) * 255 / 224 ≈ (UV - 16) * 291 >> 8
        var result = ((uv - TvRangeYOffset) * TvRangeUvScale) >> 8;
        return Math.Clamp(result, 0, 255);
    }

    /// <summary>
    /// 整数値を0-255の範囲にクランプしてbyteに変換する
    /// </summary>
    /// <param name="value">入力値</param>
    /// <returns>クランプされたbyte値</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ClampToByte(int value) => (byte)Math.Clamp(value, 0, 255);
}

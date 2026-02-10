// <copyright file="VideoConversionShader.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

using ComputeSharp;

namespace Sidecar.Host.Shaders;

/// <summary>
/// NV12/YUY2/RGBデコードおよびHDRトーンマッピングを実行するGPUシェーダー
/// </summary>
/// <remarks>
/// formatMode: 0=RGB, 1=YUY2, 2=NV12 で入力フォーマットをディスパッチし、
/// isHdr=true の場合はPQカーブ逆変換+Reinhardトーンマッピングを適用する
/// </remarks>
[ThreadGroupSize(8, 8, 1)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct VideoConversionShader : IComputeShader {
    /// <summary>
    /// 入力バッファ (RAWバイト列をuint単位でパック)
    /// </summary>
    public readonly ReadWriteBuffer<uint> rawInput;

    /// <summary>
    /// 出力テクスチャ (RGB)
    /// </summary>
    public readonly ReadWriteTexture2D<Float4> output;

    /// <summary>
    /// 画像幅
    /// </summary>
    public readonly int width;

    /// <summary>
    /// 画像高さ
    /// </summary>
    public readonly int height;

    /// <summary>
    /// フォーマットモード (0=RGB, 1=YUY2, 2=NV12)
    /// </summary>
    public readonly int formatMode;

    /// <summary>
    /// HDRトーンマッピングを適用する場合true
    /// </summary>
    public readonly bool isHdr;

    /// <summary>
    /// 入力バッファの1ピクセルあたりバイト数 (RGBのみ: 3 or 4)
    /// </summary>
    public readonly int bytesPerPixel;

    /// <summary>
    /// <see cref="VideoConversionShader"/> 構造体の新しいインスタンスを初期化
    /// </summary>
    /// <param name="rawInput">入力バッファ (バイト列をuintパック)</param>
    /// <param name="output">出力テクスチャ</param>
    /// <param name="width">画像幅</param>
    /// <param name="height">画像高さ</param>
    /// <param name="formatMode">フォーマットモード (0=RGB, 1=YUY2, 2=NV12)</param>
    /// <param name="isHdr">HDRトーンマップフラグ</param>
    /// <param name="bytesPerPixel">RGB時の1ピクセルあたりバイト数</param>
    public VideoConversionShader(
        ReadWriteBuffer<uint> rawInput,
        ReadWriteTexture2D<Float4> output,
        int width,
        int height,
        int formatMode,
        bool isHdr,
        int bytesPerPixel) {
        this.rawInput = rawInput;
        this.output = output;
        this.width = width;
        this.height = height;
        this.formatMode = formatMode;
        this.isHdr = isHdr;
        this.bytesPerPixel = bytesPerPixel;
    }

    /// <inheritdoc/>
    public void Execute() {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;

        if (x >= width || y >= height)
            return;

        Float3 rgb;

        if (formatMode == 2) {
            // NV12デコード: Yプレーン + インターリーブUVプレーン
            rgb = DecodeNv12(x, y);
        } else if (formatMode == 1) {
            // YUY2デコード: パック4バイト [Y0, U, Y1, V]
            rgb = DecodeYuy2(x, y);
        } else if (formatMode == 3) {
            // UYVYデコード: パック4バイト [U, Y0, V, Y1]
            rgb = DecodeUyvy(x, y);
        } else {
            // RGBスルーパス
            rgb = DecodeRgb(x, y);
        }

        if (isHdr) {
            // PQカーブ (ST.2084) 逆変換 → Reinhardトーンマッピング
            rgb = ToneMapPqToSdr(rgb);
        }

        output[x, y] = new Float4(rgb, 1.0f);
    }

    /// <summary>
    /// バッファからバイト値を読み出す (uintパックから特定バイトを抽出)
    /// </summary>
    /// <param name="byteIndex">バイトオフセット</param>
    /// <returns>0.0-1.0に正規化された値</returns>
    private float ReadByte(int byteIndex) {
        var uintIndex = byteIndex / 4;
        var byteOffset = byteIndex % 4;
        var packed = rawInput[uintIndex];
        var byteVal = (packed >> (byteOffset * 8)) & 0xFFu;
        return byteVal / 255.0f;
    }

    /// <summary>
    /// NV12フォーマットのデコード (4:2:0 YUV)
    /// </summary>
    /// <param name="x">ピクセルX座標</param>
    /// <param name="y">ピクセルY座標</param>
    /// <returns>デコードされたRGB値</returns>
    private Float3 DecodeNv12(int x, int y) {
        var yPlaneSize = width * height;

        // Yプレーン: 各ピクセルに1バイト
        var yIndex = (y * width) + x;
        var yVal = ReadByte(yIndex);

        // UVプレーン: 2x2ブロック共有, インターリーブ [U, V, U, V, ...]
        var uvRowStart = yPlaneSize + ((y / 2) * width);
        var uvColBase = x & ~1; // 偶数列にアライン
        var uVal = ReadByte(uvRowStart + uvColBase) - 0.5f;
        var vVal = ReadByte(uvRowStart + uvColBase + 1) - 0.5f;

        return YuvToRgb(yVal, uVal, vVal);
    }

    /// <summary>
    /// YUY2フォーマットのデコード (4:2:2 パックYUV)
    /// </summary>
    /// <param name="x">ピクセルX座標</param>
    /// <param name="y">ピクセルY座標</param>
    /// <returns>デコードされたRGB値</returns>
    private Float3 DecodeYuy2(int x, int y) {
        // YUY2: 4バイトで2ピクセル [Y0, U, Y1, V]
        var pixelPairIndex = x / 2;
        var baseByteIndex = ((y * width) + (pixelPairIndex * 2)) * 2;

        var y0 = ReadByte(baseByteIndex);
        var u = ReadByte(baseByteIndex + 1) - 0.5f;
        var y1 = ReadByte(baseByteIndex + 2);
        var v = ReadByte(baseByteIndex + 3) - 0.5f;

        // 偶数ピクセル=Y0, 奇数ピクセル=Y1
        var yVal = (x % 2 == 0) ? y0 : y1;

        return YuvToRgb(yVal, u, v);
    }

    /// <summary>
    /// UYVYフォーマットのデコード (4:2:2 パックYUV)
    /// </summary>
    /// <param name="x">ピクセルX座標</param>
    /// <param name="y">ピクセルY座標</param>
    /// <returns>デコードされたRGB値</returns>
    private Float3 DecodeUyvy(int x, int y) {
        // UYVY: 4バイトで2ピクセル [U, Y0, V, Y1]
        var pixelPairIndex = x / 2;
        var baseByteIndex = ((y * width) + (pixelPairIndex * 2)) * 2;

        var u = ReadByte(baseByteIndex) - 0.5f;
        var y0 = ReadByte(baseByteIndex + 1);
        var v = ReadByte(baseByteIndex + 2) - 0.5f;
        var y1 = ReadByte(baseByteIndex + 3);

        // 偶数ピクセル=Y0, 奇数ピクセル=Y1
        var yVal = (x % 2 == 0) ? y0 : y1;

        return YuvToRgb(yVal, u, v);
    }

    /// <summary>
    /// RGBフォーマットのデコード (パススルー)
    /// </summary>
    /// <param name="x">ピクセルX座標</param>
    /// <param name="y">ピクセルY座標</param>
    /// <returns>RGB値</returns>
    private Float3 DecodeRgb(int x, int y) {
        var byteIndex = ((y * width) + x) * bytesPerPixel;
        var r = ReadByte(byteIndex);
        var g = ReadByte(byteIndex + 1);
        var b = ReadByte(byteIndex + 2);
        return new Float3(r, g, b);
    }

    /// <summary>
    /// YUV (BT.709) からRGBへの変換
    /// </summary>
    /// <param name="y">輝度値 (0.0-1.0)</param>
    /// <param name="u">色差U (-0.5〜0.5)</param>
    /// <param name="v">色差V (-0.5〜0.5)</param>
    /// <returns>クランプ済みRGB値</returns>
    private static Float3 YuvToRgb(float y, float u, float v) {
        var r = y + (1.5748f * v);
        var g = y - (0.1873f * u) - (0.4681f * v);
        var b = y + (1.8556f * u);

        return new Float3(
            Hlsl.Saturate(r),
            Hlsl.Saturate(g),
            Hlsl.Saturate(b));
    }

    /// <summary>
    /// PQカーブ (ST.2084) 逆変換 + Reinhardトーンマッピング
    /// </summary>
    /// <param name="pqSignal">PQエンコードされた信号値</param>
    /// <returns>SDRにマッピングされたRGB値</returns>
    private static Float3 ToneMapPqToSdr(Float3 pqSignal) {
        // 各チャンネルに対してPQ EOTF逆変換を適用
        var linearR = PqEotfInverse(pqSignal.X);
        var linearG = PqEotfInverse(pqSignal.Y);
        var linearB = PqEotfInverse(pqSignal.Z);

        var linear = new Float3(linearR, linearG, linearB);

        // 10000 nit → 1.0 正規化 (SDRは約100nitを想定)
        linear *= 100.0f;

        // Reinhardトーンマッピング
        var mapped = linear / (linear + new Float3(1.0f, 1.0f, 1.0f));

        return new Float3(
            Hlsl.Saturate(mapped.X),
            Hlsl.Saturate(mapped.Y),
            Hlsl.Saturate(mapped.Z));
    }

    /// <summary>
    /// PQ EOTF逆変換 (ST.2084 非線形→リニア)
    /// </summary>
    /// <param name="n">PQエンコード値 (0.0-1.0)</param>
    /// <returns>リニア輝度値 (0.0-1.0, 10000nit正規化)</returns>
    private static float PqEotfInverse(float n) {
        // ST.2084 定数
        const float m1 = 0.1593017578125f;   // 2610/16384
        const float m2 = 78.84375f;           // 2523/32 * 128
        const float c1 = 0.8359375f;          // 3424/4096
        const float c2 = 18.8515625f;         // 2413/128
        const float c3 = 18.6875f;            // 2392/128

        var np = Hlsl.Pow(Hlsl.Max(n, 0.0f), 1.0f / m2);
        var numerator = Hlsl.Max(np - c1, 0.0f);
        var denominator = c2 - (c3 * np);

        return Hlsl.Pow(numerator / Hlsl.Max(denominator, 1e-6f), 1.0f / m1);
    }
}

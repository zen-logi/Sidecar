// <copyright file="IGpuPipelineService.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

namespace Sidecar.Host.Interfaces;

/// <summary>
/// GPU処理パイプラインのインターフェース
/// </summary>
public interface IGpuPipelineService : IDisposable {
    /// <summary>
    /// GPU処理パイプラインを初期化
    /// </summary>
    void Initialize();

    /// <summary>
    /// RAWバッファをGPUで処理してJPEGに変換
    /// </summary>
    /// <param name="rawBuffer">入力RAWバッファ (FlashCapから取得)</param>
    /// <param name="width">画像幅</param>
    /// <param name="height">画像高さ</param>
    /// <param name="inputFormat">入力フォーマット種別</param>
    /// <param name="enableToneMap">HDRトーンマッピングを適用するか</param>
    /// <returns>JPEG圧縮されたバイト配列</returns>
    byte[] ProcessFrame(
        ReadOnlySpan<byte> rawBuffer,
        int width,
        int height,
        VideoInputFormat inputFormat,
        bool enableToneMap);
}

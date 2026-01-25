// <copyright file="IBt709Converter.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

namespace Sidecar.Host.Interfaces;

/// <summary>
/// BT.709規格に基づいたYUY2からRGBへの色空間変換を提供するインターフェース
/// </summary>
public interface IBt709Converter {
    /// <summary>
    /// YUY2形式のピクセルデータをBGR形式に変換する
    /// </summary>
    /// <param name="yuy2Data">YUY2形式の入力データ</param>
    /// <param name="width">画像の幅（ピクセル）</param>
    /// <param name="height">画像の高さ（ピクセル）</param>
    /// <param name="expandTvRange">TVレンジ(16-235)からフルレンジ(0-255)に拡張するかどうか</param>
    /// <returns>BGR形式のピクセルデータ</returns>
    byte[] ConvertYuy2ToBgr(ReadOnlySpan<byte> yuy2Data, int width, int height, bool expandTvRange = true);
}

// <copyright file="FrameData.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

namespace Sidecar.Shared.Models;

/// <summary>
/// フレームデータを表すモデルクラス。
/// </summary>
/// <param name="JpegData">JPEG圧縮されたフレームデータ。</param>
/// <param name="Timestamp">フレームのタイムスタンプ。</param>
/// <param name="FrameNumber">フレーム番号。</param>
public sealed record FrameData(byte[] JpegData, DateTime Timestamp, long FrameNumber);

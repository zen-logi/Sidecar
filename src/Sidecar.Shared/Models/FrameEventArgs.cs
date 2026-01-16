// <copyright file="FrameEventArgs.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

namespace Sidecar.Shared.Models;

/// <summary>
/// フレーム受信イベントの引数。
/// </summary>
/// <remarks>
/// <see cref="FrameEventArgs"/> クラスの新しいインスタンスを初期化します。
/// </remarks>
/// <param name="frame">受信したフレームデータ。</param>
public sealed class FrameEventArgs(FrameData frame) : EventArgs
{

    /// <summary>
    /// 受信したフレームデータを取得します。
    /// </summary>
    public FrameData Frame { get; } = frame;
}

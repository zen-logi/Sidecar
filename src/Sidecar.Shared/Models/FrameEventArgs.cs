// <copyright file="FrameEventArgs.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

namespace Sidecar.Shared.Models;

/// <summary>
/// フレーム受信イベントの引数。
/// </summary>
public sealed class FrameEventArgs : EventArgs
{
    /// <summary>
    /// <see cref="FrameEventArgs"/> クラスの新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="frame">受信したフレームデータ。</param>
    public FrameEventArgs(FrameData frame)
    {
        Frame = frame;
    }

    /// <summary>
    /// 受信したフレームデータを取得します。
    /// </summary>
    public FrameData Frame { get; }
}

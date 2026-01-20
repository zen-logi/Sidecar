// <copyright file="FrameEventArgs.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

namespace Sidecar.Shared.Models;

/// <summary>
/// フレーム受信イベントの引数
/// </summary>
/// <summary>
/// <see cref="FrameEventArgs"/> クラスの新しいインスタンスを初期化
/// </summary>
/// <param name="frame">受信したフレームデータ</param>
public sealed class FrameEventArgs(FrameData frame) : EventArgs
{

    /// <summary>
    /// 受信したフレームデータを取得
    /// </summary>
    public FrameData Frame { get; } = frame;
}

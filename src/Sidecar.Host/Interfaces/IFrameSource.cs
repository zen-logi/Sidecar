// <copyright file="IFrameSource.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

using Sidecar.Shared.Models;

namespace Sidecar.Host.Interfaces;

/// <summary>
/// フレーム供給元を抽象化するインターフェース
/// カメラデバイスやネットワーク中継など、異なるソースを統一的に扱う
/// </summary>
public interface IFrameSource : IDisposable {
    /// <summary>
    /// フレームが利用可能になったときに発生するイベント
    /// </summary>
    event EventHandler<FrameEventArgs>? FrameAvailable;

    /// <summary>
    /// 最新のフレームを取得
    /// </summary>
    /// <returns>JPEG圧縮されたフレームデータ フレームがない場合はnull</returns>
    byte[]? GetLatestFrame();

    /// <summary>
    /// フレームソースがアクティブかどうかを取得
    /// </summary>
    bool IsActive { get; }
}

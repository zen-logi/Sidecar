// <copyright file="IOrientationService.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

namespace Sidecar.Client.Interfaces;

/// <summary>
/// 画面の向き制御サービスのインターフェース
/// </summary>
public interface IOrientationService
{
    /// <summary>
    /// 横画面にロック
    /// </summary>
    void LockLandscape();

    /// <summary>
    /// 縦画面にロック
    /// </summary>
    void LockPortrait();

    /// <summary>
    /// 画面ロックを解除
    /// </summary>
    void Unlock();

    /// <summary>
    /// 現在横画面かどうか
    /// </summary>
    bool IsLandscape { get; }
}

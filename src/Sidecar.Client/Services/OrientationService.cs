// <copyright file="OrientationService.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

using Sidecar.Client.Interfaces;

namespace Sidecar.Client.Services;

/// <summary>
/// デスクトップ向け画面の向き制御サービス（スタブ実装）
/// Windows/macOS では画面回転は不要なため何もしない
/// </summary>
public class OrientationService : IOrientationService
{
    /// <inheritdoc />
    public bool IsLandscape => true;

    /// <inheritdoc />
    public void LockLandscape()
    {
        // デスクトップでは不要
    }

    /// <inheritdoc />
    public void LockPortrait()
    {
        // デスクトップでは不要
    }

    /// <inheritdoc />
    public void Unlock()
    {
        // デスクトップでは不要
    }
}

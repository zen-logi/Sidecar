// <copyright file="CameraFrameSource.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

using Sidecar.Host.Interfaces;
using Sidecar.Shared.Models;

namespace Sidecar.Host.Services;

/// <summary>
/// ICameraServiceをIFrameSourceとしてラップするアダプター
/// 既存のカメラキャプチャをフレームソースとして公開する
/// </summary>
/// <param name="cameraService">カメラサービス</param>
public sealed class CameraFrameSource(ICameraService cameraService) : IFrameSource {
    private bool _disposed;

    /// <inheritdoc/>
    public event EventHandler<FrameEventArgs>? FrameAvailable {
        add => cameraService.FrameAvailable += value;
        remove => cameraService.FrameAvailable -= value;
    }

    /// <inheritdoc/>
    public bool IsActive => cameraService.IsCapturing;

    /// <inheritdoc/>
    public byte[]? GetLatestFrame() => cameraService.GetLatestFrame();

    /// <inheritdoc/>
    public void Dispose() {
        if (_disposed)
            return;
        // CameraServiceのライフサイクルはDIコンテナが管理するため、ここでは解放しない
        _disposed = true;
    }
}

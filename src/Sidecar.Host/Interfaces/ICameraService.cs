// <copyright file="ICameraService.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

using Sidecar.Shared.Models;

namespace Sidecar.Host.Interfaces;

/// <summary>
/// キャプチャデバイスからフレームを取得するサービスのインターフェース。
/// </summary>
public interface ICameraService : IDisposable
{
    /// <summary>
    /// 利用可能なカメラデバイスを列挙します。
    /// </summary>
    /// <returns>利用可能なカメラデバイスのリスト。</returns>
    IReadOnlyList<CameraDevice> GetAvailableDevices();

    /// <summary>
    /// 指定したカメラデバイスでキャプチャを開始します。
    /// </summary>
    /// <param name="deviceIndex">使用するカメラデバイスのインデックス。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <returns>非同期操作を表すタスク。</returns>
    Task StartCaptureAsync(int deviceIndex, CancellationToken cancellationToken = default);

    /// <summary>
    /// キャプチャを停止します。
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <returns>非同期操作を表すタスク。</returns>
    Task StopCaptureAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 最新のフレームを取得します。
    /// </summary>
    /// <returns>JPEG圧縮されたフレームデータ。フレームがない場合はnull。</returns>
    byte[]? GetLatestFrame();

    /// <summary>
    /// フレームが利用可能になったときに発生するイベント。
    /// </summary>
    event EventHandler<FrameEventArgs>? FrameAvailable;

    /// <summary>
    /// キャプチャが実行中かどうかを取得します。
    /// </summary>
    bool IsCapturing { get; }
}

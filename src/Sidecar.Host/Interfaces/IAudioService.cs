// <copyright file="IAudioService.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

using Sidecar.Shared.Models;

namespace Sidecar.Host.Interfaces;

/// <summary>
/// 音声キャプチャサービスのインターフェース
/// </summary>
public interface IAudioService : IDisposable
{
    /// <summary>
    /// 利用可能な音声デバイスを列挙
    /// </summary>
    /// <returns>利用可能な音声デバイスのリスト。</returns>
    IReadOnlyList<AudioDevice> GetAvailableDevices();

    /// <summary>
    /// 指定した音声デバイスでキャプチャを開始
    /// </summary>
    /// <param name="deviceId">使用する音声デバイスのID。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <returns>非同期操作を表すタスク。</returns>
    Task StartCaptureAsync(string deviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// キャプチャを停止
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>非同期操作を表すタスク</returns>
    Task StopCaptureAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 音声データが利用可能になったときに発生するイベント
    /// </summary>
    event EventHandler<AudioEventArgs>? AudioAvailable;

    /// <summary>
    /// キャプチャが実行中かどうかを取得
    /// </summary>
    bool IsCapturing { get; }
}

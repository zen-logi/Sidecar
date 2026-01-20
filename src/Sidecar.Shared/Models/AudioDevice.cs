// <copyright file="AudioDevice.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

namespace Sidecar.Shared.Models;

/// <summary>
/// 音声デバイスの種類
/// </summary>
public enum AudioDeviceType
{
    /// <summary>
    /// キャプチャボード (WASAPI キャプチャ)
    /// </summary>
    CaptureBoard,

    /// <summary>
    /// システム音声 (WASAPI Loopback)
    /// </summary>
    SystemAudio,

    /// <summary>
    /// マイク入力
    /// </summary>
    Microphone,

    /// <summary>
    /// マイク + キャプチャボード/システム音声のミックス
    /// </summary>
    Mixed,
}

/// <summary>
/// 音声デバイスを表すモデル
/// </summary>
/// <param name="Id">デバイスの一意識別子</param>
/// <param name="Name">デバイスの表示名</param>
/// <param name="Type">デバイスの種類</param>
public record AudioDevice(string Id, string Name, AudioDeviceType Type)
{
    /// <inheritdoc/>
    public override string ToString() => Name;
}

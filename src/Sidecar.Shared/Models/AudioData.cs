// <copyright file="AudioData.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

namespace Sidecar.Shared.Models;

/// <summary>
/// 音声データを表すモデル
/// </summary>
/// <param name="PcmData">PCM生データ</param>
/// <param name="SampleRate">サンプリングレート (Hz)</param>
/// <param name="Channels">チャンネル数</param>
/// <param name="Timestamp">キャプチャ時のタイムスタンプ</param>
public record AudioData(byte[] PcmData, int SampleRate, int Channels, long Timestamp);

/// <summary>
/// 音声データのイベント引数
/// </summary>
/// <param name="Audio">音声データ</param>
public sealed class AudioEventArgs(AudioData Audio) : EventArgs
{
    /// <summary>
    /// 音声データを取得
    /// </summary>
    public AudioData Audio { get; } = Audio;
}

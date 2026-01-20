// <copyright file="IAudioPlayerService.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

namespace Sidecar.Client.Interfaces;

/// <summary>
/// 音声再生サービスのインターフェース。
/// </summary>
public interface IAudioPlayerService : IDisposable
{
    /// <summary>
    /// 音声再生を開始します。
    /// </summary>
    /// <param name="sampleRate">サンプリングレート (Hz)。</param>
    /// <param name="channels">チャンネル数。</param>
    void Start(int sampleRate, int channels);

    /// <summary>
    /// 音声再生を停止します。
    /// </summary>
    void Stop();

    /// <summary>
    /// PCM音声データを再生キューに追加します。
    /// </summary>
    /// <param name="pcmData">PCM音声データ。</param>
    void AddSamples(byte[] pcmData);

    /// <summary>
    /// 音量を取得または設定します（0.0 〜 1.0）。
    /// </summary>
    float Volume { get; set; }

    /// <summary>
    /// ミュート状態を取得または設定します。
    /// </summary>
    bool IsMuted { get; set; }

    /// <summary>
    /// 再生中かどうかを取得します。
    /// </summary>
    bool IsPlaying { get; }
}

// <copyright file="AudioPlayerService.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

using Sidecar.Client.Interfaces;

#if WINDOWS
using NAudio.Wave;
#endif

namespace Sidecar.Client.Services;

/// <summary>
/// Windows環境におけるNAudioを使用した音声再生サービス
/// </summary>
public sealed class AudioPlayerService : IAudioPlayerService
{
    private bool _isMuted;
    private float _volume = 1.0f;
    private bool _isPlaying;
    private bool _disposed;

#if WINDOWS
    private IWavePlayer? _waveOut;
    private BufferedWaveProvider? _bufferedWaveProvider;
#endif

    /// <inheritdoc />
    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0.0f, 1.0f);
#if WINDOWS
            if (_waveOut != null)
            {
                _waveOut.Volume = _isMuted ? 0 : _volume;
            }
#endif
        }
    }

    /// <inheritdoc />
    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            _isMuted = value;
#if WINDOWS
            if (_waveOut != null)
            {
                _waveOut.Volume = _isMuted ? 0 : _volume;
            }
#endif
        }
    }

    /// <inheritdoc />
    public bool IsPlaying => _isPlaying;

    /// <summary>
    /// 音声再生を開始
    /// </summary>
    public void Start(int sampleRate, int channels)
    {
        if (_isPlaying) return;

#if WINDOWS
        var waveFormat = new WaveFormat(sampleRate, channels);
        _bufferedWaveProvider = new BufferedWaveProvider(waveFormat)
        {
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromSeconds(5)
        };

        _waveOut = new WaveOutEvent
        {
            Volume = _isMuted ? 0 : _volume
        };
        _waveOut.Init(_bufferedWaveProvider);
        _waveOut.Play();
#endif
        _isPlaying = true;
    }

    /// <summary>
    /// 音声再生を停止
    /// </summary>
    public void Stop()
    {
        if (!_isPlaying) return;

#if WINDOWS
        _waveOut?.Stop();
        _waveOut?.Dispose();
        _waveOut = null;
        _bufferedWaveProvider = null;
#endif
        _isPlaying = false;
    }

    /// <inheritdoc />
    public void AddSamples(byte[] pcmData)
    {
        if (!_isPlaying) return;

#if WINDOWS
        _bufferedWaveProvider?.AddSamples(pcmData, 0, pcmData.Length);
#endif
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
    }
}

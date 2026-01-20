// <copyright file="AudioPlayerService.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

using Sidecar.Client.Interfaces;

#if WINDOWS
using NAudio.Wave;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
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
    private MMDeviceEnumerator? _deviceEnumerator;
    private DeviceNotificationClient? _notificationClient;
    private int _sampleRate;
    private int _channels;
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

        _sampleRate = sampleRate;
        _channels = channels;

#if WINDOWS
        InitializePlayer();

        // デバイス変更の監視を開始
        try
        {
            _deviceEnumerator = new MMDeviceEnumerator();
            _notificationClient = new DeviceNotificationClient(() =>
            {
                // デバイスが変更されたら再初期化
                if (_isPlaying)
                {
                    Stop();
                    Start(_sampleRate, _channels);
                }
            });
            _deviceEnumerator.RegisterEndpointNotificationCallback(_notificationClient);
        }
        catch (Exception)
        {
            // 通知に失敗しても再生自体は継続
        }
#endif
        _isPlaying = true;
    }

#if WINDOWS
    private void InitializePlayer()
    {
        var waveFormat = new WaveFormat(_sampleRate, _channels);
        _bufferedWaveProvider = new BufferedWaveProvider(waveFormat)
        {
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromSeconds(5)
        };

        // WASAPI Shared モードを使用してデフォルトデバイスで再生
        _waveOut = new WasapiOut(AudioClientShareMode.Shared, 100);
        _waveOut.Init(_bufferedWaveProvider);
        _waveOut.Volume = _isMuted ? 0 : _volume;
        _waveOut.Play();
    }

    private class DeviceNotificationClient(Action onDeviceChanged) : IMMNotificationClient
    {
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (flow == DataFlow.Render && (role == Role.Console || role == Role.Multimedia))
            {
                onDeviceChanged();
            }
        }

        public void OnDeviceAdded(string pwstrDeviceId) { }
        public void OnDeviceRemoved(string pwstrDeviceId) { }
        public void OnDeviceStateChanged(string pwstrDeviceId, DeviceState dwNewState) { }
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }
#endif

    /// <summary>
    /// 音声再生を停止
    /// </summary>
    public void Stop()
    {
        if (!_isPlaying) return;

#if WINDOWS
        if (_deviceEnumerator != null && _notificationClient != null)
        {
            try
            {
                _deviceEnumerator.UnregisterEndpointNotificationCallback(_notificationClient);
            }
            catch { }
        }

        _waveOut?.Stop();
        _waveOut?.Dispose();
        _waveOut = null;
        _bufferedWaveProvider = null;
        _deviceEnumerator?.Dispose();
        _deviceEnumerator = null;
        _notificationClient = null;
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

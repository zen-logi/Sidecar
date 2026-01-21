using Sidecar.Client.Interfaces;

#if WINDOWS
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
#elif IOS || MACCATALYST
using AVFoundation;
using AudioToolbox;
using Foundation;
#endif

namespace Sidecar.Client.Services;

/// <summary>
/// 各プラットフォーム向けの音声再生サービス
/// </summary>
public sealed class AudioPlayerService : IAudioPlayerService
{
    private bool _isMuted;
    private float _volume = 1.0f;
    private bool _isPlaying;
    private bool _disposed;
    private int _sampleRate;
    private int _channels;

#if WINDOWS
    private IWavePlayer? _waveOut;
    private BufferedWaveProvider? _bufferedWaveProvider;
    private VolumeSampleProvider? _volumeProvider;
    private MMDeviceEnumerator? _deviceEnumerator;
    private DeviceNotificationClient? _notificationClient;
#elif IOS || MACCATALYST
    private AVAudioEngine? _audioEngine;
    private AVAudioPlayerNode? _playerNode;
    private AVAudioFormat? _audioFormat;
    private readonly System.Collections.Concurrent.ConcurrentQueue<AVAudioPcmBuffer> _bufferQueue = new();
#endif

    /// <inheritdoc />
    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0.0f, 1.0f);
#if WINDOWS
            if (_volumeProvider != null)
            {
                _volumeProvider.Volume = _isMuted ? 0 : _volume;
            }
#elif IOS || MACCATALYST
            if (_playerNode != null)
            {
                _playerNode.Volume = _isMuted ? 0 : _volume;
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
            if (_volumeProvider != null)
            {
                _volumeProvider.Volume = _isMuted ? 0 : _volume;
            }
#elif IOS || MACCATALYST
            if (_playerNode != null)
            {
                _playerNode.Volume = _isMuted ? 0 : _volume;
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
        InitializeWindowsPlayer();
#elif IOS || MACCATALYST
        InitializeApplePlayer();
#endif
        _isPlaying = true;
    }

#if WINDOWS
    private void InitializeWindowsPlayer()
    {
        var waveFormat = new WaveFormat(_sampleRate, _channels);
        _bufferedWaveProvider = new BufferedWaveProvider(waveFormat)
        {
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromSeconds(5)
        };

        _volumeProvider = new VolumeSampleProvider(_bufferedWaveProvider.ToSampleProvider())
        {
            Volume = _isMuted ? 0 : _volume
        };

        _waveOut = new WasapiOut(AudioClientShareMode.Shared, 100);
        _waveOut.Init(_volumeProvider);
        _waveOut.Play();

        try
        {
            _deviceEnumerator = new MMDeviceEnumerator();
            _notificationClient = new DeviceNotificationClient(() =>
            {
                if (_isPlaying)
                {
                    Stop();
                    Start(_sampleRate, _channels);
                }
            });
            _deviceEnumerator.RegisterEndpointNotificationCallback(_notificationClient);
        }
        catch { }
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
#elif IOS || MACCATALYST
    private void InitializeApplePlayer()
    {
#if IOS
        try
        {
            var session = AVAudioSession.SharedInstance();
            session.SetCategory(AVAudioSessionCategory.Playback, 
                AVAudioSessionCategoryOptions.DefaultToSpeaker | 
                AVAudioSessionCategoryOptions.MixWithOthers);
            session.SetMode(AVAudioSessionMode.MoviePlayback, out _); // 同期重視のモード
            session.SetPreferredSampleRate(48000, out _);             // 48kHzを明示的に要求
            session.SetActive(true);
        }
        catch { /* Ignore */ }
#endif

        _audioEngine = new AVAudioEngine();
        _playerNode = new AVAudioPlayerNode();
        _audioEngine.AttachNode(_playerNode);

        _audioFormat = new AVAudioFormat(AVAudioCommonFormat.PCMFloat32, (double)_sampleRate, (uint)_channels, false);

        _audioEngine.Connect(_playerNode, _audioEngine.MainMixerNode, _audioFormat);

        NSError error;
        _audioEngine.StartAndReturnError(out error);
        
        if (error == null)
        {
            _playerNode.Play();
            _playerNode.Volume = _isMuted ? 0 : _volume;
        }
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
            try { _deviceEnumerator.UnregisterEndpointNotificationCallback(_notificationClient); } catch { }
        }
        _waveOut?.Stop();
        _waveOut?.Dispose();
        _waveOut = null;
        _volumeProvider = null;
        _bufferedWaveProvider = null;
        _deviceEnumerator?.Dispose();
        _deviceEnumerator = null;
        _notificationClient = null;
#elif IOS || MACCATALYST
        _playerNode?.Stop();
        _audioEngine?.Stop();
        _playerNode?.Dispose();
        _audioEngine?.Dispose();
        _playerNode = null;
        _audioEngine = null;
        _audioFormat = null;
        while (_bufferQueue.TryDequeue(out _)) { }
#endif
        _isPlaying = false;
    }

    /// <inheritdoc />
    public void AddSamples(byte[] pcmData)
    {
        if (!_isPlaying) return;

#if WINDOWS
        _bufferedWaveProvider?.AddSamples(pcmData, 0, pcmData.Length);
#elif IOS || MACCATALYST
        if (_playerNode == null || _audioFormat == null || !_audioEngine!.Running) return;

        // byte[] (S16 Interleaved) -> float[] (Deinterleaved) に変換
        int sampleCount = pcmData.Length / 2;
        int frameCount = sampleCount / _channels;
        
        var buffer = new AVAudioPcmBuffer(_audioFormat, (uint)frameCount);
        buffer.FrameLength = (uint)frameCount;

        unsafe
        {
            float* leftPtr = (float*)((nint*)buffer.FloatChannelData)[0];
            float* rightPtr = _channels > 1 ? (float*)((nint*)buffer.FloatChannelData)[1] : null;

            fixed (byte* bytePtr = pcmData)
            {
                short* shortPtr = (short*)bytePtr;
                for (int i = 0; i < frameCount; i++)
                {
                    if (_channels == 2)
                    {
                        leftPtr[i] = shortPtr[i * 2] / 32768f;
                        if (rightPtr != null)
                        {
                            rightPtr[i] = shortPtr[i * 2 + 1] / 32768f;
                        }
                    }
                    else
                    {
                        leftPtr[i] = shortPtr[i] / 32768f;
                    }
                }
            }
        }

        // 参照を保持してGCされないようにする
        _bufferQueue.Enqueue(buffer);
        
        // 再生終了時にキューから削除する
        _playerNode.ScheduleBuffer(buffer, () => {
            _bufferQueue.TryDequeue(out _);
        });
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

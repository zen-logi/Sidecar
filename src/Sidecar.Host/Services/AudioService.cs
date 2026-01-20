// <copyright file="AudioService.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Sidecar.Host.Interfaces;
using Sidecar.Shared;
using Sidecar.Shared.Models;

namespace Sidecar.Host.Services;

/// <summary>
/// NAudioを使用した音声キャプチャサービス
/// </summary>
public sealed class AudioService : IAudioService
{
    private readonly ILogger<AudioService> _logger;
    private IWaveIn? _waveIn;
    private WaveFormat? _targetFormat;
    private BufferedWaveProvider? _captureProvider;
    private IWaveProvider? _conversionStream;
    private byte[] _conversionBuffer = new byte[4096]; // 低遅延化のためバッファサイズを縮小
    private bool _disposed;

    /// <inheritdoc/>
    public event EventHandler<AudioEventArgs>? AudioAvailable;

    /// <inheritdoc/>
    public bool IsCapturing => _waveIn is not null;

    /// <summary>
    /// <see cref="AudioService"/> クラスの新しいインスタンスを初期化
    /// </summary>
    /// <param name="logger">ロガー</param>
    public AudioService(ILogger<AudioService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public IReadOnlyList<AudioDevice> GetAvailableDevices()
    {
        var devices = new List<AudioDevice>();

        using var enumerator = new MMDeviceEnumerator();

        // キャプチャボード / マイク (入力デバイス)
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            // キャプチャボードは通常「USB」「HDMI」「Capture」などの名前を含む
            var type = IsLikelyCaptureBoard(device.FriendlyName)
                ? AudioDeviceType.CaptureBoard
                : AudioDeviceType.Microphone;

            devices.Add(new AudioDevice(device.ID, device.FriendlyName, type));
        }

        // システム音声 (WASAPI Loopback)
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            devices.Add(new AudioDevice(
                $"loopback:{device.ID}",
                $"[システム音声] {device.FriendlyName}",
                AudioDeviceType.SystemAudio));
        }

        // 優先度順にソート: キャプチャボード > システム音声 > マイク
        return devices
            .OrderBy(d => d.Type switch
            {
                AudioDeviceType.CaptureBoard => 0,
                AudioDeviceType.SystemAudio => 1,
                AudioDeviceType.Microphone => 2,
                _ => 3
            })
            .ToList()
            .AsReadOnly();
    }

    /// <inheritdoc/>
    public Task StartCaptureAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        if (_waveIn is not null)
        {
            throw new InvalidOperationException("既にキャプチャ中");
        }

        // ターゲットフォーマット: 48kHz, 16bit, Stereo
        _targetFormat = new WaveFormat(
            StreamingConstants.AudioSampleRate,
            StreamingConstants.AudioBitsPerSample,
            StreamingConstants.AudioChannels);

        using var enumerator = new MMDeviceEnumerator();

        if (deviceId.StartsWith("loopback:"))
        {
            // WASAPI Loopback (システム音声)
            var actualId = deviceId["loopback:".Length..];
            var device = enumerator.GetDevice(actualId);

            var capture = new WasapiLoopbackCapture(device)
            {
                ShareMode = AudioClientShareMode.Shared,
            };

            capture.DataAvailable += OnDataAvailable;
            capture.RecordingStopped += OnRecordingStopped;

            _waveIn = capture;
            _logger.LogInformation("WASAPI Loopback キャプチャを開始: {DeviceName}", device.FriendlyName);
        }
        else
        {
            // WASAPI Capture (マイク / キャプチャボード)
            var device = enumerator.GetDevice(deviceId);

            var capture = new WasapiCapture(device)
            {
                ShareMode = AudioClientShareMode.Shared,
            };

            capture.DataAvailable += OnDataAvailable;
            capture.RecordingStopped += OnRecordingStopped;

            _waveIn = capture;
            _logger.LogInformation("WASAPI キャプチャを開始: {DeviceName} (入力フォーマット: {InputFormat})", 
                device.FriendlyName, _waveIn.WaveFormat);
        }

        // 変換用プロバイダーの設定
        _captureProvider = new BufferedWaveProvider(_waveIn.WaveFormat)
        {
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromSeconds(1)
        };

        // managedな変換パイプラインの構築
        ISampleProvider sampleProvider = _captureProvider.ToSampleProvider();

        // リサンプリングが必要な場合 (例: 44.1kHz -> 48kHz)
        if (sampleProvider.WaveFormat.SampleRate != _targetFormat.SampleRate)
        {
            _logger.LogInformation("リサンプリングを適用: {SourceRate}Hz -> {TargetRate}Hz", 
                sampleProvider.WaveFormat.SampleRate, _targetFormat.SampleRate);
            sampleProvider = new WdlResamplingSampleProvider(sampleProvider, _targetFormat.SampleRate);
        }

        // 16-bit PCMへの変換
        _conversionStream = new SampleToWaveProvider16(sampleProvider);

        _waveIn.StartRecording();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopCaptureAsync(CancellationToken cancellationToken = default)
    {
        if (_waveIn is not null)
        {
            _waveIn.StopRecording();

            if (_waveIn is WasapiCapture wasapiCapture)
            {
                wasapiCapture.DataAvailable -= OnDataAvailable;
                wasapiCapture.RecordingStopped -= OnRecordingStopped;
            }
            else if (_waveIn is WasapiLoopbackCapture loopbackCapture)
            {
                loopbackCapture.DataAvailable -= OnDataAvailable;
                loopbackCapture.RecordingStopped -= OnRecordingStopped;
            }

            _waveIn.Dispose();
            _waveIn = null;
        }

        (_conversionStream as IDisposable)?.Dispose();
        _conversionStream = null;
        _captureProvider = null;

        _logger.LogInformation("音声キャプチャを停止");
        return Task.CompletedTask;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0 || _captureProvider == null || _conversionStream == null) return;

        try
        {
            // キャプチャデータをバッファに追加
            _captureProvider.AddSamples(e.Buffer, 0, e.BytesRecorded);

            // ターゲットフォーマットへ変換して読み取り (可能な限り多くのデータを取得)
            int totalRead = 0;
            int read;
            // 少ないデータ量でも確実に読み取るため、残響などを考慮しループを継続
            while ((read = _conversionStream.Read(_conversionBuffer, 0, _conversionBuffer.Length)) > 0)
            {
                totalRead += read;
                var pcmData = new byte[read];
                Array.Copy(_conversionBuffer, 0, pcmData, 0, read);

                var audioData = new AudioData(
                    pcmData,
                    StreamingConstants.AudioSampleRate,
                    StreamingConstants.AudioChannels,
                    DateTime.UtcNow.Ticks);

                AudioAvailable?.Invoke(this, new AudioEventArgs(audioData));
            }

            if (totalRead == 0 && e.BytesRecorded > 0)
            {
                // リサンプラーがデータを溜めている可能性があるため警告は最小限に
                _logger.LogTrace("音声変換出力なし: 入力 {InputBytes} バイト, バッファ残量 {BufferedBytes} バイト", 
                    e.BytesRecorded, _captureProvider.BufferedBytes);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "音声フォーマット変換中にエラーが発生");
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            _logger.LogError(e.Exception, "録音中にエラーが発生");
        }
    }

    private static bool IsLikelyCaptureBoard(string deviceName)
    {
        var lowerName = deviceName.ToLowerInvariant();
        return lowerName.Contains("capture") ||
               lowerName.Contains("hdmi") ||
               lowerName.Contains("usb") ||
               lowerName.Contains("game") ||
               lowerName.Contains("elgato") ||
               lowerName.Contains("avermedia");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;

        _waveIn?.StopRecording();
        _waveIn?.Dispose();
        _waveIn = null;

        (_conversionStream as IDisposable)?.Dispose();
        _conversionStream = null;

        _disposed = true;
    }
}

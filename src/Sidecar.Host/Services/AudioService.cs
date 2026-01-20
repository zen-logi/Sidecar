// <copyright file="AudioService.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
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
            throw new InvalidOperationException("既にキャプチャ中")
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
            _logger.LogInformation("WASAPI キャプチャを開始: {DeviceName}", device.FriendlyName);
        }

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

        _logger.LogInformation("音声キャプチャを停止");
        return Task.CompletedTask;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0) return;

        try
        {
            // フォーマット変換が必要な場合はここで行う
            // 現状は生データをそのまま送信
            var pcmData = new byte[e.BytesRecorded];
            Array.Copy(e.Buffer, pcmData, e.BytesRecorded);

            var waveIn = sender as IWaveIn;
            var audioData = new AudioData(
                pcmData,
                waveIn?.WaveFormat.SampleRate ?? StreamingConstants.AudioSampleRate,
                waveIn?.WaveFormat.Channels ?? StreamingConstants.AudioChannels,
                DateTime.UtcNow.Ticks);

            AudioAvailable?.Invoke(this, new AudioEventArgs(audioData));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "音声データ処理中にエラーが発生");
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

        _disposed = true;
    }
}

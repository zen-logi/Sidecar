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
/// <remarks>
/// <see cref="AudioService"/> クラスの新しいインスタンスを初期化
/// </remarks>
/// <param name="logger">ロガー</param>
public sealed class AudioService(ILogger<AudioService> logger) : IAudioService {
    private IWaveIn? _waveIn;

    private WaveFormat? _targetFormat;
    private BufferedWaveProvider? _captureProvider;
    private IWaveProvider? _conversionStream;
    private readonly byte[] _conversionBuffer = new byte[4096]; // 低遅延化のためバッファサイズを縮小
    private long _totalBytesCaptured;
    private long _totalBytesConverted;
    private bool _disposed;
#if DEBUG
    private Task? _statsTask;
    private CancellationTokenSource? _statsCts;
#endif

    /// <inheritdoc/>
    public event EventHandler<AudioEventArgs>? AudioAvailable;

    /// <inheritdoc/>
    public bool IsCapturing => _waveIn is not null;

    /// <inheritdoc/>
    public IReadOnlyList<AudioDevice> GetAvailableDevices() {
        var devices = new List<AudioDevice>();

        using var enumerator = new MMDeviceEnumerator();

        // キャプチャボード / マイク (入力デバイス)
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)) {
            // キャプチャボードは通常「USB」「HDMI」「Capture」などの名前を含む
            var type = IsLikelyCaptureBoard(device.FriendlyName)
                ? AudioDeviceType.CaptureBoard
                : AudioDeviceType.Microphone;

            devices.Add(new AudioDevice(device.ID, device.FriendlyName, type));
        }

        // システム音声 (WASAPI Loopback)
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)) {
            devices.Add(new AudioDevice(
                $"loopback:{device.ID}",
                $"[システム音声] {device.FriendlyName}",
                AudioDeviceType.SystemAudio));
        }

        // 優先度順にソート: キャプチャボード > システム音声 > マイク
        return devices
            .OrderBy(d => d.Type switch {
                AudioDeviceType.CaptureBoard => 0,
                AudioDeviceType.SystemAudio => 1,
                AudioDeviceType.Microphone => 2,
                AudioDeviceType.Mixed => throw new NotImplementedException(),
                _ => 3
            })
            .ToList()
            .AsReadOnly();
    }

    /// <inheritdoc/>
    public Task StartCaptureAsync(string deviceId, CancellationToken cancellationToken = default) {
        if (_waveIn is not null) {
            throw new InvalidOperationException("既にキャプチャ中");
        }

        // ターゲットフォーマット: 48kHz, 16bit, Stereo
        _targetFormat = new WaveFormat(
            StreamingConstants.AudioSampleRate,
            StreamingConstants.AudioBitsPerSample,
            StreamingConstants.AudioChannels);

        using var enumerator = new MMDeviceEnumerator();

        if (deviceId.StartsWith("loopback:")) {
            // WASAPI Loopback (システム音声)
            var actualId = deviceId["loopback:".Length..];
            var device = enumerator.GetDevice(actualId);

            var capture = new WasapiLoopbackCapture(device) {
                ShareMode = AudioClientShareMode.Shared,
            };

            capture.DataAvailable += OnDataAvailable;
            capture.RecordingStopped += OnRecordingStopped;

            _waveIn = capture;
            logger.LogInformation("WASAPI Loopback キャプチャを開始: {DeviceName}", device.FriendlyName);
        } else {
            // WASAPI Capture (マイク / キャプチャボード)
            var device = enumerator.GetDevice(deviceId);

            var capture = new WasapiCapture(device) {
                ShareMode = AudioClientShareMode.Shared,
            };

            capture.DataAvailable += OnDataAvailable;
            capture.RecordingStopped += OnRecordingStopped;

            _waveIn = capture;
            logger.LogInformation("WASAPI キャプチャを開始: {DeviceName} (入力フォーマット: {InputFormat})",
                device.FriendlyName, _waveIn.WaveFormat);
        }

        // 変換用プロバイダーの設定
        _captureProvider = new BufferedWaveProvider(_waveIn.WaveFormat) {
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromSeconds(1)
        };

        // managedな変換パイプラインの構築
        ISampleProvider sampleProvider = _captureProvider.ToSampleProvider();

        // リサンプリングが必要な場合 (例: 44.1kHz -> 48kHz)
        if (sampleProvider.WaveFormat.SampleRate != _targetFormat.SampleRate) {
            logger.LogInformation("リサンプリングを適用: {SourceRate}Hz -> {TargetRate}Hz",
                sampleProvider.WaveFormat.SampleRate, _targetFormat.SampleRate);
            sampleProvider = new WdlResamplingSampleProvider(sampleProvider, _targetFormat.SampleRate);
        }

        // 16-bit PCMへの変換
        _conversionStream = new SampleToWaveProvider16(sampleProvider);

#if DEBUG
        _statsCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _statsTask = LogStatsAsync(_statsCts.Token);
#endif

        _waveIn.StartRecording();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task StopCaptureAsync(CancellationToken cancellationToken = default) {
        if (_waveIn is not null) {
            await Task.Run(() => {
                try {
                    _waveIn.StopRecording();

                    if (_waveIn is WasapiCapture wasapiCapture) {
                        wasapiCapture.DataAvailable -= OnDataAvailable;
                        wasapiCapture.RecordingStopped -= OnRecordingStopped;
                    } else if (_waveIn is WasapiLoopbackCapture loopbackCapture) {
                        loopbackCapture.DataAvailable -= OnDataAvailable;
                        loopbackCapture.RecordingStopped -= OnRecordingStopped;
                    }

                    _waveIn.Dispose();
                } catch (Exception ex) {
                    logger.LogDebug(ex, "NAudio停止中の例外");
                }
            }, cancellationToken);

            _waveIn = null;
        }

#if DEBUG
        if (_statsCts != null) {
            await _statsCts.CancelAsync();
            if (_statsTask != null) {
                try {
                    await _statsTask.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);
                } catch (OperationCanceledException) { }
            }
            _statsCts.Dispose();
            _statsCts = null;
            _statsTask = null;
        }
#endif

        await Task.Run(() => {
            (_conversionStream as IDisposable)?.Dispose();
            _conversionStream = null;
            _captureProvider = null;
        }, cancellationToken);

        logger.LogInformation("音声キャプチャを停止");
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e) {
        if (e.BytesRecorded == 0)
            return;

        try {
            _ = Interlocked.Add(ref _totalBytesCaptured, e.BytesRecorded);

            // 高速パス: 48kHz Float 入力の場合は直接変換 (処理負荷軽減と無限ループ回避)
            if (_waveIn?.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat &&
                _waveIn.WaveFormat.SampleRate == StreamingConstants.AudioSampleRate &&
                _waveIn.WaveFormat.Channels == StreamingConstants.AudioChannels) {
                var pcmData = new byte[e.BytesRecorded / 2];
                for (var i = 0; i < e.BytesRecorded / 4; i++) {
                    var sample = BitConverter.ToSingle(e.Buffer, i * 4);
                    // 範囲制限
                    if (sample > 1.0f)
                        sample = 1.0f;
                    else if (sample < -1.0f)
                        sample = -1.0f;

                    var s = (short)(sample * 32767);
                    pcmData[i * 2] = (byte)(s & 0xff);
                    pcmData[(i * 2) + 1] = (byte)((s >> 8) & 0xff);
                }

                var audioData = new AudioData(
                    pcmData,
                    StreamingConstants.AudioSampleRate,
                    StreamingConstants.AudioChannels,
                    DateTime.UtcNow.Ticks);

                AudioAvailable?.Invoke(this, new AudioEventArgs(audioData));
                _ = Interlocked.Add(ref _totalBytesConverted, pcmData.Length);
                return;
            }

            if (_captureProvider == null || _conversionStream == null)
                return;

            // キャプチャデータをバッファに追加
            _captureProvider.AddSamples(e.Buffer, 0, e.BytesRecorded);

            // ターゲットフォーマットへ変換して読み取り (安全のため読み取りループに上限を設定)
            var totalRead = 0;
            var iterations = 0;
            while (iterations < 50) {
                var read = _conversionStream.Read(_conversionBuffer, 0, _conversionBuffer.Length);
                if (read <= 0)
                    break;

                iterations++;
                totalRead += read;

                var pcmData = new byte[read];
                Array.Copy(_conversionBuffer, 0, pcmData, 0, read);

                var audioData = new AudioData(
                    pcmData,
                    StreamingConstants.AudioSampleRate,
                    StreamingConstants.AudioChannels,
                    DateTime.UtcNow.Ticks);

                AudioAvailable?.Invoke(this, new AudioEventArgs(audioData));

                // 1回のコールバックで1MBを超えるデータは異常として打ち切り
                if (totalRead > 1024 * 1024) {
                    logger.LogWarning("音声変換データが上限を超えたため読み取りを中断");
                    break;
                }
            }
            _ = Interlocked.Add(ref _totalBytesConverted, totalRead);
        } catch (Exception ex) {
            logger.LogError(ex, "音声フォーマット変換中にエラーが発生");
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e) {
        if (e.Exception is not null) {
            logger.LogError(e.Exception, "録音中にエラーが発生");
        }
    }

    private static bool IsLikelyCaptureBoard(string deviceName) {
        var lowerName = deviceName.ToLowerInvariant();
        return lowerName.Contains("capture") ||
               lowerName.Contains("hdmi") ||
               lowerName.Contains("usb") ||
               lowerName.Contains("game") ||
               lowerName.Contains("elgato") ||
               lowerName.Contains("avermedia");
    }

    /// <inheritdoc/>
    public void Dispose() {
        if (_disposed)
            return;

        _waveIn?.StopRecording();
        _waveIn?.Dispose();
        _waveIn = null;

        (_conversionStream as IDisposable)?.Dispose();
        _conversionStream = null;

        _disposed = true;
    }

    private async Task LogStatsAsync(CancellationToken cancellationToken) {
        while (!cancellationToken.IsCancellationRequested) {
            try {
                await Task.Delay(1000, cancellationToken);
                var captured = Interlocked.Exchange(ref _totalBytesCaptured, 0);
                var converted = Interlocked.Exchange(ref _totalBytesConverted, 0);
                if (captured > 0 || converted > 0) {
                    logger.LogDebug("音声キャプチャ統計: キャプチャ {Captured} バイト, 変換後 {Converted} バイト",
                        captured, converted);
                }
            } catch (OperationCanceledException) { break; } catch (Exception ex) { logger.LogError(ex, "統計ログ出力エラー"); }
        }
    }
}

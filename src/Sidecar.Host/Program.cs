// <copyright file="Program.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sidecar.Host.Extensions;
using Sidecar.Host.Interfaces;
using Sidecar.Shared;

Console.WriteLine("=================================");
Console.WriteLine(" Sidecar Host - Video & Audio Streamer");
Console.WriteLine("=================================");
Console.WriteLine();

// DIコンテナのセットアップ
var services = new ServiceCollection();

// ロギング設定
services.AddLogging(builder => {
    _ = builder.SetMinimumLevel(LogLevel.Information);
    _ = builder.AddConsole(options => options.FormatterName = "simple");
});

services.AddSidecarHostServices();
await using var serviceProvider = services.BuildServiceProvider();

var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
var cameraService = serviceProvider.GetRequiredService<ICameraService>();
var streamServer = serviceProvider.GetRequiredService<IStreamServer>();
var audioService = serviceProvider.GetRequiredService<IAudioService>();
var audioStreamServer = serviceProvider.GetRequiredService<IAudioStreamServer>();
var formatInterceptor = serviceProvider.GetRequiredService<IFormatInterceptor>();

using var cts = new CancellationTokenSource();

// Ctrl+C でキャンセル
Console.CancelKeyPress += (_, e) => {
    e.Cancel = true;
    logger.LogInformation("終了リクエストを受信しました...");
    cts.Cancel();
};

try {
    // ==================== カメラ選択 ====================
    logger.LogInformation("利用可能なカメラデバイスを検索中");
    var cameras = cameraService.GetAvailableDevices();

    if (cameras.Count == 0) {
        logger.LogError("利用可能なカメラデバイスが見つからない");
        return 1;
    }

    foreach (var camera in cameras) {
        Console.WriteLine($"  [{camera.Index}] {camera.Name}");
    }

    int selectedCameraIndex;
    if (args.Length > 0 && int.TryParse(args[0], out var argIndex)) {
        selectedCameraIndex = argIndex;
        logger.LogInformation("コマンドライン引数からカメラ {Index} を選択", selectedCameraIndex);
    } else {
        Console.Write("\n使用するカメラのインデックスを入力: ");
        var input = Console.ReadLine();

        if (!int.TryParse(input, out selectedCameraIndex)) {
            logger.LogError("無効な入力");
            return 1;
        }
    }

    if (selectedCameraIndex < 0 || selectedCameraIndex >= cameras.Count) {
        logger.LogError("カメラインデックス {Index} は存在しない", selectedCameraIndex);
        return 1;
    }

    // ==================== 音声デバイス選択 ====================
    logger.LogInformation("利用可能な音声デバイスを検索中");
    var audioDevices = audioService.GetAvailableDevices();

    string? selectedAudioDeviceId = null;
    if (audioDevices.Count > 0) {
        for (var i = 0; i < audioDevices.Count; i++) {
            Console.WriteLine($"  [{i}] {audioDevices[i].Name}");
        }

        Console.Write("\n使用する音声デバイスのインデックスを入力 (スキップ: Enter): ");
        var audioInput = Console.ReadLine();

        if (!string.IsNullOrWhiteSpace(audioInput) && int.TryParse(audioInput, out var audioIndex)) {
            if (audioIndex >= 0 && audioIndex < audioDevices.Count) {
                selectedAudioDeviceId = audioDevices[audioIndex].Id;
            }
        }
    } else {
        logger.LogWarning("利用可能な音声デバイスが見つからない 音声ストリーミングは無効");
    }

    // ==================== ポート番号の取得 ====================
    var videoPort = StreamingConstants.DefaultPort;
    var audioPort = StreamingConstants.DefaultAudioPort;

    if (args.Length > 1 && int.TryParse(args[1], out var argPort)) {
        videoPort = argPort;
        audioPort = argPort + 1;
    }

    // ==================== キャプチャ開始 ====================
    await cameraService.StartCaptureAsync(selectedCameraIndex, cts.Token);
    await streamServer.StartAsync(videoPort, cts.Token);

    if (selectedAudioDeviceId is not null) {
        await audioService.StartCaptureAsync(selectedAudioDeviceId, cts.Token);
        await audioStreamServer.StartAsync(audioPort, cts.Token);
    }

    Console.WriteLine($"\n===== ストリーミング開始 =====");
    Console.WriteLine($"ビデオ: http://<このPCのIPアドレス>:{videoPort}");
    if (selectedAudioDeviceId is not null) {
        Console.WriteLine($"オーディオ: tcp://<このPCのIPアドレス>:{audioPort}");
    }
    Console.WriteLine("Ctrl+C で終了");
    Console.WriteLine("\nCLIコマンド:");
    Console.WriteLine("  mode auto|yuy2|uyvy|nv12|rgb  - 入力フォーマットを切替 (紫/緑画面対策)");
    Console.WriteLine("  hdr on|off                    - HDRトーンマッピングを切替 (白飛び対策)");
    Console.WriteLine("  status                   - 現在のパイプライン状態を表示");
    Console.WriteLine();

    // メインループ: ステータス表示 + CLI入力の統合
    var lastStatusTime = DateTime.MinValue;
    while (!cts.Token.IsCancellationRequested) {
        // キー入力があればコマンド処理モードに入る
        if (Console.KeyAvailable) {
            // ステータス行をクリアしてプロンプト表示
            Console.Write("\r" + new string(' ', 80) + "\r");
            Console.Write("> ");
            var input = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(input)) {
                formatInterceptor.ProcessCommand(input);
            }
        }

        // 1秒ごとにステータス表示を更新
        if ((DateTime.UtcNow - lastStatusTime).TotalSeconds >= 1.0) {
            var audioClientCount = selectedAudioDeviceId is not null ? audioStreamServer.ConnectedClientCount : 0;
            Console.Write($"\r接続数 - ビデオ: {streamServer.ConnectedClientCount}, オーディオ: {audioClientCount}   ");
            lastStatusTime = DateTime.UtcNow;
        }

        try {
            await Task.Delay(100, cts.Token);
        } catch (OperationCanceledException) {
            break;
        }
    }
} catch (OperationCanceledException) {
    // 正常終了
} catch (Exception ex) {
    logger.LogError(ex, "予期しないエラーが発生");
    return 1;
} finally {
    Console.WriteLine("\nサーバーを停止中...");

    // タイムアウト付きの停止処理
    using var shutdownCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var shutdownToken = shutdownCts.Token;

    async Task StopService(string name, Func<CancellationToken, Task> stopAction) {
        try {
            await stopAction(shutdownToken);
        } catch (Exception ex) {
            logger.LogDebug(ex, "{Name} 停止中のエラー", name);
        }
    }

    await StopService("音声ストリーミング", audioStreamServer.StopAsync);
    await StopService("音声キャプチャ", audioService.StopCaptureAsync);
    await StopService("ビデオストリーミング", streamServer.StopAsync);
    await StopService("カメラキャプチャ", cameraService.StopCaptureAsync);

    Console.WriteLine("終了");
}

return 0;

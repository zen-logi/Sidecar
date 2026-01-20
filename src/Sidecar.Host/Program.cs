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
services.AddLogging(builder =>
{
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

using var cts = new CancellationTokenSource();

// Ctrl+C でキャンセル
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    logger.LogInformation("終了リクエストを受信しました...");
    cts.Cancel();
};

try
{
    // ==================== カメラ選択 ====================
    logger.LogInformation("利用可能なカメラデバイスを検索中...");
    var cameras = cameraService.GetAvailableDevices();

    if (cameras.Count == 0)
    {
        logger.LogError("利用可能なカメラデバイスが見つかりません");
        return 1;
    }

    Console.WriteLine($"\n{cameras.Count} 個のカメラデバイスが見つかりました:");
    for (int i = 0; i < cameras.Count; i++)
    {
        Console.WriteLine($"  [{i}] {cameras[i]}");
    }

    int selectedCameraIndex;
    if (args.Length > 0 && int.TryParse(args[0], out var argIndex))
    {
        selectedCameraIndex = argIndex;
        logger.LogInformation("コマンドライン引数からカメラ {Index} を選択", selectedCameraIndex);
    }
    else
    {
        Console.Write("\n使用するカメラのインデックスを入力してください: ");
        var input = Console.ReadLine();

        if (!int.TryParse(input, out selectedCameraIndex))
        {
            logger.LogError("無効な入力です");
            return 1;
        }
    }

    if (selectedCameraIndex < 0 || selectedCameraIndex >= cameras.Count)
    {
        logger.LogError("カメラインデックス {Index} は存在しません", selectedCameraIndex);
        return 1;
    }

    // ==================== 音声デバイス選択 ====================
    logger.LogInformation("利用可能な音声デバイスを検索中...");
    var audioDevices = audioService.GetAvailableDevices();

    string? selectedAudioDeviceId = null;
    if (audioDevices.Count > 0)
    {
        Console.WriteLine($"\n{audioDevices.Count} 個の音声デバイスが見つかりました:");
        for (int i = 0; i < audioDevices.Count; i++)
        {
            Console.WriteLine($"  [{i}] {audioDevices[i]}");
        }

        Console.Write("\n使用する音声デバイスのインデックスを入力してください (スキップ: Enter): ");
        var audioInput = Console.ReadLine();

        if (!string.IsNullOrWhiteSpace(audioInput) && int.TryParse(audioInput, out var audioIndex))
        {
            if (audioIndex >= 0 && audioIndex < audioDevices.Count)
            {
                selectedAudioDeviceId = audioDevices[audioIndex].Id;
            }
        }
    }
    else
    {
        logger.LogWarning("利用可能な音声デバイスが見つかりません。音声ストリーミングは無効です。");
    }

    // ==================== ポート番号の取得 ====================
    var videoPort = StreamingConstants.DefaultPort;
    var audioPort = StreamingConstants.DefaultAudioPort;

    if (args.Length > 1 && int.TryParse(args[1], out var argPort))
    {
        videoPort = argPort;
        audioPort = argPort + 1;
    }

    // ==================== キャプチャ開始 ====================
    await cameraService.StartCaptureAsync(selectedCameraIndex, cts.Token);
    await streamServer.StartAsync(videoPort, cts.Token);

    if (selectedAudioDeviceId is not null)
    {
        await audioService.StartCaptureAsync(selectedAudioDeviceId, cts.Token);
        await audioStreamServer.StartAsync(audioPort, cts.Token);
    }

    Console.WriteLine($"\n===== ストリーミング開始 =====");
    Console.WriteLine($"ビデオ: http://<このPCのIPアドレス>:{videoPort}");
    if (selectedAudioDeviceId is not null)
    {
        Console.WriteLine($"オーディオ: tcp://<このPCのIPアドレス>:{audioPort}");
    }
    Console.WriteLine("Ctrl+C で終了します。\n");

    // メインループ
    while (!cts.Token.IsCancellationRequested)
    {
        var audioClientCount = selectedAudioDeviceId is not null ? audioStreamServer.ConnectedClientCount : 0;
        Console.Write($"\r接続クライアント数 - ビデオ: {streamServer.ConnectedClientCount}, オーディオ: {audioClientCount}   ");
        try
        {
            await Task.Delay(1000, cts.Token);
        }
        catch (OperationCanceledException)
        {
            break;
        }
    }
}
catch (OperationCanceledException)
{
    // 正常終了
}
catch (Exception ex)
{
    logger.LogError(ex, "予期しないエラーが発生しました");
    return 1;
}
finally
{
    Console.WriteLine("\nサーバーを停止しています...");
    await audioStreamServer.StopAsync(CancellationToken.None);
    await audioService.StopCaptureAsync(CancellationToken.None);
    await streamServer.StopAsync(CancellationToken.None);
    await cameraService.StopCaptureAsync(CancellationToken.None);
    Console.WriteLine("終了しました。");
}

return 0;

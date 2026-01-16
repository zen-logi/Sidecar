// <copyright file="Program.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sidecar.Host.Extensions;
using Sidecar.Host.Interfaces;
using Sidecar.Shared;

Console.WriteLine("=================================");
Console.WriteLine(" Sidecar Host - MJPEG Streamer");
Console.WriteLine("=================================");
Console.WriteLine();

// DIコンテナのセットアップ
var services = new ServiceCollection();

// ロギング設定
services.AddLogging(builder =>
{
    builder.SetMinimumLevel(LogLevel.Information);
    builder.AddConsole(options =>
    {
        options.FormatterName = "simple";
    });
});

services.AddSidecarHostServices();
await using var serviceProvider = services.BuildServiceProvider();

var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
var cameraService = serviceProvider.GetRequiredService<ICameraService>();
var streamServer = serviceProvider.GetRequiredService<IStreamServer>();

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
    // カメラデバイスの列挙
    logger.LogInformation("利用可能なカメラデバイスを検索中...");
    var devices = cameraService.GetAvailableDevices();

    if (devices.Count == 0)
    {
        logger.LogError("利用可能なカメラデバイスが見つかりません");
        return 1;
    }

    Console.WriteLine($"\n{devices.Count} 個のカメラデバイスが見つかりました:");
    foreach (var device in devices)
    {
        Console.WriteLine($"  {device}");
    }

    // カメラ選択
    int selectedIndex;
    if (args.Length > 0 && int.TryParse(args[0], out var argIndex))
    {
        selectedIndex = argIndex;
        logger.LogInformation("コマンドライン引数からカメラ {Index} を選択", selectedIndex);
    }
    else
    {
        Console.Write("\n使用するカメラのインデックスを入力してください: ");
        var input = Console.ReadLine();

        if (!int.TryParse(input, out selectedIndex))
        {
            logger.LogError("無効な入力です");
            return 1;
        }
    }

    // 選択されたデバイスが有効か確認
    if (!devices.Any(d => d.Index == selectedIndex))
    {
        logger.LogError("カメラインデックス {Index} は存在しません", selectedIndex);
        return 1;
    }

    // ポート番号の取得
    var port = StreamingConstants.DefaultPort;
    if (args.Length > 1 && int.TryParse(args[1], out var argPort))
    {
        port = argPort;
    }

    // カメラキャプチャ開始
    await cameraService.StartCaptureAsync(selectedIndex, cts.Token);

    // ストリーミングサーバー開始
    await streamServer.StartAsync(port, cts.Token);

    Console.WriteLine($"\n接続先: http://<このPCのIPアドレス>:{port}");
    Console.WriteLine("Ctrl+C で終了します。\n");

    // メインループ
    while (!cts.Token.IsCancellationRequested)
    {
        Console.Write($"\r接続クライアント数: {streamServer.ConnectedClientCount}  ");
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
    await streamServer.StopAsync(CancellationToken.None);
    await cameraService.StopCaptureAsync(CancellationToken.None);
    Console.WriteLine("終了しました。");
}

return 0;

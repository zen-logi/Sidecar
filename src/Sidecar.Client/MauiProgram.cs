// <copyright file="MauiProgram.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using Sidecar.Client.Extensions;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace Sidecar.Client;

/// <summary>
/// MAUIアプリケーションのエントリーポイント
/// </summary>
public static class MauiProgram
{
    /// <summary>
    /// MAUIアプリケーションを作成
    /// </summary>
    /// <returns>構成されたMauiApp</returns>
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseSkiaSharp()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // サービス登録
        builder.Services.AddSidecarClientServices();

        // App と ページ登録
        builder.Services.AddSingleton<App>();
        builder.Services.AddTransient<MainPage>();

#if DEBUG
        builder.Logging.AddDebug();
        builder.Logging.SetMinimumLevel(LogLevel.Debug);
#endif

        return builder.Build();
    }
}

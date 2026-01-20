// <copyright file="ClientServiceExtensions.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

using Microsoft.Extensions.DependencyInjection;
using Sidecar.Client.Interfaces;
using Sidecar.Client.Services;
using Sidecar.Client.ViewModels;

namespace Sidecar.Client.Extensions;

/// <summary>
/// クライアントサービスのDI登録用拡張メソッド
/// </summary>
public static class ClientServiceExtensions
{
    /// <summary>
    /// Sidecar.Clientのサービスをサービスコレクションに追加
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <returns>サービスコレクション</returns>
    public static IServiceCollection AddSidecarClientServices(this IServiceCollection services)
    {
        services.AddSingleton<IStreamClient, StreamClient>();
        services.AddSingleton<IAudioClient, AudioClient>();
        services.AddSingleton<IAudioPlayerService, AudioPlayerService>();
        services.AddTransient<MainPageViewModel>();

        return services;
    }
}

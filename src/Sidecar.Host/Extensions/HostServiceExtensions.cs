// <copyright file="HostServiceExtensions.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

using Microsoft.Extensions.DependencyInjection;
using Sidecar.Host.Interfaces;
using Sidecar.Host.Services;

namespace Sidecar.Host.Extensions;

/// <summary>
/// ホストサービスのDI登録用拡張メソッド
/// </summary>
public static class HostServiceExtensions {
    /// <summary>
    /// Sidecar.Hostのサービスをサービスコレクションに追加
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <returns>サービスコレクション</returns>
    public static IServiceCollection AddSidecarHostServices(this IServiceCollection services) {
        // GPU Pipeline services
        _ = services.AddSingleton<IFormatInterceptor, FormatInterceptor>();
        _ = services.AddSingleton<IGpuPipelineService, GpuPipelineService>();

        // Video services
        _ = services.AddSingleton<ICameraService, CameraService>();
        _ = services.AddSingleton<IStreamServer, StreamServer>();

        // Audio services
        _ = services.AddSingleton<IAudioService, AudioService>();
        _ = services.AddSingleton<IAudioStreamServer, AudioStreamServer>();

        return services;
    }
}

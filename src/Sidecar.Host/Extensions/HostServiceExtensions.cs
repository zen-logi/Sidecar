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
    /// カメラモード用のSidecar.Hostサービスをサービスコレクションに追加
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <returns>サービスコレクション</returns>
    public static IServiceCollection AddSidecarCameraMode(this IServiceCollection services) {
        // Video services (カメラモード)
        _ = services.AddSingleton<ICameraService, CameraService>();
        _ = services.AddSingleton<IFrameSource, CameraFrameSource>();
        _ = services.AddSingleton<IStreamServer, StreamServer>();

        // Audio services
        _ = services.AddSingleton<IAudioService, AudioService>();
        _ = services.AddSingleton<IAudioStreamServer, AudioStreamServer>();

        return services;
    }

    /// <summary>
    /// リレーモード用のSidecar.Hostサービスをサービスコレクションに追加
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <returns>サービスコレクション</returns>
    public static IServiceCollection AddSidecarRelayMode(this IServiceCollection services) {
        // Video services (リレーモード)
        _ = services.AddSingleton<RelayReceiverService>();
        _ = services.AddSingleton<IRelayReceiverService>(sp => sp.GetRequiredService<RelayReceiverService>());
        _ = services.AddSingleton<IFrameSource>(sp => sp.GetRequiredService<RelayReceiverService>());
        _ = services.AddSingleton<IStreamServer, StreamServer>();

        // Audio services (リレーモードでもローカル音声は利用可能)
        _ = services.AddSingleton<IAudioService, AudioService>();
        _ = services.AddSingleton<IAudioStreamServer, AudioStreamServer>();

        return services;
    }
}

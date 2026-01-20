// <copyright file="IAudioStreamServer.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

namespace Sidecar.Host.Interfaces;

/// <summary>
/// 音声ストリームを配信するサーバーのインターフェース
/// </summary>
public interface IAudioStreamServer : IDisposable
{
    /// <summary>
    /// サーバーを開始
    /// </summary>
    /// <param name="port">待ち受けポート番号</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>非同期操作を表すタスク</returns>
    Task StartAsync(int port, CancellationToken cancellationToken = default);

    /// <summary>
    /// サーバーを停止
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>非同期操作を表すタスク</returns>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 接続中のクライアント数を取得
    /// </summary>
    int ConnectedClientCount { get; }

    /// <summary>
    /// サーバーが実行中かどうかを取得
    /// </summary>
    bool IsRunning { get; }
}

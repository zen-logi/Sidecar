// <copyright file="IRelayReceiverService.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

namespace Sidecar.Host.Interfaces;

/// <summary>
/// Macからのリレー映像を受信するサービスのインターフェース
/// TCP Listenerで接続を待ち受け、受信したフレームをIFrameSourceとして公開する
/// </summary>
public interface IRelayReceiverService : IFrameSource {
    /// <summary>
    /// TCP Listenerを開始してMac Senderからの接続を待ち受ける
    /// </summary>
    /// <param name="port">待ち受けポート番号</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>非同期操作を表すタスク</returns>
    Task StartAsync(int port, CancellationToken cancellationToken = default);

    /// <summary>
    /// リレー受信を停止
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>非同期操作を表すタスク</returns>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Senderが接続中かどうかを取得
    /// </summary>
    bool IsSenderConnected { get; }
}

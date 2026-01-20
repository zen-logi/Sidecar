// <copyright file="IStreamClient.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

using Sidecar.Shared.Models;

namespace Sidecar.Client.Interfaces;

/// <summary>
/// MJPEGストリームを受信するクライアントのインターフェース
/// </summary>
public interface IStreamClient : IDisposable
{
    /// <summary>
    /// ストリーミングサーバーに接続
    /// </summary>
    /// <param name="host">ホストアドレス</param>
    /// <param name="port">ポート番号</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>非同期操作を表すタスク</returns>
    Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default);

    /// <summary>
    /// 接続を切断
    /// </summary>
    void Disconnect();

    /// <summary>
    /// 最新のフレームを取得
    /// </summary>
    /// <returns>JPEG圧縮されたフレームデータ フレームがない場合はnull</returns>
    byte[]? GetLatestFrame();

    /// <summary>
    /// フレームを受信したときに発生するイベント
    /// </summary>
    event EventHandler<FrameEventArgs>? FrameReceived;

    /// <summary>
    /// 接続状態を取得
    /// </summary>
    ConnectionState State { get; }
}

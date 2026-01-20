// <copyright file="ConnectionState.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

namespace Sidecar.Shared.Models;

/// <summary>
/// 接続状態を表す列挙型
/// </summary>
public enum ConnectionState
{
    /// <summary>
    /// 切断済み
    /// </summary>
    Disconnected,

    /// <summary>
    /// 接続中
    /// </summary>
    Connecting,

    /// <summary>
    /// 接続済み
    /// </summary>
    Connected,

    /// <summary>
    /// エラー発生
    /// </summary>
    Error,
}

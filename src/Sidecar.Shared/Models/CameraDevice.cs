// <copyright file="CameraDevice.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

namespace Sidecar.Shared.Models;

/// <summary>
/// カメラデバイス情報を表すモデルクラス
/// </summary>
/// <param name="Index">デバイスのインデックス番号</param>
/// <param name="Name">デバイスの表示名</param>
public sealed record CameraDevice(int Index, string Name)
{
    /// <summary>
    /// デバイス情報を文字列として取得します。
    /// </summary>
    /// <returns>デバイス情報の文字列表現。</returns>
    public override string ToString() => Name;
}

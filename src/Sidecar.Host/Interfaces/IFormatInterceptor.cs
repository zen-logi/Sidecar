// <copyright file="IFormatInterceptor.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

namespace Sidecar.Host.Interfaces;

/// <summary>
/// キャプチャデバイスのフォーマットを決定・上書きするサービスのインターフェース
/// </summary>
public interface IFormatInterceptor {
    /// <summary>
    /// 現在の入力フォーマット種別を取得
    /// </summary>
    VideoInputFormat InputFormat { get; }

    /// <summary>
    /// HDRトーンマッピングの有効化状態を取得
    /// </summary>
    bool EnableToneMap { get; }

    /// <summary>
    /// デバイスから報告されたFourCCを基にフォーマットを決定
    /// </summary>
    /// <param name="deviceFourCC">デバイスが報告するFourCCコード</param>
    void DetermineFormat(string deviceFourCC);

    /// <summary>
    /// CLIコマンドによるフォーマット強制上書き
    /// </summary>
    /// <param name="command">コマンド文字列 (例: "mode yuy2", "hdr on")</param>
    /// <returns>コマンドが正常に処理された場合true</returns>
    bool ProcessCommand(string command);
}

/// <summary>
/// ビデオ入力フォーマットの種類
/// </summary>
public enum VideoInputFormat {
    /// <summary>
    /// RGB形式 (スルーパス)
    /// </summary>
    Rgb,

    /// <summary>
    /// YUY2形式 (4:2:2 YUV)
    /// </summary>
    Yuy2,

    /// <summary>
    /// NV12形式 (4:2:0 YUV)
    /// </summary>
    Nv12,

    /// <summary>
    /// UYVY形式 (4:2:2 YUV, U-Y-V-Y順)
    /// </summary>
    Uyvy,

    /// <summary>
    /// YVYU形式 (4:2:2 YUV, Y-V-Y-U順)
    /// </summary>
    Yvyu,

    /// <summary>
    /// VYUY形式 (4:2:2 YUV, V-Y-U-Y順)
    /// </summary>
    Vyuy,

    /// <summary>
    /// 不明なフォーマット
    /// </summary>
    Unknown
}

// <copyright file="StreamingConstants.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

namespace Sidecar.Shared;

/// <summary>
/// ストリーミングに関する設定値を管理する定数クラス
/// </summary>
public static class StreamingConstants
{
    /// <summary>
    /// デフォルトのストリーミングポート番号
    /// </summary>
    public const int DefaultPort = 8554;

    /// <summary>
    /// JPEG圧縮品質（0-100）
    /// 低遅延を優先するため75を推奨
    /// </summary>
    public const int JpegQuality = 75;

    /// <summary>
    /// MJPEGストリームの境界文字列
    /// </summary>
    public const string MjpegBoundary = "--sidecar-mjpeg-boundary";

    /// <summary>
    /// 受信バッファサイズ（バイト）
    /// </summary>
    public const int ReceiveBufferSize = 1024 * 1024; // 1MB

    /// <summary>
    /// フレーム受信タイムアウト（ミリ秒）
    /// </summary>
    public const int FrameTimeoutMs = 5000;

    /// <summary>
    /// HTTP風レスポンスヘッダーのContent-Type
    /// </summary>
    public const string MjpegContentType = "multipart/x-mixed-replace; boundary=" + MjpegBoundary;

    /// <summary>
    /// 接続確立時の最大待機時間（ミリ秒）
    /// </summary>
    public const int ConnectionTimeoutMs = 10000;

    // ==================== Audio Streaming ====================

    /// <summary>
    /// デフォルトの音声ストリーミングポート番号
    /// </summary>
    public const int DefaultAudioPort = 8555;

    /// <summary>ビデオ配信キューの最大数 (滞留時に古いフレームを破棄)</summary>
    public const int VideoQueueLimit = 15;

    /// <summary>音声配信キューの最大数 (滞留時に古いパケットを破棄)</summary>
    public const int AudioQueueLimit = 50;

    /// <summary>
    /// 音声サンプリングレート（Hz）
    /// </summary>
    public const int AudioSampleRate = 48000;

    /// <summary>
    /// 音声チャンネル数
    /// </summary>
    public const int AudioChannels = 2;

    /// <summary>
    /// 音声ビット深度（bits per sample）
    /// </summary>
    public const int AudioBitsPerSample = 16;

    /// <summary>
    /// 音声バッファサイズ（ミリ秒）低遅延のため20msを推奨
    /// </summary>
    public const int AudioBufferMs = 20;

    /// <summary>
    /// 音声受信バッファサイズ（バイト）
    /// </summary>
    public const int AudioReceiveBufferSize = 64 * 1024; // 64KB
}

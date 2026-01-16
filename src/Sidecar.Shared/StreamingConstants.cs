// <copyright file="StreamingConstants.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

namespace Sidecar.Shared;

/// <summary>
/// ストリーミングに関する設定値を管理する定数クラス。
/// </summary>
public static class StreamingConstants
{
    /// <summary>
    /// デフォルトのストリーミングポート番号。
    /// </summary>
    public const int DefaultPort = 8554;

    /// <summary>
    /// JPEG圧縮品質（0-100）。
    /// 低遅延を優先するため、75を推奨。
    /// </summary>
    public const int JpegQuality = 75;

    /// <summary>
    /// MJPEGストリームの境界文字列。
    /// </summary>
    public const string MjpegBoundary = "--sidecar-mjpeg-boundary";

    /// <summary>
    /// 受信バッファサイズ（バイト）。
    /// </summary>
    public const int ReceiveBufferSize = 1024 * 1024; // 1MB

    /// <summary>
    /// フレーム受信タイムアウト（ミリ秒）。
    /// </summary>
    public const int FrameTimeoutMs = 5000;

    /// <summary>
    /// HTTP風レスポンスヘッダーのContent-Type。
    /// </summary>
    public const string MjpegContentType = "multipart/x-mixed-replace; boundary=" + MjpegBoundary;

    /// <summary>
    /// 接続確立時の最大待機時間（ミリ秒）。
    /// </summary>
    public const int ConnectionTimeoutMs = 10000;
}

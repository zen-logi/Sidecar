// <copyright file="FormatInterceptor.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

using Microsoft.Extensions.Logging;
using Sidecar.Host.Interfaces;

namespace Sidecar.Host.Services;

/// <summary>
/// キャプチャデバイスのフォーマット決定およびCLI上書き処理を行うスレッドセーフなサービス
/// </summary>
/// <remarks>
/// <see cref="FormatInterceptor"/> クラスの新しいインスタンスを初期化
/// </remarks>
/// <param name="logger">ロガー</param>
public sealed class FormatInterceptor(ILogger<FormatInterceptor> logger) : IFormatInterceptor {
    private readonly object _lock = new();
    private VideoInputFormat _currentFormat = VideoInputFormat.Unknown;
    private bool _enableToneMap;
    private VideoInputFormat? _overrideFormat;

    /// <inheritdoc/>
    public bool DumpRequested { get; set; }

    /// <summary>
    /// 次のフレームをCPU変換してファイル保存するフラグ
    /// </summary>
    public bool VerifyRequested { get; set; }

    /// <inheritdoc/>
    public bool CalibrateRequested { get; set; }

    /// <inheritdoc/>
    public float ChromaOffsetU { get; set; }

    /// <inheritdoc/>
    public float ChromaOffsetV { get; set; }

    /// <inheritdoc/>
    public VideoInputFormat InputFormat {
        get {
            lock (_lock)
                return _overrideFormat ?? _currentFormat;
        }
    }

    /// <inheritdoc/>
    public bool EnableToneMap {
        get {
            lock (_lock)
                return _enableToneMap;
        }
    }

    /// <inheritdoc/>
    public void DetermineFormat(string deviceFourCC) {
        lock (_lock) {
            // CLI上書きがある場合はデバイスのFourCCを無視
            if (_overrideFormat.HasValue) {
                logger.LogInformation("フォーマット上書き有効: {Override} (デバイス報告: {Device})", _overrideFormat.Value, deviceFourCC);
                return;
            }

            _currentFormat = ParseFourCC(deviceFourCC);
            logger.LogInformation("デバイスフォーマット決定: {Format} (FourCC: {FourCC})", _currentFormat, deviceFourCC);
        }
    }

    /// <inheritdoc/>
    public bool ProcessCommand(string command) {
        var normalized = command.Trim().ToLowerInvariant();

        return normalized switch {
            // 新CLI構文 (設計書準拠: mode xxx)
            "mode auto" => ResetOverride(),
            "mode yuy2" or "mode yuyv" => SetOverride(VideoInputFormat.Yuy2, "YUY2"),
            "mode uyvy" => SetOverride(VideoInputFormat.Uyvy, "UYVY"),
            "mode yvyu" => SetOverride(VideoInputFormat.Yvyu, "YVYU"),
            "mode vyuy" => SetOverride(VideoInputFormat.Vyuy, "VYUY"),
            "mode nv12" => SetOverride(VideoInputFormat.Nv12, "NV12"),
            "mode rgb" => SetOverride(VideoInputFormat.Rgb, "RGB"),

            // 後方互換CLI構文 (既存: force xxx)
            "force yuy2" or "force yuyv" => SetOverride(VideoInputFormat.Yuy2, "YUY2"),
            "force uyvy" => SetOverride(VideoInputFormat.Uyvy, "UYVY"),
            "force rgb" => SetOverride(VideoInputFormat.Rgb, "RGB"),
            "force nv12" => SetOverride(VideoInputFormat.Nv12, "NV12"),
            "reset" => ResetOverride(),

            // HDR制御
            "hdr on" or "enable hdr" => SetHdr(true),
            "hdr off" or "disable hdr" => SetHdr(false),

            // ステータス表示
            "status" => ShowStatus(),

            // RAWバイトダンプ
            "dump" => RequestDump(),

            // CPU検証フレーム保存
            "verify" => RequestVerify(),

            // クロマオフセット自動計算
            "calibrate" => RequestCalibrate(),

            // クロマオフセットリセット
            "offset reset" => ResetChromaOffset(),

            _ => HandleOffsetOrUnknown(normalized)
        };
    }

    /// <summary>
    /// フォーマット上書きを設定
    /// </summary>
    /// <param name="format">上書きするフォーマット</param>
    /// <param name="displayName">表示用の名前</param>
    /// <returns>常にtrue</returns>
    private bool SetOverride(VideoInputFormat format, string displayName) {
        lock (_lock)
            _overrideFormat = format;
        logger.LogWarning("フォーマット強制上書き: {Format}", displayName);
        return true;
    }

    /// <summary>
    /// フォーマット上書きをリセット
    /// </summary>
    /// <returns>常にtrue</returns>
    private bool ResetOverride() {
        lock (_lock)
            _overrideFormat = null;
        logger.LogInformation("フォーマット上書きをリセット デバイス報告に従う");
        return true;
    }

    /// <summary>
    /// HDRトーンマッピングを有効化/無効化
    /// </summary>
    /// <param name="enable">有効化する場合true</param>
    /// <returns>常にtrue</returns>
    private bool SetHdr(bool enable) {
        lock (_lock)
            _enableToneMap = enable;
        logger.LogInformation("HDRトーンマッピング: {Status}", enable ? "有効" : "無効");
        return true;
    }

    /// <summary>
    /// 現在のパイプライン状態をコンソールに出力
    /// </summary>
    /// <returns>常にtrue</returns>
    private bool ShowStatus() {
        lock (_lock) {
            var effectiveFormat = _overrideFormat ?? _currentFormat;
            var overrideLabel = _overrideFormat.HasValue ? $" (上書き: {_overrideFormat.Value})" : " (自動)";
            logger.LogInformation(
                "パイプライン状態 - フォーマット: {Format}{Override}, HDR: {Hdr}",
                effectiveFormat,
                overrideLabel,
                _enableToneMap ? "有効" : "無効");
        }
        return true;
    }

    /// <summary>
    /// RAWバイトダンプ要求をセット
    /// </summary>
    /// <returns>常にtrue</returns>
    private bool RequestDump() {
        DumpRequested = true;
        logger.LogInformation("次のフレームでRAWバイトダンプを実行します...");
        return true;
    }

    /// <summary>
    /// CPU検証フレーム保存要求をセット
    /// </summary>
    /// <returns>常にtrue</returns>
    private bool RequestVerify() {
        VerifyRequested = true;
        logger.LogInformation("次のフレームをCPU変換してファイル保存します...");
        return true;
    }

    /// <summary>
    /// 不明なコマンドをログ出力
    /// </summary>
    /// <param name="command">コマンド文字列</param>
    /// <returns>常にfalse</returns>
    private bool LogUnknownCommand(string command) {
        logger.LogWarning("不明なコマンド: {Command}", command);
        logger.LogInformation("利用可能コマンド: mode auto|yuy2|nv12|rgb, hdr on|off, calibrate, offset reset, status");
        return false;
    }

    /// <summary>
    /// クロマオフセット自動計算要求
    /// </summary>
    private bool RequestCalibrate() {
        CalibrateRequested = true;
        logger.LogInformation("次のフレームでクロマオフセットを自動計算します...");
        return true;
    }

    /// <summary>
    /// クロマオフセットをリセット
    /// </summary>
    private bool ResetChromaOffset() {
        ChromaOffsetU = 0f;
        ChromaOffsetV = 0f;
        logger.LogInformation("クロマオフセットをリセット (U=0, V=0)");
        return true;
    }

    /// <summary>
    /// offsetコマンドまたは不明コマンドの処理
    /// </summary>
    private bool HandleOffsetOrUnknown(string command) {
        // "offset u <val> v <val>" パターン
        if (command.StartsWith("offset ")) {
            var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // offset u <float> v <float>
            if (parts.Length >= 5 && parts[1] == "u" && parts[3] == "v"
                && float.TryParse(parts[2], out var uVal) && float.TryParse(parts[4], out var vVal)) {
                ChromaOffsetU = uVal / 255f;
                ChromaOffsetV = vVal / 255f;
                logger.LogInformation("クロマオフセット設定: U={U}, V={V} (0-255スケール)", uVal, vVal);
                return true;
            }
        }
        return LogUnknownCommand(command);
    }

    /// <summary>
    /// FourCCコードを<see cref="VideoInputFormat"/>に変換
    /// </summary>
    /// <param name="fourCC">FourCCコード文字列</param>
    /// <returns>対応する<see cref="VideoInputFormat"/></returns>
    private static VideoInputFormat ParseFourCC(string fourCC) {
        var normalized = fourCC.Trim().ToUpperInvariant();

        return normalized switch {
            "YUY2" or "YUYV" => VideoInputFormat.Yuy2,
            "UYVY" => VideoInputFormat.Uyvy,
            "YVYU" => VideoInputFormat.Yvyu,
            "VYUY" => VideoInputFormat.Vyuy,
            "NV12" => VideoInputFormat.Nv12,
            "RGB" or "RGB24" or "RGB32" or "ARGB" => VideoInputFormat.Rgb,
            _ => VideoInputFormat.Unknown
        };
    }
}

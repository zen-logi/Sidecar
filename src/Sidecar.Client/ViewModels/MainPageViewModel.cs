// <copyright file="MainPageViewModel.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sidecar.Client.Interfaces;
using Sidecar.Shared;
using Sidecar.Shared.Models;

namespace Sidecar.Client.ViewModels;

/// <summary>
/// 色補正モード。
/// </summary>
public enum ColorMode
{
    /// <summary>
    /// デフォルト（補正なし）。
    /// </summary>
    Default,

    /// <summary>
    /// 赤と青を入れ替え (BGR -> RGB)。
    /// </summary>
    SwapRedBlue,

    // RGB Permutations for debugging
    RGB, // Identity
    RBG,
    GRB,
    GBR,
    BRG, // (SwapRedBlue)
    BGR,

    /// <summary>
    /// SDRディスプレイ向け補正。
    /// </summary>
    SDRDisplayLike,

    /// <summary>
    /// グレースケール。
    /// </summary>
    Grayscale,

    /// <summary>
    /// HDR (Rec.2020) -> SDR (Rec.709) Tone Mapping.
    /// </summary>
    HDRToSDR,

    /// <summary>
    /// YUV -> RGB Recovery (Rescue Purple).
    /// </summary>
    RescuePurple,

    /// <summary>
    /// Grayscale using only Red channel (Luma fallback).
    /// </summary>
    GrayscaleRed,

    // Channel Inspection
    InspectRed,
    InspectGreen,
    InspectBlue,
}


/// <summary>
/// MainPageのViewModel。
/// </summary>
public partial class MainPageViewModel : ObservableObject, IDisposable
{
    private readonly IStreamClient _streamClient;
    private CancellationTokenSource? _connectionTokenSource;
    private bool _disposed;

    /// <summary>
    /// 色補正モードの選択肢。
    /// </summary>
    public class ColorModeOption
    {
        public string DisplayName { get; set; } = string.Empty;
        public ColorMode Mode { get; set; }
    }

    /// <summary>
    /// 利用可能な色補正モードのリスト。
    /// </summary>
    public IReadOnlyList<ColorModeOption> ColorModeOptions { get; } = new List<ColorModeOption>
    {
        new() { DisplayName = "Default", Mode = ColorMode.Default },
        new() { DisplayName = "Rescue Purple (YUV Fix)", Mode = ColorMode.RescuePurple },
        new() { DisplayName = "HDR to SDR (Tone Map)", Mode = ColorMode.HDRToSDR },
        new() { DisplayName = "SDR Boost (Brighten)", Mode = ColorMode.SDRDisplayLike },
        new() { DisplayName = "Swap Red/Blue", Mode = ColorMode.SwapRedBlue },
        new() { DisplayName = "Force RGB", Mode = ColorMode.RGB },
        new() { DisplayName = "Force GBR", Mode = ColorMode.GBR },
        new() { DisplayName = "Grayscale (Mix)", Mode = ColorMode.Grayscale },
        new() { DisplayName = "Grayscale (Red Ch)", Mode = ColorMode.GrayscaleRed },
        new() { DisplayName = "Inspect Red Channel", Mode = ColorMode.InspectRed },
        new() { DisplayName = "Inspect Green Channel", Mode = ColorMode.InspectGreen },
        new() { DisplayName = "Inspect Blue Channel", Mode = ColorMode.InspectBlue },
    };

    /// <summary>
    /// 選択された色補正モード。
    /// </summary>
    [ObservableProperty]
    public partial ColorModeOption SelectedColorModeOption { get; set; }

    partial void OnSelectedColorModeOptionChanged(ColorModeOption value)
    {
        // 変更通知が必要な場合はここに記述
    }

    /// <summary>
    /// <see cref="MainPageViewModel"/> クラスの新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="streamClient">ストリームクライアント。</param>
    public MainPageViewModel(IStreamClient streamClient)
    {
        _streamClient = streamClient ?? throw new ArgumentNullException(nameof(streamClient));
        _streamClient.FrameReceived += OnFrameReceived;
        
        // 初期選択
        SelectedColorModeOption = ColorModeOptions[0];
    }

    /// <summary>
    /// 接続先ホストアドレス。
    /// </summary>
    [ObservableProperty]
    public partial string HostAddress { get; set; } = string.Empty;

    /// <summary>
    /// ポート番号。
    /// </summary>
    [ObservableProperty]
    public partial string Port { get; set; } = StreamingConstants.DefaultPort.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// 接続状態メッセージ。
    /// </summary>
    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "切断済み";

    /// <summary>
    /// 接続中かどうか。
    /// </summary>
    [ObservableProperty]
    public partial bool IsConnected { get; set; }

    /// <summary>
    /// 接続処理中かどうか。
    /// </summary>
    [ObservableProperty]
    public partial bool IsConnecting { get; set; }

    /// <summary>
    /// 最新フレームを取得したときに発生するイベント。
    /// </summary>
    public event EventHandler<byte[]>? FrameUpdated;

    /// <summary>
    /// サーバーへ接続します。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        if (string.IsNullOrWhiteSpace(HostAddress))
        {
            StatusMessage = "ホストアドレスを入力してください";
            return;
        }

        if (!int.TryParse(Port, out var portNumber) || portNumber < 1 || portNumber > 65535)
        {
            StatusMessage = "有効なポート番号を入力してください";
            return;
        }

        IsConnecting = true;
        StatusMessage = "接続中...";

        try
        {
            _connectionTokenSource = new CancellationTokenSource();
            await _streamClient.ConnectAsync(HostAddress, portNumber, _connectionTokenSource.Token);

            IsConnected = true;
            StatusMessage = $"{HostAddress}:{portNumber} に接続済み";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "接続がキャンセルされました";
        }
        catch (Exception ex)
        {
            StatusMessage = $"接続エラー: {ex.Message}";
        }
        finally
        {
            IsConnecting = false;
        }
    }

    /// <summary>
    /// 接続を切断します。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private void Disconnect()
    {
        _connectionTokenSource?.Cancel();
        _streamClient.Disconnect();

        IsConnected = false;
        StatusMessage = "切断済み";
    }

    /// <summary>
    /// 接続可能かどうかを取得します。
    /// </summary>
    private bool CanConnect() => !IsConnected && !IsConnecting;

    /// <summary>
    /// 切断可能かどうかを取得します。
    /// </summary>
    private bool CanDisconnect() => IsConnected;

    /// <summary>
    /// 最新のフレームを取得します。
    /// </summary>
    /// <returns>JPEG圧縮されたフレームデータ。フレームがない場合はnull。</returns>
    public byte[]? GetLatestFrame() => _streamClient.GetLatestFrame();

    /// <summary>
    /// フレーム受信時のイベントハンドラ。
    /// </summary>
    private void OnFrameReceived(object? sender, FrameEventArgs e)
    {
        var handler = FrameUpdated;
        handler?.Invoke(this, e.Frame.JpegData);
    }

    /// <summary>
    /// IsConnected プロパティ変更時に呼び出されます。
    /// </summary>
    partial void OnIsConnectedChanged(bool value)
    {
        ConnectCommand.NotifyCanExecuteChanged();
        DisconnectCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// IsConnecting プロパティ変更時に呼び出されます。
    /// </summary>
    partial void OnIsConnectingChanged(bool value)
    {
        ConnectCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// リソースを解放します。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _streamClient.FrameReceived -= OnFrameReceived;
        _connectionTokenSource?.Dispose();
        _disposed = true;

        GC.SuppressFinalize(this);
    }
}

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
/// MainPageのViewModel。
/// </summary>
public partial class MainPageViewModel : ObservableObject, IDisposable
{
    private readonly IStreamClient _streamClient;
    private CancellationTokenSource? _connectionTokenSource;
    private bool _disposed;

    /// <summary>
    /// <see cref="MainPageViewModel"/> クラスの新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="streamClient">ストリームクライアント。</param>
    public MainPageViewModel(IStreamClient streamClient)
    {
        _streamClient = streamClient ?? throw new ArgumentNullException(nameof(streamClient));
        _streamClient.FrameReceived += OnFrameReceived;
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

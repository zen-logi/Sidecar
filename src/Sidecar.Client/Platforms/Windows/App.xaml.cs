// <copyright file="App.xaml.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

namespace Sidecar.Client.WinUI;

/// <summary>
/// Windows用のアプリケーションクラス。
/// </summary>
public partial class App : MauiWinUIApplication
{
    /// <summary>
    /// <see cref="App"/> クラスの新しいインスタンスを初期化します。
    /// </summary>
    public App()
    {
        InitializeComponent();
    }

    /// <summary>
    /// MAUIアプリケーションを作成します。
    /// </summary>
    /// <returns>MAUIアプリケーション。</returns>
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}

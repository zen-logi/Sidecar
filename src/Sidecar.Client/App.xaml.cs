// <copyright file="App.xaml.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

namespace Sidecar.Client;

/// <summary>
/// MAUIアプリケーションクラス。
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// <see cref="App"/> クラスの新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="mainPage">メインページ。</param>
    public App(MainPage mainPage)
    {
        InitializeComponent();
    }

    /// <summary>
    /// メインウィンドウを作成します。
    /// </summary>
    /// <param name="activationState">アクティベーション状態。</param>
    /// <returns>作成されたウィンドウ。</returns>
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var mainPage = Handler?.MauiContext?.Services.GetService<MainPage>();
        return new Window(new NavigationPage(mainPage ?? new ContentPage()));
    }
}

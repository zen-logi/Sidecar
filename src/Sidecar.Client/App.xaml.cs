// <copyright file="App.xaml.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

namespace Sidecar.Client;

/// <summary>
/// MAUIアプリケーションクラス。
/// </summary>
public partial class App : Application
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// <see cref="App"/> クラスの新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="serviceProvider">サービスプロバイダー。</param>
    public App(IServiceProvider serviceProvider)
    {
        InitializeComponent();
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// メインウィンドウを作成します。
    /// </summary>
    /// <param name="activationState">アクティベーション状態。</param>
    /// <returns>作成されたウィンドウ。</returns>
    protected override Window CreateWindow(IActivationState? activationState)
    {
        // リソースがロードされた後にMainPageを作成
        var mainPage = _serviceProvider.GetRequiredService<MainPage>();
        return new Window(new NavigationPage(mainPage));
    }
}

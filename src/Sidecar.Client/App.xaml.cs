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
        var mainPage = _serviceProvider.GetRequiredService<MainPage>();
        var window = new Window(new NavigationPage(mainPage))
        {
            Title = "Sidecar"
        };

        // Restore window state (Windows only)
#if WINDOWS
        window.X = Preferences.Default.Get("WindowX", window.X);
        window.Y = Preferences.Default.Get("WindowY", window.Y);
        window.Width = Preferences.Default.Get("WindowWidth", window.Width);
        window.Height = Preferences.Default.Get("WindowHeight", window.Height);

        window.SizeChanged += (s, e) =>
        {
            Preferences.Default.Set("WindowWidth", window.Width);
            Preferences.Default.Set("WindowHeight", window.Height);
            Preferences.Default.Set("WindowX", window.X);
            Preferences.Default.Set("WindowY", window.Y);
        };
#endif

        return window;
    }
}

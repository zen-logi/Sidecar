// <copyright file="AppDelegate.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

using Foundation;
using UIKit;
using Sidecar.Client.Platforms.iOS;

namespace Sidecar.Client;

/// <summary>
/// iOSアプリケーションデリゲート
/// </summary>
[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    /// <summary>
    /// MAUIアプリケーションを作成
    /// </summary>
    /// <returns>MAUIアプリケーション</returns>
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
    {
        var result = base.FinishedLaunching(application, launchOptions);
        
        // Windowの背景色を黒に設定（白いバー対策のフェイルセーフ）
        if (Window != null)
        {
            Window.BackgroundColor = UIColor.Black;
        }

        return result;
    }

    /// <summary>
    /// 許可されている画面の向きを返す
    /// </summary>
    [Export("application:supportedInterfaceOrientationsForWindow:")]
    public UIInterfaceOrientationMask GetSupportedInterfaceOrientations(UIApplication application, UIWindow forWindow)
    {
        return OrientationService.CurrentMask;
    }
}

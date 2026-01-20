// <copyright file="AppDelegate.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

using Foundation;

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
}

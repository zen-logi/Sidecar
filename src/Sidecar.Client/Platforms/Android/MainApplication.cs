// <copyright file="MainApplication.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

using Android.App;
using Android.Runtime;

namespace Sidecar.Client;

/// <summary>
/// Androidアプリケーションクラス。
/// </summary>
[Application]
public class MainApplication : MauiApplication
{
    /// <summary>
    /// <see cref="MainApplication"/> クラスの新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="handle">Javaオブジェクトへのハンドル。</param>
    /// <param name="ownership">所有権の転送方法。</param>
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    /// <summary>
    /// MAUIアプリケーションを作成します。
    /// </summary>
    /// <returns>MAUIアプリケーション。</returns>
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}

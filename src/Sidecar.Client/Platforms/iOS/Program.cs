// <copyright file="Program.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

using ObjCRuntime;
using UIKit;

namespace Sidecar.Client;

/// <summary>
/// iOSアプリケーションのエントリーポイント。
/// </summary>
public static class Program
{
    /// <summary>
    /// アプリケーションのメインエントリーポイント。
    /// </summary>
    /// <param name="args">コマンドライン引数。</param>
    private static void Main(string[] args)
    {
        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}

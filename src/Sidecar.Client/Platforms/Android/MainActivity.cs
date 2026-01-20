// <copyright file="MainActivity.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

using Android.App;
using Android.Content.PM;
using Android.OS;

namespace Sidecar.Client;

/// <summary>
/// AndroidのメインActivity
/// </summary>
[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
}

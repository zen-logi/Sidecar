// <copyright file="OrientationService.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

using Android.Content.PM;
using Sidecar.Client.Interfaces;

namespace Sidecar.Client.Platforms.Android;

/// <summary>
/// Android向け画面の向き制御サービス
/// </summary>
public class OrientationService : IOrientationService
{
    /// <inheritdoc />
    public bool IsLandscape
    {
        get
        {
            var activity = Platform.CurrentActivity;
            return activity?.RequestedOrientation == ScreenOrientation.Landscape
                || activity?.RequestedOrientation == ScreenOrientation.ReverseLandscape
                || activity?.RequestedOrientation == ScreenOrientation.SensorLandscape;
        }
    }

    /// <inheritdoc />
    public void LockLandscape()
    {
        var activity = Platform.CurrentActivity;
        if (activity != null)
        {
            activity.RequestedOrientation = ScreenOrientation.SensorLandscape;
        }
    }

    /// <inheritdoc />
    public void LockPortrait()
    {
        var activity = Platform.CurrentActivity;
        if (activity != null)
        {
            activity.RequestedOrientation = ScreenOrientation.Portrait;
        }
    }

    /// <inheritdoc />
    public void Unlock()
    {
        var activity = Platform.CurrentActivity;
        if (activity != null)
        {
            activity.RequestedOrientation = ScreenOrientation.Unspecified;
        }
    }
}

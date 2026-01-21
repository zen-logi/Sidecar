// <copyright file="OrientationService.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

using Foundation;
using Sidecar.Client.Interfaces;
using UIKit;

namespace Sidecar.Client.Platforms.iOS;

/// <summary>
/// iOS向け画面の向き制御サービス
/// </summary>
public class OrientationService : IOrientationService
{
    private static UIInterfaceOrientationMask _currentMask = UIInterfaceOrientationMask.All;

    /// <summary>
    /// 現在の向きマスクを取得（AppDelegateから参照用）
    /// </summary>
    public static UIInterfaceOrientationMask CurrentMask => _currentMask;

    /// <inheritdoc />
    public bool IsLandscape => UIDevice.CurrentDevice.Orientation == UIDeviceOrientation.LandscapeLeft
                            || UIDevice.CurrentDevice.Orientation == UIDeviceOrientation.LandscapeRight;

    /// <inheritdoc />
    public void LockLandscape()
    {
        _currentMask = UIInterfaceOrientationMask.Landscape;
        SetOrientation(UIInterfaceOrientation.LandscapeRight);
        NotifyOrientationChanged();
    }

    /// <inheritdoc />
    public void LockPortrait()
    {
        _currentMask = UIInterfaceOrientationMask.Portrait;
        SetOrientation(UIInterfaceOrientation.Portrait);
        NotifyOrientationChanged();
    }

    /// <inheritdoc />
    public void Unlock()
    {
        _currentMask = UIInterfaceOrientationMask.All;
        NotifyOrientationChanged();
    }

    private static void NotifyOrientationChanged()
    {
        if (OperatingSystem.IsIOSVersionAtLeast(16))
        {
            var windowScene = UIApplication.SharedApplication.ConnectedScenes
                .OfType<UIWindowScene>()
                .FirstOrDefault();

            if (windowScene != null)
            {
                var rootViewController = windowScene.KeyWindow?.RootViewController;
                if (rootViewController != null)
                {
                    rootViewController.SetNeedsUpdateOfSupportedInterfaceOrientations();
                }
            }
        }
    }

    private static void SetOrientation(UIInterfaceOrientation orientation)
    {
        if (OperatingSystem.IsIOSVersionAtLeast(16))
        {
            var windowScene = UIApplication.SharedApplication.ConnectedScenes
                .OfType<UIWindowScene>()
                .FirstOrDefault();

            if (windowScene != null)
            {
                var mask = orientation switch
                {
                    UIInterfaceOrientation.LandscapeLeft => UIInterfaceOrientationMask.LandscapeLeft,
                    UIInterfaceOrientation.LandscapeRight => UIInterfaceOrientationMask.LandscapeRight,
                    UIInterfaceOrientation.Portrait => UIInterfaceOrientationMask.Portrait,
                    _ => UIInterfaceOrientationMask.All
                };
                var preferences = new UIWindowSceneGeometryPreferencesIOS(mask);
                windowScene.RequestGeometryUpdate(preferences, null);
            }
        }
        else
        {
            UIDevice.CurrentDevice.SetValueForKey(
                new NSNumber((int)orientation),
                new NSString("orientation"));
        }
    }
}

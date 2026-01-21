// <copyright file="BoolToColorConverter.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

using System.Globalization;

namespace Sidecar.Client.Converters;

/// <summary>
/// Bool値を色に変換するコンバーター（横画面ロック状態表示用）
/// </summary>
public sealed class BoolToColorConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isActive)
        {
            return isActive ? Color.FromArgb("#007AFF") : Color.FromArgb("#444444");
        }
        return Color.FromArgb("#444444");
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

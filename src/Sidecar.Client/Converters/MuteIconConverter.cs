// <copyright file="MuteIconConverter.cs" company="Sidecar">
// Copyright (c) Sidecar. All rights reserved.
// </copyright>

using System.Globalization;

namespace Sidecar.Client.Converters;

/// <summary>
/// ミュート状態をアイコン文字（Unicode）に変換するコンバーター
/// </summary>
public sealed class MuteIconConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isMuted)
        {
            return isMuted ? "🔇" : "🔊";
        }
        return "🔊";
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

using System;
using System.Globalization;
using Avalonia.Data.Converters;
using YoutubeDownloader.Services;

namespace YoutubeDownloader.Converters;

public sealed class LocalizedValueConverter : IValueConverter
{
    public static LocalizedValueConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Localization.Get(value?.ToString() ?? "Live");

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => throw new NotSupportedException();
}

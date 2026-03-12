using System;
using Microsoft.UI.Xaml.Data;

namespace VoiceStudio.App.Converters;

/// <summary>
/// Converts a normalized double (0-1) to a scaled height in pixels for VU meters.
/// ConverterParameter is the maximum height (e.g. 120).
/// </summary>
public class DoubleToScaledHeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not double level)
            return 0.0;

        var maxHeight = 120.0;
        if (parameter is string s && double.TryParse(s, out var parsed))
            maxHeight = parsed;
        else if (parameter is double d)
            maxHeight = d;

        var clamped = Math.Max(0, Math.Min(1, level));
        return clamped * maxHeight;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException("DoubleToScaledHeightConverter is one-way.");
    }
}

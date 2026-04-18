using System.Globalization;
using Avalonia.Data.Converters;

namespace SvalboardLayerViz.App.Converters;

/// <summary>
/// Returns true when the bound value equals the ConverterParameter.
/// Supports enum comparison by name (parameter as string vs enum value).
/// </summary>
public class EqualityToBoolConverter : IValueConverter
{
    public static readonly EqualityToBoolConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is null)
            return false;

        // Direct equality
        if (value.Equals(parameter))
            return true;

        // Enum value vs string parameter (common in AXAML ConverterParameter)
        if (value is Enum enumValue && parameter is string paramStr)
            return enumValue.ToString() == paramStr;

        // String representations
        return string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

using System.Globalization;
using System.Windows.Data;
using x.ai_client_PC.Models;
using x.ai_client_PC.Services;

namespace x.ai_client_PC.Converters;

public sealed class LocalizedMessageRoleConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length > 0 && values[0] is MessageRole role)
        {
            var languageCode = values.Length > 1 ? values[1]?.ToString() : null;
            var loc = new LocalizationService { LanguageCode = LocalizationService.NormalizeLanguageCode(languageCode) };
            return loc.GetMessageRole(role);
        }

        return string.Empty;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class LocalizedVideoStatusConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length > 0 && values[0] is VideoGenerationStatus status)
        {
            var languageCode = values.Length > 1 ? values[1]?.ToString() : null;
            var loc = new LocalizationService { LanguageCode = LocalizationService.NormalizeLanguageCode(languageCode) };
            return loc.GetVideoStatus(status);
        }

        return string.Empty;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

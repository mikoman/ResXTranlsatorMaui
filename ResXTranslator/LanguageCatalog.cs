using System.Globalization;
using System.Text;

namespace ResXTranslator;

/// <summary>
/// A language or regional/script variant and the BCP-47 culture name used for
/// the model request, RESX suffix, and spreadsheet column header.
/// </summary>
sealed record TargetLanguageOption(
    string DisplayName,
    string ColumnHeader,
    string NativeName)
{
    public string Detail => string.Equals(
        NormalizeSearchText(DisplayName),
        NormalizeSearchText(NativeName),
        StringComparison.Ordinal)
        ? ColumnHeader
        : $"{NativeName}  ·  {ColumnHeader}";

    public string SearchText { get; } = BuildSearchText(DisplayName, NativeName, ColumnHeader);

    public string ModelTarget => $"{DisplayName}; BCP-47 locale: {ColumnHeader}";

    public override string ToString() => $"{DisplayName} ({ColumnHeader})";

    static string BuildSearchText(string displayName, string nativeName, string cultureName)
    {
        var terms = $"{displayName} {nativeName} {cultureName}";
        var openParenthesis = displayName.IndexOf('(');
        var closeParenthesis = displayName.LastIndexOf(')');

        if (openParenthesis > 0 && closeParenthesis > openParenthesis)
        {
            var language = displayName[..openParenthesis].Trim();
            var variant = displayName[(openParenthesis + 1)..closeParenthesis].Trim();
            terms += $" {variant} {language}";
        }

        return NormalizeSearchText(terms);
    }

    internal static string NormalizeSearchText(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }
}

/// <summary>
/// The platform globalization catalog, including neutral languages and specific
/// regional/script variants such as French (Canada) and English (Singapore).
/// </summary>
static class LanguageCatalog
{
    public static readonly TargetLanguageOption[] All = CultureInfo
        .GetCultures(CultureTypes.AllCultures)
        .Where(culture =>
            !string.IsNullOrWhiteSpace(culture.Name) &&
            !culture.Name.StartsWith("qps-", StringComparison.OrdinalIgnoreCase))
        .DistinctBy(culture => culture.Name, StringComparer.OrdinalIgnoreCase)
        .Select(culture => new TargetLanguageOption(
            culture.EnglishName,
            culture.Name,
            culture.NativeName))
        .OrderBy(language => language.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ThenBy(language => language.ColumnHeader, StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

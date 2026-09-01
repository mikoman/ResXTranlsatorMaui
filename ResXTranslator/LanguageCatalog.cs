using DeepL;

namespace ResXTranslator;

/// <summary>
/// A DeepL target language, its display name, and the culture suffix used for
/// both the output <c>.resx</c> filename and the spreadsheet column header.
/// </summary>
sealed record TargetLanguageOption(string DisplayName, string DeepLCode, string ColumnHeader)
{
    public override string ToString() => $"{DisplayName} ({ColumnHeader})";
}

/// <summary>
/// The target languages offered in the UI. Edit this list to add or remove one.
/// </summary>
static class LanguageCatalog
{
    /// <summary>
    /// Every configured target language. Translating with no single language
    /// selected walks this whole list.
    /// </summary>
    public static readonly TargetLanguageOption[] All =
    [
        new("Portuguese (Portugal)", LanguageCode.PortugueseEuropean, "pt-PT"),
        new("Italian", LanguageCode.Italian, "it"),
        new("German", LanguageCode.German, "de"),
        new("Spanish", LanguageCode.Spanish, "es"),
        new("French", LanguageCode.French, "fr")
    ];
}

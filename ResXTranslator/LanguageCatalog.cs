namespace ResXTranslator;

/// <summary>
/// A target language display name and the culture suffix used for both the
/// output <c>.resx</c> filename and the spreadsheet column header.
/// </summary>
sealed record TargetLanguageOption(string DisplayName, string ColumnHeader)
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
        new("Portuguese (Portugal)", "pt-PT"),
        new("Italian", "it"),
        new("German", "de"),
        new("Spanish", "es"),
        new("French", "fr")
    ];
}

using DeepL;

namespace ResXTranslator;

public partial class MainPage : ContentPage
{
    const string AuthKeyPreferenceKey = "deepl_auth_key";
    const int BatchSize = 50;

    static readonly TargetLanguageOption[] DefaultTargetLanguages =
    [
        new("Portuguese (Portugal)", LanguageCode.PortugueseEuropean, "pt-PT"),
        new("Italian", LanguageCode.Italian, "it"),
        new("German", LanguageCode.German, "de"),
        new("Spanish", LanguageCode.Spanish, "es"),
        new("French", LanguageCode.French, "fr")
    ];

    string? _selectedFilePath;
    TranslationSpreadsheetDocument? _translationDocument;

    public MainPage()
    {
        InitializeComponent();
        LanguagePicker.ItemsSource = DefaultTargetLanguages;
        AuthKeyEntry.Text = Preferences.Default.Get(AuthKeyPreferenceKey, string.Empty);
    }

    public Dictionary<string, string> ReadResXFile(string path) => new ResXParser().ReadResXFile(path);

    public void WriteResXFile(string path, Dictionary<string, string> values) =>
        new ResXParser().WriteResXFile(path, values);

    void OnAuthKeyCompleted(object? sender, EventArgs e) => PersistAuthKey();

    void OnAuthKeyUnfocused(object? sender, FocusEventArgs e) => PersistAuthKey();

    void PersistAuthKey() => Preferences.Default.Set(AuthKeyPreferenceKey, AuthKeyEntry.Text?.Trim() ?? string.Empty);

    async void OnFilePickerButtonClicked(object? sender, EventArgs e)
    {
        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Select a RESX, Excel, or CSV file"
            });

            if (result is null)
            {
                return;
            }

            var extension = Path.GetExtension(result.FileName).ToLowerInvariant();

            if (extension is not (".resx" or ".csv" or ".xlsx"))
            {
                StatusLabel.Text = $"'{result.FileName}' is not a supported .resx, .csv, or .xlsx file.";
                return;
            }

            TranslationSpreadsheetDocument? nextDocument = null;

            if (extension is ".csv" or ".xlsx")
            {
                nextDocument = await Task.Run(() => TranslationSpreadsheetDocument.Load(result.FullPath));
            }

            _translationDocument?.Dispose();
            _translationDocument = nextDocument;
            _selectedFilePath = result.FullPath;
            SelectedFileLabel.Text = result.FullPath;

            StatusLabel.Text = nextDocument is null
                ? $"RESX selected. Output folder: {GetOutputDirectory(result.FullPath)}"
                : $"Loaded {nextDocument.EntryCount} rows. Existing languages: " +
                  $"{string.Join(", ", nextDocument.LanguageHeaders)}. " +
                  $"Output folder: {GetOutputDirectory(result.FullPath)}";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Could not open the selected file: {ex.Message}";
        }
    }

    async void OnTranslateButtonClicked(object? sender, EventArgs e)
    {
        SetBusy(true);

        try
        {
            var path = _selectedFilePath ?? throw new InvalidOperationException("Please select a file first.");
            var authKey = AuthKeyEntry.Text?.Trim();

            if (string.IsNullOrWhiteSpace(authKey))
            {
                throw new InvalidOperationException("Please enter your DeepL API key first.");
            }

            PersistAuthKey();

            using var client = new DeepLClient(authKey);

            if (IsResX(path))
            {
                await TranslateResXAsync(client, path);
            }
            else
            {
                await TranslateSpreadsheetAsync(client, path);
            }
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"An error occurred: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    async Task TranslateResXAsync(DeepLClient client, string path)
    {
        var values = ReadResXFile(path);

        if (values.Count == 0)
        {
            throw new InvalidOperationException("The selected RESX file does not contain any string entries.");
        }

        var languagesToTranslate = GetSelectedLanguages();
        var outputDirectory = GetOutputDirectory(path);
        var baseName = Path.GetFileNameWithoutExtension(path);
        var writtenFiles = new List<string>();
        var keys = values.Keys.ToArray();
        var sourceTexts = values.Values.ToArray();
        var totalSteps = (double)languagesToTranslate.Count * sourceTexts.Length;
        var completedSteps = 0d;

        translationProgressBar.Progress = 0;

        foreach (var targetLanguage in languagesToTranslate)
        {
            var translatedValues = new Dictionary<string, string>(keys.Length);

            for (var offset = 0; offset < sourceTexts.Length; offset += BatchSize)
            {
                var batch = sourceTexts.Skip(offset).Take(BatchSize).ToArray();
                var results = await client.TranslateTextAsync(
                    batch,
                    LanguageCode.English,
                    targetLanguage.DeepLCode);

                for (var index = 0; index < results.Length; index++)
                {
                    translatedValues[keys[offset + index]] = results[index].Text;
                }

                completedSteps += batch.Length;
                await UpdateProgressAsync(
                    completedSteps,
                    totalSteps,
                    $"Translating RESX to {targetLanguage.ColumnHeader}");
            }

            var outputPath = Path.Combine(
                outputDirectory,
                $"{baseName}.{targetLanguage.ColumnHeader}.resx");

            WriteResXFile(outputPath, translatedValues);
            writtenFiles.Add(outputPath);
        }

        var usageSummary = await GetUsageSummaryAsync(client);
        StatusLabel.Text = $"Translation complete. {usageSummary}" + Environment.NewLine +
            string.Join(Environment.NewLine, writtenFiles);
    }

    async Task TranslateSpreadsheetAsync(DeepLClient client, string path)
    {
        var document = _translationDocument
            ?? throw new InvalidOperationException("Please select the spreadsheet again.");

        var selectedLanguage = LanguagePicker.SelectedItem as TargetLanguageOption;
        var languagesToTranslate = selectedLanguage is null
            ? DefaultTargetLanguages.Where(language => !document.HasLanguage(language.ColumnHeader)).ToArray()
            : [selectedLanguage];

        if (selectedLanguage is not null && document.HasLanguage(selectedLanguage.ColumnHeader))
        {
            throw new InvalidOperationException(
                $"The document already contains a '{selectedLanguage.ColumnHeader}' translation column.");
        }

        if (languagesToTranslate.Length == 0)
        {
            throw new InvalidOperationException("The document already contains every configured target language.");
        }

        var sourceRows = document.GetSourceRows();

        if (sourceRows.Count == 0)
        {
            throw new InvalidOperationException("The document has no non-empty values in 'Default' or 'en-US'.");
        }

        var totalSteps = (double)languagesToTranslate.Length * sourceRows.Count;
        var completedSteps = 0d;
        translationProgressBar.Progress = 0;

        foreach (var targetLanguage in languagesToTranslate)
        {
            var translatedValues = new Dictionary<int, string>(sourceRows.Count);

            for (var offset = 0; offset < sourceRows.Count; offset += BatchSize)
            {
                var batchRows = sourceRows.Skip(offset).Take(BatchSize).ToArray();
                var results = await client.TranslateTextAsync(
                    batchRows.Select(row => row.Text).ToArray(),
                    LanguageCode.English,
                    targetLanguage.DeepLCode);

                for (var index = 0; index < results.Length; index++)
                {
                    translatedValues[batchRows[index].RowNumber] = results[index].Text;
                }

                completedSteps += batchRows.Length;
                await UpdateProgressAsync(
                    completedSteps,
                    totalSteps,
                    $"Translating spreadsheet to {targetLanguage.ColumnHeader}");
            }

            document.AddLanguage(targetLanguage.ColumnHeader, translatedValues);
        }

        var extension = Path.GetExtension(path).ToLowerInvariant();
        var outputPath = GetTabularOutputPath(path, extension, translated: true);

        await Task.Run(() => SaveDocument(document, outputPath, extension));
        var usageSummary = await GetUsageSummaryAsync(client);
        StatusLabel.Text = $"Translation complete. Added {string.Join(", ", languagesToTranslate.Select(x => x.ColumnHeader))}." +
            $" {usageSummary}" + Environment.NewLine + outputPath;
    }

    async Task UpdateProgressAsync(double completedSteps, double totalSteps, string message)
    {
        await translationProgressBar.ProgressTo(completedSteps / totalSteps, 50, Easing.Linear);
        StatusLabel.Text = $"{message}: {completedSteps:0}/{totalSteps:0} entries…";
    }

    static async Task<string> GetUsageSummaryAsync(DeepLClient client)
    {
        try
        {
            var usage = await client.GetUsageAsync();

            return usage.Character is null
                ? "DeepL character usage is unavailable."
                : $"DeepL characters used this period: {usage.Character.Count}/{usage.Character.Limit}.";
        }
        catch (Exception)
        {
            return "DeepL character usage is unavailable.";
        }
    }

    async void OnSaveToExcelButtonClicked(object? sender, EventArgs e)
    {
        SetBusy(true);

        try
        {
            var path = _selectedFilePath ?? throw new InvalidOperationException("Please select a file first.");

            if (IsResX(path))
            {
                var values = ReadResXFile(path);
                var excelPath = Path.Combine(
                    GetOutputDirectory(path),
                    $"{Path.GetFileNameWithoutExtension(path)}.xlsx");

                await Task.Run(() => new ExcelGenerator().WriteResXToExcel(excelPath, values));
                StatusLabel.Text = $"Exported {values.Count} entries to {excelPath}";
                return;
            }

            var document = _translationDocument
                ?? throw new InvalidOperationException("Please select the spreadsheet again.");
            var outputPath = GetTabularOutputPath(path, ".xlsx", document.IsModified);

            await Task.Run(() => document.SaveAsExcel(outputPath));
            StatusLabel.Text = $"Exported {document.EntryCount} rows to {outputPath}";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"An error occurred: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    async void OnSaveToCsvButtonClicked(object? sender, EventArgs e)
    {
        SetBusy(true);

        try
        {
            var path = _selectedFilePath ?? throw new InvalidOperationException("Please select a file first.");

            if (IsResX(path))
            {
                var values = ReadResXFile(path);
                var csvPath = Path.Combine(
                    GetOutputDirectory(path),
                    $"{Path.GetFileNameWithoutExtension(path)}.csv");

                await Task.Run(() => new ExcelGenerator().WriteResXToCsv(csvPath, values));
                StatusLabel.Text = $"Exported {values.Count} entries to {csvPath}";
                return;
            }

            var document = _translationDocument
                ?? throw new InvalidOperationException("Please select the spreadsheet again.");
            var outputPath = GetTabularOutputPath(path, ".csv", document.IsModified);

            await Task.Run(() => document.SaveAsCsv(outputPath));
            StatusLabel.Text = $"Exported {document.EntryCount} rows to {outputPath}";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"An error occurred: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    IReadOnlyList<TargetLanguageOption> GetSelectedLanguages() =>
        LanguagePicker.SelectedItem is TargetLanguageOption selected
            ? [selected]
            : DefaultTargetLanguages;

    void SetBusy(bool busy)
    {
        PickFileButton.IsEnabled = !busy;
        TranslateButton.IsEnabled = !busy;
        ExportButton.IsEnabled = !busy;
        ExportCsvButton.IsEnabled = !busy;
    }

    static bool IsResX(string path) =>
        string.Equals(Path.GetExtension(path), ".resx", StringComparison.OrdinalIgnoreCase);

    static void SaveDocument(TranslationSpreadsheetDocument document, string path, string extension)
    {
        if (string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase))
        {
            document.SaveAsCsv(path);
        }
        else
        {
            document.SaveAsExcel(path);
        }
    }

    static string GetTabularOutputPath(string sourcePath, string extension, bool translated)
    {
        var suffix = translated ? ".translated" : ".exported";

        return Path.Combine(
            GetOutputDirectory(sourcePath),
            Path.GetFileNameWithoutExtension(sourcePath) + suffix + extension);
    }

    static string GetOutputDirectory(string sourcePath)
    {
        var sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(sourcePath));

        if (!string.IsNullOrEmpty(sourceDirectory) && IsWritable(sourceDirectory))
        {
            return sourceDirectory;
        }

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        if (string.IsNullOrEmpty(documents) || !IsWritable(documents))
        {
            documents = FileSystem.Current.AppDataDirectory;
        }

        var fallback = Path.Combine(documents, "ResXTranslator");
        Directory.CreateDirectory(fallback);
        return fallback;
    }

    static bool IsWritable(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return false;
            }

            var probePath = Path.Combine(directory, $".resxtranslator-{Guid.NewGuid():N}.tmp");
            using (File.Create(probePath))
            {
            }

            File.Delete(probePath);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    sealed record TargetLanguageOption(string DisplayName, string DeepLCode, string ColumnHeader)
    {
        public override string ToString() => $"{DisplayName} ({ColumnHeader})";
    }
}

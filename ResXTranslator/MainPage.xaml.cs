using ResXTranslator.Controls;

namespace ResXTranslator;

public partial class MainPage : ContentPage
{
    const int BatchSize = 50;

    /// <summary>Sentinel row meaning "every configured language".</summary>
    static readonly MenuOption AllLanguagesOption = new("All languages");

    readonly OpenRouterClient _openRouterClient = new();
    string? _selectedFilePath;
    TranslationSpreadsheetDocument? _translationDocument;
    string? _apiKey;
    IReadOnlyList<OpenRouterModel> _models = [];
    OpenRouterModel? _selectedModel;
    OpenRouterConnectionState _connectionState = OpenRouterConnectionState.NotConnected;
    OpenRouterTokenUsage _translationUsage;
    bool _catalogLoadedSuccessfully;
    bool _selectedModelUnavailable;
    bool _initialized;
    bool _isBusy;

    public MainPage()
    {
        InitializeComponent();
        RestoreSelectedModel();
        RenderLanguageOptions();
        RenderSourceRow();
        RenderOpenRouterState();
        UpdateActionState();

        Loaded += OnLoaded;
    }

    public Dictionary<string, string> ReadResXFile(string path) => new ResXParser().ReadResXFile(path);

    public void WriteResXFile(string path, Dictionary<string, string> values) =>
        new ResXParser().WriteResXFile(path, values);

    // ------------------------------------------------------------- OpenRouter

    async void OnLoaded(object? sender, EventArgs e)
    {
        WindowGeometry.ApplyOnce(Window);

        if (_initialized)
        {
            return;
        }

        _initialized = true;
        await InitializeOpenRouterAsync();
    }

    void RestoreSelectedModel()
    {
        var savedId = Preferences.Default.Get(OpenRouterSettings.ModelPreferenceKey, string.Empty);

        if (!string.IsNullOrWhiteSpace(savedId))
        {
            _selectedModel = new OpenRouterModel(savedId, savedId, null, null);
        }
    }

    async Task InitializeOpenRouterAsync()
    {
        try
        {
            _apiKey = await OpenRouterCredentialStore.GetAsync();
        }
        catch (Exception ex)
        {
            _connectionState = OpenRouterConnectionState.NeedsAttention;
            RenderOpenRouterState();
            UpdateActionState();
            ShowError($"The saved OpenRouter key could not be read from secure storage: {ex.Message}");
            return;
        }

        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _connectionState = OpenRouterConnectionState.NotConnected;
            RenderOpenRouterState();
            UpdateActionState();
            return;
        }

        _connectionState = OpenRouterConnectionState.Checking;
        RenderOpenRouterState();
        UpdateActionState();

        try
        {
            await _openRouterClient.ValidateApiKeyAsync(_apiKey);
        }
        catch (OpenRouterApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _connectionState = OpenRouterConnectionState.NeedsAttention;
            RenderOpenRouterState();
            UpdateActionState();
            return;
        }
        catch (Exception)
        {
            // A network or service failure does not prove a stored credential is
            // invalid. Keep it usable so the translation request can try later.
            _connectionState = OpenRouterConnectionState.Unverified;
            RenderOpenRouterState();
            UpdateActionState();
            return;
        }

        _connectionState = OpenRouterConnectionState.Connected;

        try
        {
            await RefreshModelsAsync();
        }
        catch (Exception)
        {
            // Catalog availability is independent of credential validity. Keep
            // the connected state and let the model sheet offer refresh/retry.
        }

        RenderOpenRouterState();
        UpdateActionState();
    }

    async Task RefreshModelsAsync()
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            return;
        }

        try
        {
            _models = await _openRouterClient.GetModelsAsync(_apiKey);
            _catalogLoadedSuccessfully = true;
            ResolveSelectedModel();
        }
        catch
        {
            _catalogLoadedSuccessfully = false;
            _selectedModelUnavailable = false;
            throw;
        }
    }

    void ResolveSelectedModel()
    {
        var savedId = Preferences.Default.Get(OpenRouterSettings.ModelPreferenceKey, string.Empty);

        if (string.IsNullOrWhiteSpace(savedId))
        {
            _selectedModel = null;
            _selectedModelUnavailable = false;
            return;
        }

        _selectedModel = _models.FirstOrDefault(model => model.Id == savedId)
            ?? new OpenRouterModel(savedId, savedId, null, null);
        _selectedModelUnavailable = _catalogLoadedSuccessfully &&
            !_models.Any(model => model.Id == savedId);
    }

    async void OnManageAccountClicked(object? sender, EventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        var page = new OpenRouterConnectionPage(
            _openRouterClient,
            !string.IsNullOrWhiteSpace(_apiKey),
            _connectionState);
        await Navigation.PushModalAsync(page);
        var result = await page.Completion;

        switch (result.Outcome)
        {
            case OpenRouterConnectionOutcome.Connected when !string.IsNullOrWhiteSpace(result.ApiKey):
                _apiKey = result.ApiKey;
                _connectionState = OpenRouterConnectionState.Connected;

                try
                {
                    await RefreshModelsAsync();
                }
                catch (Exception ex)
                {
                    ShowError($"Connected, but the model catalog could not be loaded: {ex.Message}");
                }

                break;
            case OpenRouterConnectionOutcome.Removed:
                _apiKey = null;
                _models = [];
                _catalogLoadedSuccessfully = false;
                _selectedModelUnavailable = false;
                _connectionState = OpenRouterConnectionState.NotConnected;
                break;
        }

        RenderOpenRouterState();
        UpdateActionState();
    }

    async void OnChooseModelClicked(object? sender, EventArgs e)
    {
        if (_isBusy || string.IsNullOrWhiteSpace(_apiKey))
        {
            return;
        }

        var page = new OpenRouterModelPage(
            _openRouterClient,
            _apiKey,
            _models,
            _selectedModel?.Id);
        await Navigation.PushModalAsync(page);
        var model = await page.Completion;

        if (model is null)
        {
            return;
        }

        _selectedModel = model;
        _selectedModelUnavailable = false;
        Preferences.Default.Set(OpenRouterSettings.ModelPreferenceKey, model.Id);
        RenderOpenRouterState();
        UpdateActionState();
    }

    void RenderOpenRouterState()
    {
        var (symbol, lightColor, darkColor, status) = _connectionState switch
        {
            OpenRouterConnectionState.Checking =>
                ("arrow.triangle.2.circlepath", "SecondaryLabelLight", "SecondaryLabelDark", "Checking…"),
            OpenRouterConnectionState.Connected =>
                ("checkmark.circle.fill", "SuccessLight", "SuccessDark", "Connected"),
            OpenRouterConnectionState.Unverified =>
                ("wifi.exclamationmark", "WarningLight", "WarningDark", "Couldn't verify"),
            OpenRouterConnectionState.NeedsAttention =>
                ("exclamationmark.triangle.fill", "DangerLight", "DangerDark", "Needs attention"),
            _ => ("key.fill", "SecondaryLabelLight", "SecondaryLabelDark", "Not connected")
        };

        AccountIcon.Symbol = symbol;
        AccountIcon.SetAppTheme(
            SymbolImage.TintProperty,
            Resource<Color>(lightColor),
            Resource<Color>(darkColor));
        AccountStatusLabel.Text = status;

        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            ModelNameLabel.Text = "Connect first";
            ModelIdLabel.IsVisible = false;
        }
        else if (_selectedModel is null)
        {
            ModelNameLabel.Text = "No model selected";
            ModelIdLabel.IsVisible = false;
        }
        else
        {
            ModelNameLabel.Text = _selectedModelUnavailable
                ? "Model unavailable"
                : _selectedModel.Name;
            ModelIdLabel.Text = _selectedModel.Id;
            ModelIdLabel.IsVisible = true;
        }
    }

    // -------------------------------------------------------------- Languages

    /// <summary>
    /// Rebuilds the pull-down. "All languages" leads so the selection can always
    /// be cleared, and a language the loaded spreadsheet already has renders
    /// disabled rather than throwing after Translate is pressed.
    /// </summary>
    void RenderLanguageOptions()
    {
        var options = new List<MenuOption>(LanguageCatalog.All.Length + 1) { AllLanguagesOption };

        foreach (var language in LanguageCatalog.All)
        {
            var alreadyPresent = _translationDocument?.HasLanguage(language.ColumnHeader) == true;

            options.Add(new MenuOption(
                language.DisplayName,
                language,
                alreadyPresent ? "already translated" : null,
                !alreadyPresent));
        }

        LanguageMenuButton.SetOptions(options);
        LanguageMenuButton.SelectedOption ??= AllLanguagesOption;
    }

    void OnLanguageSelected(object? sender, MenuOption option) => UpdateActionState();

    // Apple sets ShowsMenuAsPrimaryAction, which suppresses TouchUpInside, so
    // Clicked never fires there. Android has no anchored pull-down and needs the
    // action-sheet fallback.
    void OnLanguageMenuFallbackClicked(object? sender, EventArgs e)
    {
#if ANDROID
        _ = MenuButtonPlatform.ShowFallbackAsync(LanguageMenuButton);
#endif
    }

    TargetLanguageOption? GetSelectedLanguage() =>
        LanguageMenuButton.SelectedOption?.Tag as TargetLanguageOption;

    IReadOnlyList<TargetLanguageOption> GetSelectedLanguages() =>
        GetSelectedLanguage() is { } selected ? [selected] : LanguageCatalog.All;

    // ------------------------------------------------------------- Source file

    async void OnFilePickerButtonClicked(object? sender, EventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

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

            await LoadSourceFileAsync(result.FullPath);
        }
        catch (Exception ex)
        {
            ShowError($"Could not open the selected file: {ex.Message}");
        }
    }

    /// <summary>
    /// Single entry point for both the file picker and a Finder drop, so the two
    /// paths cannot drift apart.
    /// </summary>
    async Task LoadSourceFileAsync(string fullPath)
    {
        var extension = Path.GetExtension(fullPath).ToLowerInvariant();

        if (extension is not (".resx" or ".csv" or ".xlsx"))
        {
            ShowError($"'{Path.GetFileName(fullPath)}' is not a supported .resx, .csv, or .xlsx file.");
            return;
        }

        TranslationSpreadsheetDocument? nextDocument = null;

        if (extension is ".csv" or ".xlsx")
        {
            nextDocument = await Task.Run(() => TranslationSpreadsheetDocument.Load(fullPath));
        }

        _translationDocument?.Dispose();
        _translationDocument = nextDocument;
        _selectedFilePath = fullPath;

        RenderSourceRow();
        RenderLanguageOptions();
        UpdateActionState();
        ClearStatus();
    }

    void RenderSourceRow()
    {
        if (_selectedFilePath is null)
        {
            SourceIcon.Symbol = "doc.badge.plus";
            SourceTitleLabel.Text = "No file selected";
            SourceSubtitleLabel.Text = "RESX, Excel or CSV — or drag one here";
            SourceSubtitleLabel.IsVisible = true;
            SourceSeparator.IsVisible = false;
            SourceDetailLabel.IsVisible = false;
            PickFileButton.Text = "Choose File…";
            return;
        }

        SourceIcon.Symbol = "doc.text.fill";
        SourceTitleLabel.Text = Path.GetFileName(_selectedFilePath);
        SourceSubtitleLabel.IsVisible = false;

        var facts = new List<string>();

        if (_translationDocument is { } document)
        {
            facts.Add($"{document.EntryCount} {(document.EntryCount == 1 ? "row" : "rows")}");

            if (document.LanguageHeaders.Count > 0)
            {
                facts.Add($"already has {string.Join(", ", document.LanguageHeaders)}");
            }
        }
        else
        {
            var entryCount = TryCountResXEntries(_selectedFilePath);

            if (entryCount is { } count)
            {
                facts.Add($"{count} {(count == 1 ? "entry" : "entries")}");
            }
        }

        facts.Add($"saves to {DescribeFolder(GetOutputDirectory(_selectedFilePath))}");

        SourceDetailLabel.Text = string.Join("  ·  ", facts);
        SourceSeparator.IsVisible = true;
        SourceDetailLabel.IsVisible = true;
        PickFileButton.Text = "Change…";
    }

    int? TryCountResXEntries(string path)
    {
        try
        {
            return ReadResXFile(path).Count;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Shortens a home-relative path the way a Mac app would show it.</summary>
    static string Abbreviate(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return !string.IsNullOrEmpty(home) && path.StartsWith(home, StringComparison.Ordinal)
            ? string.Concat("~", path.AsSpan(home.Length))
            : path;
    }

    /// <summary>
    /// A folder as a Mac app names it in passing: the containing folder, not a
    /// full path that truncates to noise.
    /// </summary>
    static string DescribeFolder(string directory)
    {
        var abbreviated = Abbreviate(directory);

        if (abbreviated.StartsWith('~'))
        {
            return abbreviated;
        }

        var name = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar));

        return string.IsNullOrEmpty(name) ? abbreviated : $"…/{name}";
    }

    // --------------------------------------------------------------- File drop

    void OnSourceDragOver(object? sender, DragEventArgs e)
    {
        if (_isBusy)
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;
        SourceCard.Stroke = (Color)(Application.Current?.RequestedTheme == AppTheme.Dark
            ? Application.Current.Resources["AccentTextDark"]
            : Application.Current?.Resources["AccentTextLight"] ?? Colors.Transparent);
        SourceCard.StrokeThickness = 2;
    }

    void OnSourceDragLeave(object? sender, DragEventArgs e) => ResetDropHighlight();

    void ResetDropHighlight()
    {
        SourceCard.StrokeThickness = 1;
        SourceCard.Stroke = (Color)(Application.Current?.RequestedTheme == AppTheme.Dark
            ? Application.Current.Resources["SeparatorDark"]
            : Application.Current?.Resources["SeparatorLight"] ?? Colors.Transparent);
    }

    /// <summary>
    /// A Finder drag arrives with an empty DataPackage — the payload is on the
    /// native drop session, which MAUI surfaces through PlatformArgs.
    /// </summary>
    void OnSourceDrop(object? sender, DropEventArgs e)
    {
        ResetDropHighlight();

        if (_isBusy)
        {
            return;
        }

#if IOS || MACCATALYST
        var session = e.PlatformArgs?.DropSession;

        if (session is null)
        {
            return;
        }

        e.Handled = true;

        foreach (var item in session.Items)
        {
            var provider = item.ItemProvider;
            var suggestedName = provider.SuggestedName ?? string.Empty;
            var extension = Path.GetExtension(suggestedName).ToLowerInvariant();

            if (extension is not (".resx" or ".csv" or ".xlsx"))
            {
                continue;
            }

            // The URL is valid only inside the completion block, so copy there.
            provider.LoadFileRepresentation(
                UniformTypeIdentifiers.UTTypes.Item.Identifier,
                (url, error) =>
                {
                    if (error is not null || url?.Path is not { } sourcePath)
                    {
                        return;
                    }

                    string workingPath;

                    try
                    {
                        if (File.Exists(sourcePath))
                        {
                            workingPath = Path.Combine(FileSystem.CacheDirectory, suggestedName);
                            File.Copy(sourcePath, workingPath, overwrite: true);
                        }
                        else
                        {
                            return;
                        }
                    }
                    catch (Exception)
                    {
                        return;
                    }

                    // Completions arrive off the main thread.
                    Dispatcher.Dispatch(async () =>
                    {
                        try
                        {
                            await LoadSourceFileAsync(workingPath);
                        }
                        catch (Exception ex)
                        {
                            ShowError($"Could not open the dropped file: {ex.Message}");
                        }
                    });
                });

            return;
        }
#endif
    }

    // --------------------------------------------------------------- Translate

    async void OnTranslateButtonClicked(object? sender, EventArgs e)
    {
        SetBusy(true);

        try
        {
            var path = _selectedFilePath ?? throw new InvalidOperationException("Please select a file first.");
            var apiKey = _apiKey ?? throw new InvalidOperationException("Connect an OpenRouter account first.");
            var model = _selectedModel ?? throw new InvalidOperationException("Choose an OpenRouter model first.");

            if (_selectedModelUnavailable)
            {
                throw new InvalidOperationException("The selected OpenRouter model is unavailable. Choose another model.");
            }

            _translationUsage = default;

            if (IsResX(path))
            {
                await TranslateResXAsync(apiKey, model, path);
            }
            else
            {
                await TranslateSpreadsheetAsync(apiKey, model, path);
            }
        }
        catch (OpenRouterApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _connectionState = OpenRouterConnectionState.NeedsAttention;
            RenderOpenRouterState();
            ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    async Task TranslateResXAsync(string apiKey, OpenRouterModel model, string path)
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

        SetProgress(0);

        foreach (var targetLanguage in languagesToTranslate)
        {
            var translatedValues = new Dictionary<string, string>(keys.Length);

            for (var offset = 0; offset < sourceTexts.Length; offset += BatchSize)
            {
                var batch = sourceTexts
                    .Skip(offset)
                    .Take(BatchSize)
                    .Select((text, index) => new OpenRouterTranslationInput(offset + index, text))
                    .ToArray();
                var result = await _openRouterClient.TranslateAsync(
                    apiKey,
                    model.Id,
                    $"{targetLanguage.DisplayName} ({targetLanguage.ColumnHeader})",
                    batch);

                foreach (var translation in result.Translations)
                {
                    translatedValues[keys[translation.Key]] = translation.Value;
                }

                _translationUsage += result.Usage;
                completedSteps += batch.Length;
                UpdateProgress(
                    completedSteps,
                    totalSteps,
                    $"Translating into {targetLanguage.DisplayName}");
            }

            var outputPath = Path.Combine(
                outputDirectory,
                $"{baseName}.{targetLanguage.ColumnHeader}.resx");

            WriteResXFile(outputPath, translatedValues);
            writtenFiles.Add(outputPath);
        }

        ShowUsage(BuildUsageSummary(model));
        ShowSuccess(
            writtenFiles.Count == 1
                ? "Translated into 1 language"
                : $"Translated into {writtenFiles.Count} languages",
            null,
            writtenFiles);
    }

    async Task TranslateSpreadsheetAsync(string apiKey, OpenRouterModel model, string path)
    {
        var document = _translationDocument
            ?? throw new InvalidOperationException("Please select the spreadsheet again.");

        var selectedLanguage = GetSelectedLanguage();
        var languagesToTranslate = selectedLanguage is null
            ? LanguageCatalog.All.Where(language => !document.HasLanguage(language.ColumnHeader)).ToArray()
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
        SetProgress(0);

        foreach (var targetLanguage in languagesToTranslate)
        {
            var translatedValues = new Dictionary<int, string>(sourceRows.Count);

            for (var offset = 0; offset < sourceRows.Count; offset += BatchSize)
            {
                var batchRows = sourceRows.Skip(offset).Take(BatchSize).ToArray();
                var batch = batchRows
                    .Select(row => new OpenRouterTranslationInput(row.RowNumber, row.Text))
                    .ToArray();
                var result = await _openRouterClient.TranslateAsync(
                    apiKey,
                    model.Id,
                    $"{targetLanguage.DisplayName} ({targetLanguage.ColumnHeader})",
                    batch);

                foreach (var translation in result.Translations)
                {
                    translatedValues[translation.Key] = translation.Value;
                }

                _translationUsage += result.Usage;
                completedSteps += batchRows.Length;
                UpdateProgress(
                    completedSteps,
                    totalSteps,
                    $"Translating into {targetLanguage.DisplayName}");
            }

            document.AddLanguage(targetLanguage.ColumnHeader, translatedValues);
        }

        var extension = Path.GetExtension(path).ToLowerInvariant();
        var outputPath = GetTabularOutputPath(path, extension, translated: true);

        await Task.Run(() => SaveDocument(document, outputPath, extension));

        ShowUsage(BuildUsageSummary(model));
        RenderLanguageOptions();
        ShowSuccess(
            $"Added {string.Join(", ", languagesToTranslate.Select(language => language.ColumnHeader))}",
            null,
            [outputPath]);
    }

    string BuildUsageSummary(OpenRouterModel model) =>
        $"{model.Name}  ·  {_translationUsage.PromptTokens:N0} input  ·  " +
        $"{_translationUsage.CompletionTokens:N0} output  ·  {_translationUsage.TotalTokens:N0} total tokens";

    // ------------------------------------------------------------------ Export

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
                ShowSuccess($"Exported {values.Count:N0} entries", null, [excelPath]);
                return;
            }

            var document = _translationDocument
                ?? throw new InvalidOperationException("Please select the spreadsheet again.");
            var outputPath = GetTabularOutputPath(path, ".xlsx", document.IsModified);

            await Task.Run(() => document.SaveAsExcel(outputPath));
            ShowSuccess($"Exported {document.EntryCount:N0} rows", null, [outputPath]);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
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
                ShowSuccess($"Exported {values.Count:N0} entries", null, [csvPath]);
                return;
            }

            var document = _translationDocument
                ?? throw new InvalidOperationException("Please select the spreadsheet again.");
            var outputPath = GetTabularOutputPath(path, ".csv", document.IsModified);

            await Task.Run(() => document.SaveAsCsv(outputPath));
            ShowSuccess($"Exported {document.EntryCount:N0} rows", null, [outputPath]);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ------------------------------------------------------------------- State

    void SetBusy(bool busy)
    {
        _isBusy = busy;
        PickFileButton.IsEnabled = !busy;
        ManageAccountButton.IsEnabled = !busy;
        ChooseModelButton.IsEnabled = !busy && !string.IsNullOrWhiteSpace(_apiKey);
        LanguageMenuButton.IsEnabled = !busy;
        ProgressGroup.IsVisible = busy;

        if (busy)
        {
            ResultGroup.IsVisible = false;
            StatusBlock.IsVisible = true;
            SetProgress(0);
            ProgressLabel.Text = "Starting…";
        }

        UpdateActionState();
    }

    /// <summary>
    /// Actions are enabled by what the app actually has, not merely by whether a
    /// job is running: Translate needs a file, usable account and available model.
    /// </summary>
    void UpdateActionState()
    {
        var hasFile = _selectedFilePath is not null;
        var hasUsableKey = !string.IsNullOrWhiteSpace(_apiKey) &&
            _connectionState is OpenRouterConnectionState.Connected or OpenRouterConnectionState.Unverified;
        var hasModel = _selectedModel is not null && !_selectedModelUnavailable;

        ManageAccountButton.IsEnabled = !_isBusy;
        ChooseModelButton.IsEnabled = !_isBusy && hasUsableKey;
        TranslateButton.IsEnabled = !_isBusy && hasFile && hasUsableKey && hasModel;
        ExportButton.IsEnabled = !_isBusy && hasFile;
        ExportCsvButton.IsEnabled = !_isBusy && hasFile;
    }

    void SetProgress(double fraction)
    {
        var clamped = Math.Clamp(fraction, 0d, 1d);
        ProgressTrack.ColumnDefinitions[0].Width = new GridLength(clamped, GridUnitType.Star);
        ProgressTrack.ColumnDefinitions[1].Width = new GridLength(1 - clamped, GridUnitType.Star);
    }

    void UpdateProgress(double completedSteps, double totalSteps, string message)
    {
        SetProgress(totalSteps <= 0 ? 0 : completedSteps / totalSteps);
        ProgressLabel.Text = $"{message}  ·  {completedSteps:N0} of {totalSteps:N0} entries";
    }

    void ClearStatus()
    {
        StatusBlock.IsVisible = false;
        ProgressGroup.IsVisible = false;
        ResultGroup.IsVisible = false;
    }

    void ShowUsage(string summary)
    {
        UsageLabel.Text = summary;
        UsageLabel.IsVisible = true;
        UsageSeparator.IsVisible = true;
    }

    void ShowSuccess(string title, string? detail, IReadOnlyList<string> files) =>
        ShowResult("checkmark.circle.fill", "SuccessLight", "SuccessDark", title, detail, files);

    void ShowError(string message) =>
        ShowResult("exclamationmark.triangle.fill", "DangerLight", "DangerDark", "Something went wrong", message, []);

    void ShowResult(
        string symbol,
        string lightColorKey,
        string darkColorKey,
        string title,
        string? detail,
        IReadOnlyList<string> files)
    {
        ProgressGroup.IsVisible = false;
        StatusBlock.IsVisible = true;
        ResultGroup.IsVisible = true;

        ResultIcon.Symbol = symbol;
        ResultIcon.SetAppTheme(
            SymbolImage.TintProperty,
            Resource<Color>(lightColorKey),
            Resource<Color>(darkColorKey));

        ResultTitleLabel.Text = title;
        ResultDetailLabel.Text = detail ?? string.Empty;
        ResultDetailLabel.IsVisible = detail is not null;

        ResultFileList.Clear();
        ResultFileList.IsVisible = files.Count > 0;

        foreach (var file in files)
        {
            ResultFileList.Add(BuildFileLink(file));
        }
    }

    /// <summary>A written file, tappable to reveal in Finder.</summary>
    Label BuildFileLink(string path)
    {
        var label = new Label
        {
            Text = Abbreviate(path),
            FontSize = 11,
            LineBreakMode = LineBreakMode.MiddleTruncation,
            TextDecorations = TextDecorations.Underline
        };

        label.SetAppTheme(Label.TextColorProperty, Resource<Color>("AccentTextLight"), Resource<Color>("AccentTextDark"));

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => RevealInFileManager(path);
        label.GestureRecognizers.Add(tap);

        return label;
    }

    static void RevealInFileManager(string path)
    {
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));

            if (string.IsNullOrEmpty(directory))
            {
                return;
            }

            _ = Launcher.Default.TryOpenAsync(new Uri($"file://{directory}"));
        }
        catch (Exception)
        {
            // Revealing the output is a convenience; failing to is not an error
            // worth replacing the success state the user just earned.
        }
    }

    static T Resource<T>(string key) =>
        Application.Current?.Resources[key] is T value ? value : default!;

    // ----------------------------------------------------------------- Helpers

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
}

using ResXTranslator.Controls;
using System.Diagnostics;

namespace ResXTranslator;

public partial class MainPage : ContentPage
{
    const int BatchSize = 40;
    const int MaxBatchCharacters = 10_000;

    readonly LlmClient _llmClient;
    readonly LlmProviderRegistry _providerRegistry;
    string? _selectedFilePath;
    PickedFolder? _pickedFolder;
    IReadOnlyList<string> _folderResxFiles = [];
    TranslationSpreadsheetDocument? _translationDocument;
    TargetLanguageOption? _selectedLanguage;
    LlmProviderDescriptor _provider;
    Uri _endpoint;
    string? _apiKey;
    IReadOnlyList<LlmModel> _models = [];
    LlmModel? _selectedModel;
    string _domainInstructions = LlmSettings.DefaultDomainInstructions;
    int _providerConcurrency;
    LlmConnectionState _connectionState = LlmConnectionState.NotConnected;
    LlmTokenUsage _translationUsage;
    bool _catalogLoadedSuccessfully;
    bool _selectedModelUnavailable;
    bool _initialized;
    bool _isBusy;
    CancellationTokenSource? _translationCancellation;
    readonly Dictionary<string, ActiveRequestStatus> _activeRequests = [];
    TranslationProgress? _activeTranslationProgress;
    int _activeConcurrency;
    IDispatcherTimer? _progressTimer;
    readonly Stopwatch _progressStopwatch = new();
    string _progressMessage = "Preparing source";
    string _progressDetail = "Inspecting the selected input";

    internal MainPage(LlmClient llmClient, LlmProviderRegistry providerRegistry)
    {
        _llmClient = llmClient;
        _providerRegistry = providerRegistry;
        var providerId = LlmSettings.ActiveProvider;
        _provider = providerRegistry.Get(providerId).Descriptor;
        if (!_provider.IsAvailable)
        {
            _provider = providerRegistry.Get(LlmProviderId.OpenRouter).Descriptor;
            LlmSettings.ActiveProvider = _provider.Id;
        }

        _endpoint = LoadEndpoint(_provider);
        InitializeComponent();
        RestoreProviderSettings();
        RenderLanguageState();
        RenderSourceRow();
        RenderProviderState();
        RenderTranslationSettings();
        UpdateActionState();

        Loaded += OnLoaded;
    }

    public Dictionary<string, string> ReadResXFile(string path) => new ResXParser().ReadResXFile(path);

    public void WriteResXFile(string path, Dictionary<string, string> values) =>
        new ResXParser().WriteResXFile(path, values);

    // ------------------------------------------------------------ LLM provider

    async void OnLoaded(object? sender, EventArgs e)
    {
        WindowGeometry.ApplyOnce(Window);

        if (_initialized)
        {
            return;
        }

        _initialized = true;
        await InitializeProviderAsync();
    }

    void RestoreProviderSettings()
    {
        _endpoint = LoadEndpoint(_provider);
        var savedId = LlmSettings.LoadModelId(_provider.Id);
        if (!string.IsNullOrWhiteSpace(savedId))
        {
            _selectedModel = new LlmModel(savedId, savedId, null, null) { Provider = _provider.Name };
        }
        else
        {
            _selectedModel = null;
        }

        _domainInstructions = LlmSettings.LoadDomainInstructions();
        _providerConcurrency = LlmSettings.LoadConcurrency(_provider);
        _selectedModelUnavailable = false;
        _catalogLoadedSuccessfully = false;
    }

    static Uri LoadEndpoint(LlmProviderDescriptor provider)
    {
        var saved = LlmSettings.LoadEndpoint(provider);
        return LlmProviderRegistry.TryCreateEndpoint(provider, saved, out var endpoint, out _)
            ? endpoint!
            : new Uri(provider.DefaultEndpoint);
    }

    LlmConnectionProfile CurrentProfile() =>
        new(_provider.Id, _endpoint, _apiKey, _providerConcurrency);

    bool HasConnectionConfiguration =>
        (!_provider.RequiresApiKey || !string.IsNullOrWhiteSpace(_apiKey)) &&
        _connectionState is LlmConnectionState.Connected or LlmConnectionState.Unverified;

    bool IsSelectedModelVerified => _selectedModel is not null &&
        LlmSettings.IsModelCompatibilityVerified(_provider.Id, _endpoint, _selectedModel.Id);

    async Task InitializeProviderAsync()
    {
        try
        {
            _apiKey = await LlmCredentialStore.GetAsync(_provider.Id);
        }
        catch (Exception ex)
        {
            _connectionState = LlmConnectionState.NeedsAttention;
            RenderProviderState();
            UpdateActionState();
            ShowError($"The saved {_provider.Name} credential could not be read from secure storage: {ex.Message}");
            return;
        }

        if (_provider.RequiresApiKey && string.IsNullOrWhiteSpace(_apiKey))
        {
            _connectionState = LlmConnectionState.NotConnected;
            RenderProviderState();
            UpdateActionState();
            return;
        }

        _connectionState = LlmConnectionState.Checking;
        RenderProviderState();
        UpdateActionState();

        try
        {
            await _llmClient.ValidateConnectionAsync(CurrentProfile());
        }
        catch (LlmApiException ex) when (ex.StatusCode is
            System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
        {
            _connectionState = LlmConnectionState.NeedsAttention;
            RenderProviderState();
            UpdateActionState();
            return;
        }
        catch (Exception)
        {
            // A network or service failure does not prove a stored credential is
            // invalid. Keep it usable so the translation request can try later.
            _connectionState = LlmConnectionState.Unverified;
            RenderProviderState();
            UpdateActionState();
            return;
        }

        _connectionState = LlmConnectionState.Connected;

        try
        {
            await RefreshModelsAsync();
        }
        catch (Exception)
        {
            // Catalog availability is independent of credential validity. Keep
            // the connected state and let the model sheet offer refresh/retry.
        }

        RenderProviderState();
        UpdateActionState();
    }

    async Task RefreshModelsAsync()
    {
        if (_provider.RequiresApiKey && string.IsNullOrWhiteSpace(_apiKey))
        {
            return;
        }

        try
        {
            _models = await _llmClient.GetModelsAsync(CurrentProfile());
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
        var savedId = LlmSettings.LoadModelId(_provider.Id);

        if (string.IsNullOrWhiteSpace(savedId))
        {
            _selectedModel = null;
            _selectedModelUnavailable = false;
            return;
        }

        _selectedModel = _models.FirstOrDefault(model => model.Id == savedId)
            ?? new LlmModel(savedId, savedId, null, null) { Provider = _provider.Name };
        _selectedModelUnavailable = _catalogLoadedSuccessfully &&
            !_models.Any(model => model.Id == savedId) &&
            !LlmSettings.IsModelCompatibilityVerified(_provider.Id, _endpoint, savedId);
    }

    async void OnChooseProviderClicked(object? sender, EventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        var page = new LlmProviderPage(_providerRegistry.Providers, _provider.Id);
        await Navigation.PushModalAsync(page);
        var providerId = await page.Completion;
        if (providerId is null || providerId == _provider.Id)
        {
            return;
        }

        _provider = _providerRegistry.Get(providerId.Value).Descriptor;
        LlmSettings.ActiveProvider = providerId.Value;
        _apiKey = null;
        _models = [];
        _connectionState = LlmConnectionState.NotConnected;
        RestoreProviderSettings();
        RenderTranslationSettings();
        RenderProviderState();
        UpdateActionState();
        await InitializeProviderAsync();
    }

    async void OnManageAccountClicked(object? sender, EventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        var page = new LlmConnectionPage(
            _llmClient,
            _provider,
            _endpoint,
            _apiKey,
            _providerConcurrency,
            _connectionState);
        await Navigation.PushModalAsync(page);
        var result = await page.Completion;

        switch (result.Outcome)
        {
            case LlmConnectionOutcome.Connected:
                _endpoint = result.Endpoint ?? _endpoint;
                _apiKey = result.ApiKey;
                _connectionState = LlmConnectionState.Connected;

                try
                {
                    await RefreshModelsAsync();
                }
                catch (Exception ex)
                {
                    ShowError($"Connected, but the model catalog could not be loaded: {ex.Message}");
                }

                break;
            case LlmConnectionOutcome.Removed:
                _apiKey = null;
                _endpoint = LoadEndpoint(_provider);
                _models = [];
                _catalogLoadedSuccessfully = false;
                _selectedModelUnavailable = false;
                _connectionState = LlmConnectionState.NotConnected;
                break;
        }

        RenderProviderState();
        UpdateActionState();
    }

    async void OnChooseModelClicked(object? sender, EventArgs e)
    {
        if (_isBusy || !HasConnectionConfiguration)
        {
            return;
        }

        var page = new LlmModelPage(
            _llmClient,
            _provider,
            CurrentProfile(),
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
        LlmSettings.SaveModelId(_provider.Id, model.Id);
        LlmSettings.SaveModelCompatibility(_provider.Id, _endpoint, model.Id);
        RenderProviderState();
        UpdateActionState();
    }

    async void OnEditSettingsClicked(object? sender, EventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        var generatorModel = HasConnectionConfiguration && !_selectedModelUnavailable && IsSelectedModelVerified
            ? _selectedModel
            : null;
        var page = new TranslationSettingsPage(
            _llmClient,
            _provider,
            _domainInstructions,
            _providerConcurrency,
            generatorModel is null ? null : CurrentProfile(),
            generatorModel);
        await Navigation.PushModalAsync(page);
        var result = await page.Completion;

        if (result is null)
        {
            return;
        }

        LlmSettings.SaveDomainInstructions(result.DomainInstructions);
        LlmSettings.SaveConcurrency(_provider, result.Concurrency);
        _domainInstructions = LlmSettings.LoadDomainInstructions();
        _providerConcurrency = LlmSettings.LoadConcurrency(_provider);
        RenderTranslationSettings();
    }

    void RenderTranslationSettings()
    {
        var domain = string.Equals(
            _domainInstructions,
            LlmSettings.DefaultDomainInstructions,
            StringComparison.Ordinal)
                ? "Default domain"
                : "Custom domain";
        SettingsSummaryLabel.Text = $"{domain} · {_providerConcurrency:N0} max";
        SemanticProperties.SetDescription(
            SettingsSummaryLabel,
            $"{domain}. Maximum {_providerConcurrency:N0} concurrent {_provider.Name} requests.");
    }

    void RenderProviderState()
    {
        var (symbol, lightColor, darkColor, status) = _connectionState switch
        {
            LlmConnectionState.Checking =>
                ("arrow.triangle.2.circlepath", "SecondaryLabelLight", "SecondaryLabelDark", "Checking…"),
            LlmConnectionState.Connected =>
                ("checkmark.circle.fill", "SuccessLight", "SuccessDark", "Connected"),
            LlmConnectionState.Unverified =>
                ("wifi.exclamationmark", "WarningLight", "WarningDark", "Couldn't verify"),
            LlmConnectionState.NeedsAttention =>
                ("exclamationmark.triangle.fill", "DangerLight", "DangerDark", "Needs attention"),
            _ => ("key.fill", "SecondaryLabelLight", "SecondaryLabelDark", "Not connected")
        };

        AccountIcon.Symbol = symbol;
        AccountIcon.SetAppTheme(
            SymbolImage.TintProperty,
            Resource<Color>(lightColor),
            Resource<Color>(darkColor));
        AccountStatusLabel.Text = status;
        ProviderNameLabel.Text = _provider.Name;

        if (!HasConnectionConfiguration)
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
                : IsSelectedModelVerified
                    ? _selectedModel.Name
                    : "Model test required";
            ModelIdLabel.Text = IsSelectedModelVerified
                ? _selectedModel.Id
                : $"{_selectedModel.Id} · strict schema not verified";
            ModelIdLabel.IsVisible = true;
        }
    }

    // -------------------------------------------------------------- Languages

    void RenderLanguageState()
    {
        if (_selectedLanguage is null)
        {
            LanguageNameLabel.Text = "No language selected";
            LanguageCodeLabel.IsVisible = false;
            return;
        }

        var alreadyPresent = _translationDocument?.HasLanguage(_selectedLanguage.ColumnHeader) == true;
        LanguageNameLabel.Text = alreadyPresent
            ? "Already in spreadsheet"
            : _selectedLanguage.DisplayName;
        LanguageCodeLabel.Text = _selectedLanguage.ColumnHeader;
        LanguageCodeLabel.IsVisible = true;
    }

    async void OnChooseLanguageClicked(object? sender, EventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        var page = new LanguageSelectionPage(
            _selectedLanguage?.ColumnHeader,
            _translationDocument?.LanguageHeaders);
        await Navigation.PushModalAsync(page);
        var language = await page.Completion;

        if (language is null)
        {
            return;
        }

        _selectedLanguage = language;
        RenderLanguageState();
        UpdateActionState();
    }

    TargetLanguageOption GetSelectedLanguage() => _selectedLanguage
        ?? throw new InvalidOperationException("Choose a target language first.");

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

    async void OnFolderPickerButtonClicked(object? sender, EventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        PickedFolder? pickedFolder = null;

        try
        {
            pickedFolder = await FolderPickerService.PickAsync();

            if (pickedFolder is null)
            {
                return;
            }

            var files = await Task.Run(() => Directory
                .EnumerateFiles(pickedFolder.Path, "*.resx", SearchOption.AllDirectories)
                .Where(path => !IsLocalizedResXOutput(path))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray());

            _translationDocument?.Dispose();
            _translationDocument = null;
            _selectedFilePath = null;
            _pickedFolder?.Dispose();
            _pickedFolder = pickedFolder;
            pickedFolder = null;
            _folderResxFiles = files;

            RenderSourceRow();
            RenderLanguageState();
            UpdateActionState();
            ClearStatus();

            if (files.Length == 0)
            {
                ShowError("This folder does not contain any source RESX files.");
            }
        }
        catch (Exception ex)
        {
            ShowError($"Could not open the selected folder: {ex.Message}");
        }
        finally
        {
            pickedFolder?.Dispose();
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
        _pickedFolder?.Dispose();
        _pickedFolder = null;
        _folderResxFiles = [];
        _selectedFilePath = fullPath;

        RenderSourceRow();
        RenderLanguageState();
        UpdateActionState();
        ClearStatus();
    }

    void RenderSourceRow()
    {
        if (_pickedFolder is not null)
        {
            SourceIcon.Symbol = "folder.fill";
            SourceTitleLabel.Text = Path.GetFileName(
                _pickedFolder.Path.TrimEnd(Path.DirectorySeparatorChar));
            SourceSubtitleLabel.IsVisible = false;
            SourceSeparator.IsVisible = true;
            SourceDetailLabel.Text = _folderResxFiles.Count == 0
                ? "No source RESX files found"
                : $"{_folderResxFiles.Count:N0} RESX {(_folderResxFiles.Count == 1 ? "file" : "files")}  ·  scans subfolders  ·  saves beside each source";
            SourceDetailLabel.IsVisible = true;
            PickFileButton.Text = "Choose File…";
            PickFolderButton.Text = "Change Folder…";
            return;
        }

        if (_selectedFilePath is null)
        {
            SourceIcon.Symbol = "doc.badge.plus";
            SourceTitleLabel.Text = "No file selected";
            SourceSubtitleLabel.Text = "RESX, Excel or CSV — or drag one here";
            SourceSubtitleLabel.IsVisible = true;
            SourceSeparator.IsVisible = false;
            SourceDetailLabel.IsVisible = false;
            PickFileButton.Text = "Choose File…";
            PickFolderButton.Text = "Choose Folder…";
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
        PickFileButton.Text = "Change File…";
        PickFolderButton.Text = "Choose Folder…";
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

#if MACCATALYST
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
        _translationCancellation?.Dispose();
        _translationCancellation = new CancellationTokenSource();
        var cancellationToken = _translationCancellation.Token;
        var runId = Guid.NewGuid().ToString("N")[..8];
        SetBusy(true);
        AppDiagnostics.Write("Translation", $"Run {runId} started");

        try
        {
            if (!HasConnectionConfiguration)
            {
                throw new InvalidOperationException($"Connect {_provider.Name} first.");
            }

            var profile = CurrentProfile();
            var model = _selectedModel ?? throw new InvalidOperationException($"Choose a {_provider.Name} model first.");

            if (_selectedModelUnavailable)
            {
                throw new InvalidOperationException($"The selected {_provider.Name} model is unavailable. Choose another model.");
            }

            if (!IsSelectedModelVerified)
            {
                throw new InvalidOperationException("Test the selected model for strict JSON Schema compatibility before translating.");
            }

            var runSettings = new LlmTranslationExecutionSettings(
                _domainInstructions,
                LlmSettings.GetParallelRequestLimit(_provider, model, _providerConcurrency));
            _translationUsage = default;

            if (_pickedFolder is not null)
            {
                await TranslateResXFolderAsync(profile, model, runSettings, cancellationToken);
            }
            else if (_selectedFilePath is { } path && IsResX(path))
            {
                await TranslateResXAsync(profile, model, runSettings, path, cancellationToken);
            }
            else if (_selectedFilePath is { } spreadsheetPath)
            {
                await TranslateSpreadsheetAsync(profile, model, runSettings, spreadsheetPath, cancellationToken);
            }
            else
            {
                throw new InvalidOperationException("Please select a file or folder first.");
            }

            AppDiagnostics.Write(
                "Translation",
                $"Run {runId} completed | elapsed={FormatRunDuration(_progressStopwatch.Elapsed)}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            AppDiagnostics.Write(
                "Translation",
                $"Run {runId} cancelled by user | elapsed={FormatRunDuration(_progressStopwatch.Elapsed)}");
            ShowResult(
                "stop.circle.fill",
                "WarningLight",
                "WarningDark",
                "Translation cancelled",
                "The current batch was discarded. Files completed earlier in a folder run remain saved." +
                Environment.NewLine + $"Stopped after {FormatRunDuration(_progressStopwatch.Elapsed)}.",
                []);
        }
        catch (LlmApiException ex) when (ex.StatusCode is
            System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
        {
            AppDiagnostics.WriteException(
                "Translation",
                $"Run {runId} failed after {FormatRunDuration(_progressStopwatch.Elapsed)}",
                ex);
            _connectionState = LlmConnectionState.NeedsAttention;
            RenderProviderState();
            ShowTranslationError(ex.Message);
        }
        catch (LlmApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            AppDiagnostics.WriteException(
                "Translation",
                $"Run {runId} failed after {FormatRunDuration(_progressStopwatch.Elapsed)}",
                ex);

            try
            {
                await RefreshModelsAsync();
            }
            catch (Exception catalogException)
            {
                AppDiagnostics.WriteException(
                    _provider.Name,
                    "Could not refresh the model catalog after a model or endpoint 404",
                    catalogException);
            }

            RenderProviderState();
            ShowTranslationError(_selectedModelUnavailable
                ? $"The selected {_provider.Name} model is no longer available. Choose another model."
                : ex.Message);
        }
        catch (Exception ex)
        {
            AppDiagnostics.WriteException(
                "Translation",
                $"Run {runId} failed after {FormatRunDuration(_progressStopwatch.Elapsed)}",
                ex);
            ShowTranslationError(ex.Message);
        }
        finally
        {
            _translationCancellation.Dispose();
            _translationCancellation = null;
            SetBusy(false);
        }
    }

    void OnCancelTranslationClicked(object? sender, EventArgs e)
    {
        if (_translationCancellation is not { IsCancellationRequested: false } cancellation)
        {
            return;
        }

        CancelTranslationButton.IsEnabled = false;
        SetProgressState(
            _progressMessage,
            $"Cancelling the active {_provider.Name} request…");
        AppDiagnostics.Write("Translation", "Cancellation requested by user");
        cancellation.Cancel();
    }

    async void OnRevealLogClicked(object? sender, EventArgs e)
    {
        AppDiagnostics.EnsureLogExists();

        try
        {
            await Launcher.Default.OpenAsync(new OpenFileRequest(
                "ResXTranslator diagnostics",
                new ReadOnlyFile(AppDiagnostics.LogPath)));
        }
        catch (Exception ex)
        {
            AppDiagnostics.WriteException("Diagnostics", "Could not open log", ex);

            if (_isBusy)
            {
                SetProgressState(
                    _progressMessage,
                    $"The diagnostics log could not be opened. It is stored at {AppDiagnostics.LogPath}");
            }
            else
            {
                ShowError($"The diagnostics log could not be opened. It is stored at {AppDiagnostics.LogPath}");
            }
        }
    }

    async Task TranslateResXAsync(
        LlmConnectionProfile profile,
        LlmModel model,
        LlmTranslationExecutionSettings runSettings,
        string path,
        CancellationToken cancellationToken)
    {
        var values = ReadResXFile(path);

        if (values.Count == 0)
        {
            throw new InvalidOperationException("The selected RESX file does not contain any string entries.");
        }

        var workItem = new ResXWorkItem(path, values);
        var progress = new TranslationProgress(values.Count);
        var writtenFiles = await TranslateResXWorkItemsAsync(
            profile,
            model,
            runSettings,
            [workItem],
            GetSelectedLanguage(),
            progress,
            cancellationToken);

        ShowUsage(BuildUsageSummary(model, runSettings.ParallelRequestLimit));
        ShowSuccess(
            "Translated into 1 language",
            $"Completed in {FormatRunDuration(_progressStopwatch.Elapsed)}.",
            writtenFiles);
    }

    async Task TranslateResXFolderAsync(
        LlmConnectionProfile profile,
        LlmModel model,
        LlmTranslationExecutionSettings runSettings,
        CancellationToken cancellationToken)
    {
        if (_folderResxFiles.Count == 0)
        {
            throw new InvalidOperationException("The selected folder does not contain any source RESX files.");
        }

        SetProgressState("Reading RESX files", "Discovering string entries before translation");
        var workItems = await Task.Run(() => _folderResxFiles
            .Select(path => new ResXWorkItem(path, ReadResXFile(path)))
            .ToArray(), cancellationToken);
        var translatableItems = workItems.Where(item => item.Values.Count > 0).ToArray();

        if (translatableItems.Length == 0)
        {
            throw new InvalidOperationException("The selected folder's RESX files do not contain any string entries.");
        }

        var totalEntries = translatableItems.Sum(item => item.Values.Count);
        var progress = new TranslationProgress(totalEntries);
        var writtenFiles = await TranslateResXWorkItemsAsync(
            profile,
            model,
            runSettings,
            translatableItems,
            GetSelectedLanguage(),
            progress,
            cancellationToken);

        var skipped = workItems.Length - translatableItems.Length;
        ShowUsage(BuildUsageSummary(model, runSettings.ParallelRequestLimit));
        ShowSuccess(
            $"Translated {translatableItems.Length:N0} RESX {(translatableItems.Length == 1 ? "file" : "files")}",
            skipped == 0
                ? $"Created {writtenFiles.Count:N0} localized {(writtenFiles.Count == 1 ? "file" : "files")} beside the sources." +
                    Environment.NewLine + $"Completed in {FormatRunDuration(_progressStopwatch.Elapsed)}."
                : $"Created {writtenFiles.Count:N0} localized files; skipped {skipped:N0} empty RESX files." +
                    Environment.NewLine + $"Completed in {FormatRunDuration(_progressStopwatch.Elapsed)}.",
            writtenFiles);
    }

    async Task<IReadOnlyList<string>> TranslateResXWorkItemsAsync(
        LlmConnectionProfile profile,
        LlmModel model,
        LlmTranslationExecutionSettings runSettings,
        IReadOnlyList<ResXWorkItem> items,
        TargetLanguageOption targetLanguage,
        TranslationProgress progress,
        CancellationToken cancellationToken)
    {
        var plans = items
            .Select((item, index) => CreateResXTranslationPlan(item, index + 1, items.Count))
            .ToArray();
        var workQueue = new List<TranslationBatchWork>();
        var largestBatchCount = plans.Max(plan => plan.Batches.Count);

        // Round-robin by batch number gives every file an early slot while still
        // allowing one large RESX to occupy spare workers when the folder is small.
        for (var batchIndex = 0; batchIndex < largestBatchCount; batchIndex++)
        {
            foreach (var plan in plans.Where(plan => batchIndex < plan.Batches.Count))
            {
                var resultIndex = batchIndex;
                workQueue.Add(new TranslationBatchWork(
                    plan.DisplayName,
                    plan.FileIndex,
                    plan.FileCount,
                    batchIndex + 1,
                    plan.Batches.Count,
                    plan.Batches[batchIndex],
                    result => plan.Results[resultIndex] = result));
            }
        }

        await TranslateBatchQueueAsync(
            profile,
            model,
            runSettings,
            targetLanguage,
            workQueue,
            progress,
            cancellationToken);
        EndParallelProgress();

        var writtenFiles = new List<string>(plans.Length);

        foreach (var plan in plans)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var translatedValues = new Dictionary<string, string>(plan.Keys.Length);

            foreach (var result in plan.Results)
            {
                foreach (var translation in result!.Translations)
                {
                    translatedValues[plan.Keys[translation.Key]] = translation.Value;
                }
            }

            SetProgressState(
                $"Saving {targetLanguage.ColumnHeader} RESX",
                $"Writing {plan.DisplayName} after every batch validated");
            var outputPath = Path.Combine(
                plan.OutputDirectory,
                $"{Path.GetFileNameWithoutExtension(plan.Item.Path)}.{targetLanguage.ColumnHeader}.resx");
            WriteResXFile(outputPath, translatedValues);
            AppDiagnostics.Write(
                "Translation",
                $"Saved localized RESX | file={Path.GetFileName(outputPath)} | entries={translatedValues.Count}");
            writtenFiles.Add(outputPath);
        }

        return writtenFiles;
    }

    async Task TranslateSpreadsheetAsync(
        LlmConnectionProfile profile,
        LlmModel model,
        LlmTranslationExecutionSettings runSettings,
        string path,
        CancellationToken cancellationToken)
    {
        var document = _translationDocument
            ?? throw new InvalidOperationException("Please select the spreadsheet again.");

        var selectedLanguage = GetSelectedLanguage();

        if (document.HasLanguage(selectedLanguage.ColumnHeader))
        {
            throw new InvalidOperationException(
                $"The document already contains a '{selectedLanguage.ColumnHeader}' translation column.");
        }

        var sourceRows = document.GetSourceRows();

        if (sourceRows.Count == 0)
        {
            throw new InvalidOperationException("The document has no non-empty values in 'Default' or 'en-US'.");
        }

        var translatedValues = new Dictionary<int, string>(sourceRows.Count);
        var inputs = sourceRows
            .Select(row => new LlmTranslationInput(row.RowNumber, row.Text))
            .ToArray();
        var batches = CreateTranslationBatches(inputs, input => input.Text);
        var results = new LlmTranslationBatch?[batches.Count];
        var workQueue = new List<TranslationBatchWork>(batches.Count);

        for (var batchIndex = 0; batchIndex < batches.Count; batchIndex++)
        {
            var resultIndex = batchIndex;
            workQueue.Add(new TranslationBatchWork(
                Path.GetFileName(path),
                1,
                1,
                batchIndex + 1,
                batches.Count,
                batches[batchIndex],
                result => results[resultIndex] = result));
        }

        var progress = new TranslationProgress(sourceRows.Count);
        await TranslateBatchQueueAsync(
            profile,
            model,
            runSettings,
            selectedLanguage,
            workQueue,
            progress,
            cancellationToken);
        EndParallelProgress();

        foreach (var result in results)
        {
            foreach (var translation in result!.Translations)
            {
                translatedValues[translation.Key] = translation.Value;
            }
        }

        document.AddLanguage(selectedLanguage.ColumnHeader, translatedValues);

        var extension = Path.GetExtension(path).ToLowerInvariant();
        var outputPath = GetTabularOutputPath(path, extension, translated: true);

        SetProgressState("Saving translated spreadsheet", $"Writing {Path.GetFileName(outputPath)}");
        await Task.Run(() => SaveDocument(document, outputPath, extension));
        AppDiagnostics.Write(
            "Translation",
            $"Saved translated spreadsheet | file={Path.GetFileName(outputPath)} | rows={sourceRows.Count}");

        ShowUsage(BuildUsageSummary(model, runSettings.ParallelRequestLimit));
        RenderLanguageState();
        ShowSuccess(
            $"Added {selectedLanguage.ColumnHeader}",
            $"Completed in {FormatRunDuration(_progressStopwatch.Elapsed)}.",
            [outputPath]);
    }

    ResXTranslationPlan CreateResXTranslationPlan(ResXWorkItem item, int fileIndex, int fileCount)
    {
        var keys = item.Values.Keys.ToArray();
        var inputs = item.Values.Values
            .Select((text, id) => new LlmTranslationInput(id, text))
            .ToArray();
        var batches = CreateTranslationBatches(inputs, input => input.Text);
        var outputDirectory = fileCount == 1
            ? GetOutputDirectory(item.Path)
            : Path.GetDirectoryName(item.Path)
                ?? throw new InvalidOperationException($"Could not locate the folder for {Path.GetFileName(item.Path)}.");

        return new ResXTranslationPlan(
            item,
            keys,
            batches,
            outputDirectory,
            fileIndex,
            fileCount);
    }

    async Task TranslateBatchQueueAsync(
        LlmConnectionProfile profile,
        LlmModel model,
        LlmTranslationExecutionSettings runSettings,
        TargetLanguageOption targetLanguage,
        IReadOnlyList<TranslationBatchWork> workQueue,
        TranslationProgress progress,
        CancellationToken cancellationToken)
    {
        if (workQueue.Count == 0)
        {
            return;
        }

        var concurrency = runSettings.ParallelRequestLimit;
        var workerCount = Math.Min(concurrency, workQueue.Count);
        var nextWorkIndex = -1;
        _activeConcurrency = concurrency;
        _activeTranslationProgress = progress;
        progress.TotalBatches = workQueue.Count;
        RenderParallelProgress();
        AppDiagnostics.Write(
            "Translation",
            $"Queued {workQueue.Count} batches | model={model.Id} | pricing={GetPricingClass(model)} | maxConcurrency={concurrency}");

        using var failFastCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        async Task RunWorkerAsync()
        {
            try
            {
                while (true)
                {
                    failFastCancellation.Token.ThrowIfCancellationRequested();
                    var workIndex = Interlocked.Increment(ref nextWorkIndex);

                    if (workIndex >= workQueue.Count)
                    {
                        return;
                    }

                    var work = workQueue[workIndex];
                    var batchCharacters = work.Inputs.Sum(input => input.Text.Length);
                    AppDiagnostics.Write(
                        "Translation",
                        $"Starting queue item {workIndex + 1}/{workQueue.Count} | file={work.DisplayName} | fileIndex={work.FileIndex}/{work.FileCount} | language={targetLanguage.ColumnHeader} | batch={work.BatchNumber}/{work.BatchCount} | entries={work.Inputs.Length} | sourceChars={batchCharacters}");

                    var requestProgress = CreateRequestProgress(work);
                    var result = await _llmClient.TranslateAsync(
                        profile,
                        model,
                        targetLanguage.ModelTarget,
                        runSettings.DomainInstructions,
                        work.Inputs,
                        requestProgress,
                        failFastCancellation.Token);

                    work.StoreResult(result);
                    _translationUsage += result.Usage;
                    progress.Completed += work.Inputs.Length;
                    progress.CompletedBatches++;
                    RenderParallelProgress();
                }
            }
            catch
            {
                // One malformed, rejected, or timed-out batch invalidates the
                // current output. Stop queued and in-flight siblings immediately;
                // completed responses remain in memory and are never written.
                await failFastCancellation.CancelAsync();
                throw;
            }
        }

        var workers = Enumerable.Range(0, workerCount)
            .Select(_ => RunWorkerAsync())
            .ToArray();

        try
        {
            await Task.WhenAll(workers);
            AppDiagnostics.Write(
                "Translation",
                $"Parallel queue completed | batches={progress.CompletedBatches}/{progress.TotalBatches} | entries={progress.Completed:N0}/{progress.Total:N0}");
        }
        catch
        {
            AppDiagnostics.Write(
                "Translation",
                $"Parallel queue stopped | batches={progress.CompletedBatches}/{progress.TotalBatches} | entries={progress.Completed:N0}/{progress.Total:N0}");

            var rootFailure = workers
                .Where(worker => worker.Exception is not null)
                .SelectMany(worker => worker.Exception!.Flatten().InnerExceptions)
                .FirstOrDefault(exception => exception is not OperationCanceledException);

            if (rootFailure is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(rootFailure).Throw();
            }

            throw;
        }
    }

    void EndParallelProgress()
    {
        _activeRequests.Clear();
        _activeTranslationProgress = null;
        _activeConcurrency = 0;
    }

    static string GetPricingClass(LlmModel model) => model switch
    {
        { IsDefinitelyPaid: true } => "paid",
        { IsDefinitelyFree: true } => "free",
        _ => "unknown-conservative"
    };

    string BuildUsageSummary(LlmModel model, int parallelRequestLimit)
    {
        var reasoning = _translationUsage.ReasoningTokens > 0
            ? $"  ·  {_translationUsage.ReasoningTokens:N0} reasoning excluded"
            : string.Empty;
        return $"{model.Name}  ·  {_translationUsage.PromptTokens:N0} input  ·  " +
            $"{_translationUsage.CompletionTokens:N0} output{reasoning}  ·  {_translationUsage.TotalTokens:N0} total tokens  ·  " +
            $"{parallelRequestLimit:N0} parallel";
    }

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
        PickFolderButton.IsEnabled = !busy;
        ManageAccountButton.IsEnabled = !busy;
        ChooseModelButton.IsEnabled = !busy && HasConnectionConfiguration;
        ChooseProviderButton.IsEnabled = !busy;
        ChooseLanguageButton.IsEnabled = !busy;
        EditSettingsButton.IsEnabled = !busy;
        ProgressGroup.IsVisible = busy;
        CancelTranslationButton.IsVisible = busy && _translationCancellation is not null;
        CancelTranslationButton.IsEnabled = busy && _translationCancellation is { IsCancellationRequested: false };

        if (busy)
        {
            _activeRequests.Clear();
            _activeTranslationProgress = null;
            _activeConcurrency = 0;
            ResultGroup.IsVisible = false;
            StatusBlock.IsVisible = true;
            SetProgress(0);
            _progressStopwatch.Restart();
            SetProgressState("Preparing source", "Inspecting the selected input");
            StartProgressFeedback();
            Dispatcher.Dispatch(async () => await MainScrollView.ScrollToAsync(
                ProgressGroup,
                ScrollToPosition.MakeVisible,
                true));
        }
        else
        {
            _progressStopwatch.Stop();
            StopProgressFeedback();
            _activeRequests.Clear();
            _activeTranslationProgress = null;
            _activeConcurrency = 0;
            CancelTranslationButton.IsVisible = false;
        }

        UpdateActionState();
    }

    /// <summary>
    /// Actions are enabled by what the app actually has, not merely by whether a
    /// job is running: Translate needs a file, usable account and available model.
    /// </summary>
    void UpdateActionState()
    {
        var hasSource = _selectedFilePath is not null || _folderResxFiles.Count > 0;
        var hasUsableConnection = HasConnectionConfiguration;
        var hasModel = _selectedModel is not null && !_selectedModelUnavailable;
        var hasLanguage = _selectedLanguage is not null &&
            _translationDocument?.HasLanguage(_selectedLanguage.ColumnHeader) != true;
        var hasSingleFile = _selectedFilePath is not null;

        ManageAccountButton.IsEnabled = !_isBusy;
        ChooseProviderButton.IsEnabled = !_isBusy;
        ChooseModelButton.IsEnabled = !_isBusy && hasUsableConnection;
        ChooseLanguageButton.IsEnabled = !_isBusy;
        EditSettingsButton.IsEnabled = !_isBusy;
        TranslateButton.IsEnabled = !_isBusy && hasSource && hasUsableConnection &&
            hasModel && IsSelectedModelVerified && hasLanguage;
        ExportButton.IsEnabled = !_isBusy && hasSingleFile;
        ExportCsvButton.IsEnabled = !_isBusy && hasSingleFile;
    }

    void SetProgress(double fraction)
    {
        var clamped = Math.Clamp(fraction, 0d, 1d);
        ProgressTrack.ColumnDefinitions[0].Width = new GridLength(clamped, GridUnitType.Star);
        ProgressTrack.ColumnDefinitions[1].Width = new GridLength(1 - clamped, GridUnitType.Star);
    }

    void SetProgressState(string message, string detail)
    {
        _progressMessage = message;
        _progressDetail = detail;
        RenderProgressMessage();
    }

    void RenderProgressMessage()
    {
        ProgressLabel.Text = _progressMessage;
        ElapsedTimeLabel.Text = $"Elapsed {FormatRunDuration(_progressStopwatch.Elapsed)}";
        SemanticProperties.SetDescription(
            ElapsedTimeLabel,
            $"Total operation elapsed time {FormatRunDuration(_progressStopwatch.Elapsed)}");
        ProgressDetailLabel.Text = _progressDetail;
        SemanticProperties.SetDescription(
            ProgressDetailLabel,
            $"{_progressMessage}. {ProgressDetailLabel.Text}");
    }

    IProgress<LlmTranslationProgress> CreateRequestProgress(TranslationBatchWork work) =>
        new Progress<LlmTranslationProgress>(request =>
        {
            if (request.Stage is LlmTranslationStage.Completed or LlmTranslationStage.Failed)
            {
                _activeRequests.Remove(request.RequestId);
            }
            else
            {
                var startedAt = _activeRequests.TryGetValue(request.RequestId, out var existing)
                    ? existing.StartedAt
                    : DateTimeOffset.UtcNow - request.Elapsed;
                _activeRequests[request.RequestId] = new ActiveRequestStatus(
                    request.RequestId,
                    work.DisplayContext,
                    work.BatchNumber,
                    work.BatchCount,
                    request.AttemptNumber,
                    request.MaximumAttempts,
                    GetRequestPhase(request),
                    startedAt);
            }

            RenderParallelProgress();
        });

    void RenderParallelProgress()
    {
        if (_activeTranslationProgress is not { } progress)
        {
            RenderProgressMessage();
            return;
        }

        SetProgress(progress.Total <= 0 ? 0 : progress.Completed / progress.Total);
        var activeCount = _activeRequests.Count;
        _progressMessage = activeCount switch
        {
            0 when progress.CompletedBatches >= progress.TotalBatches => "All batches validated",
            0 => $"Starting up to {_activeConcurrency:N0} parallel requests",
            1 => $"1 {_provider.Name} request active",
            _ => $"{activeCount:N0} {_provider.Name} requests active"
        };

        var batchPercent = progress.TotalBatches <= 0
            ? 0
            : Math.Clamp(
                (int)Math.Round(
                    progress.CompletedBatches * 100d / progress.TotalBatches,
                    MidpointRounding.AwayFromZero),
                0,
                100);
        var summary = $"{progress.Completed:N0} of {progress.Total:N0} entries complete · " +
            $"{progress.CompletedBatches:N0} of {progress.TotalBatches:N0} batches ({batchPercent:N0}%) · " +
            $"{_activeConcurrency:N0} max parallel";
        var orderedRequests = _activeRequests.Values
            .OrderBy(status => status.RequestId, StringComparer.Ordinal)
            .ToArray();
        var requestLines = orderedRequests
            .Take(4)
            .Select(status =>
                $"{status.RequestId} · {status.DisplayContext} · batch {status.BatchNumber:N0}/{status.BatchCount:N0} · " +
                (status.AttemptNumber > 1
                    ? $"retry {status.AttemptNumber:N0}/{status.MaximumAttempts:N0} · "
                    : string.Empty) +
                $"{status.Phase} · {FormatElapsed(DateTimeOffset.UtcNow - status.StartedAt)}")
            .ToList();

        if (orderedRequests.Length > requestLines.Count)
        {
            requestLines.Add($"+ {orderedRequests.Length - requestLines.Count:N0} more active requests");
        }

        _progressDetail = requestLines.Count == 0
            ? summary
            : summary + Environment.NewLine + string.Join(Environment.NewLine, requestLines);
        RenderProgressMessage();
    }

    static string GetRequestPhase(LlmTranslationProgress request) => request.Stage switch
    {
        LlmTranslationStage.Sending => "sending",
        LlmTranslationStage.WaitingForResponse => "awaiting provider",
        LlmTranslationStage.ProviderConnected => "awaiting first data",
        LlmTranslationStage.ReceivingResponse => $"receiving {request.ResponseCharacters:N0} chars",
        LlmTranslationStage.ValidatingResponse => $"validating {request.ResponseCharacters:N0} chars",
        LlmTranslationStage.Retrying => "selecting a different provider",
        _ => "working"
    };

    static string FormatElapsed(TimeSpan elapsed) => elapsed.TotalHours >= 1
        ? elapsed.ToString("h\\:mm\\:ss")
        : elapsed.ToString("m\\:ss");

    static string FormatRunDuration(TimeSpan elapsed) => elapsed.TotalHours >= 1
        ? $"{(int)elapsed.TotalHours:N0}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
        : $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";

    void StartProgressFeedback()
    {
        _progressTimer ??= Dispatcher.CreateTimer();
        _progressTimer.Interval = TimeSpan.FromSeconds(1);
        _progressTimer.Tick -= OnProgressTimerTick;
        _progressTimer.Tick += OnProgressTimerTick;
        _progressTimer.Start();

        ProgressPulse.TranslationX = -76;

        if (IsReduceMotionEnabled())
        {
            ProgressPulse.TranslationX = 0;
            ProgressPulse.Opacity = 0.25;
            return;
        }

        ProgressPulse.Opacity = 0.5;
        new Animation(
                value => ProgressPulse.TranslationX = -76 +
                    ((Math.Max(ProgressAnimationTrack.Width, 648) + 76) * value),
                0,
                1)
            .Commit(
                ProgressPulse,
                "ProgressPulse",
                16,
                1_150,
                Easing.SinInOut,
                repeat: () => _isBusy);
    }

    void StopProgressFeedback()
    {
        _progressTimer?.Stop();
        ProgressPulse.AbortAnimation("ProgressPulse");
        ProgressPulse.TranslationX = -76;
    }

    void OnProgressTimerTick(object? sender, EventArgs e)
    {
        if (_isBusy)
        {
            RenderParallelProgress();
        }
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
        ShowResult("exclamationmark.triangle.fill", "DangerLight", "DangerDark", "Couldn't complete that", message, []);

    void ShowTranslationError(string message) =>
        ShowError(message + Environment.NewLine + $"Stopped after {FormatRunDuration(_progressStopwatch.Elapsed)}.");

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

        Dispatcher.Dispatch(async () => await MainScrollView.ScrollToAsync(
            ResultGroup,
            ScrollToPosition.MakeVisible,
            true));
    }

    /// <summary>A written file, tappable to reveal in Finder.</summary>
    View BuildFileLink(string path)
    {
        var button = new ActionButton
        {
            Text = Abbreviate(path),
            FontSize = 11,
            LineBreakMode = LineBreakMode.MiddleTruncation,
            Prominence = ButtonProminence.Plain,
            HorizontalOptions = LayoutOptions.Start
        };

        button.SetAppTheme(
            ActionButton.AccentProperty,
            Resource<Color>("AccentTextLight"),
            Resource<Color>("AccentTextDark"));
        SemanticProperties.SetDescription(button, $"Reveal {Path.GetFileName(path)} in the file manager");
        button.Clicked += (_, _) => RevealInFileManager(path);

        return button;
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

    static bool IsReduceMotionEnabled()
    {
#if MACCATALYST
        return UIKit.UIAccessibility.IsReduceMotionEnabled;
#elif WINDOWS
        return !new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
#else
        return false;
#endif
    }

    static bool IsLocalizedResXOutput(string path)
    {
        var fileName = Path.GetFileName(path);
        return LanguageCatalog.All.Any(language => fileName.EndsWith(
            $".{language.ColumnHeader}.resx",
            StringComparison.OrdinalIgnoreCase));
    }

    static IReadOnlyList<T[]> CreateTranslationBatches<T>(
        IReadOnlyList<T> items,
        Func<T, string> getText)
    {
        var batches = new List<T[]>();
        var current = new List<T>(BatchSize);
        var currentCharacters = 0;

        foreach (var item in items)
        {
            var characterCount = getText(item).Length;

            if (current.Count > 0 &&
                (current.Count >= BatchSize || currentCharacters + characterCount > MaxBatchCharacters))
            {
                batches.Add(current.ToArray());
                current.Clear();
                currentCharacters = 0;
            }

            current.Add(item);
            currentCharacters += characterCount;
        }

        if (current.Count > 0)
        {
            batches.Add(current.ToArray());
        }

        return batches;
    }

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

    sealed record ResXWorkItem(string Path, Dictionary<string, string> Values);

    sealed class ResXTranslationPlan(
        ResXWorkItem item,
        string[] keys,
        IReadOnlyList<LlmTranslationInput[]> batches,
        string outputDirectory,
        int fileIndex,
        int fileCount)
    {
        public ResXWorkItem Item { get; } = item;

        public string[] Keys { get; } = keys;

        public IReadOnlyList<LlmTranslationInput[]> Batches { get; } = batches;

        public LlmTranslationBatch?[] Results { get; } = new LlmTranslationBatch?[batches.Count];

        public string OutputDirectory { get; } = outputDirectory;

        public string DisplayName { get; } = Path.GetFileName(item.Path);

        public int FileIndex { get; } = fileIndex;

        public int FileCount { get; } = fileCount;
    }

    sealed class TranslationBatchWork(
        string displayName,
        int fileIndex,
        int fileCount,
        int batchNumber,
        int batchCount,
        LlmTranslationInput[] inputs,
        Action<LlmTranslationBatch> storeResult)
    {
        public string DisplayName { get; } = displayName;

        public string DisplayContext { get; } = fileCount == 1
            ? CompactName(displayName)
            : $"{CompactName(displayName)} {fileIndex:N0}/{fileCount:N0}";

        public int FileIndex { get; } = fileIndex;

        public int FileCount { get; } = fileCount;

        public int BatchNumber { get; } = batchNumber;

        public int BatchCount { get; } = batchCount;

        public LlmTranslationInput[] Inputs { get; } = inputs;

        public Action<LlmTranslationBatch> StoreResult { get; } = storeResult;

        static string CompactName(string value) => value.Length <= 28
            ? value
            : $"{value[..14]}…{value[^11..]}";
    }

    sealed record ActiveRequestStatus(
        string RequestId,
        string DisplayContext,
        int BatchNumber,
        int BatchCount,
        int AttemptNumber,
        int MaximumAttempts,
        string Phase,
        DateTimeOffset StartedAt);

    sealed class TranslationProgress(double total)
    {
        public double Completed { get; set; }

        public double Total { get; } = total;

        public int CompletedBatches { get; set; }

        public int TotalBatches { get; set; }
    }
}

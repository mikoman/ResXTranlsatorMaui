using ResXTranslator.Controls;

namespace ResXTranslator;

enum LlmConnectionOutcome
{
    Cancelled,
    Connected,
    Removed
}

sealed record LlmConnectionResult(
    LlmConnectionOutcome Outcome,
    Uri? Endpoint = null,
    string? ApiKey = null);

partial class LlmConnectionPage : ModalSheetPage
{
    readonly LlmClient _client;
    readonly LlmProviderDescriptor _provider;
    readonly string? _currentApiKey;
    readonly int _concurrency;
    readonly TaskCompletionSource<LlmConnectionResult> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    bool _isBusy;
    bool _isClosing;

    public LlmConnectionPage(
        LlmClient client,
        LlmProviderDescriptor provider,
        Uri endpoint,
        string? currentApiKey,
        int concurrency,
        LlmConnectionState state)
    {
        _client = client;
        _provider = provider;
        _currentApiKey = currentApiKey;
        _concurrency = concurrency;
        PreferredSheetWidth = 560;
        PreferredSheetHeight = 500;
        InitializeComponent();
        PageTitleLabel.Text = $"{provider.Name} Connection";
        EndpointEntry.Text = endpoint.AbsoluteUri;
        EndpointEntry.IsEnabled = provider.Id is LlmProviderId.Ollama or LlmProviderId.LmStudio or LlmProviderId.Custom;
        ApiKeySection.IsVisible = provider.RequiresApiKey || provider.Id == LlmProviderId.Custom;
        KeyEntry.Placeholder = provider.RequiresApiKey ? "Required" : "Optional bearer token";
        SecurityDetailLabel.Text = provider.IsLocal
            ? "The app connects to this Mac or a private-network server. No inference engine is installed or started by ResXTranslator."
            : "The credential is validated with the provider, then stored in this device's secure credential store.";
        RemoveButton.Text = provider.IsLocal ? "Reset" : "Remove Key";
        RenderStoredState(state);

        var hasConnection = !provider.RequiresApiKey || !string.IsNullOrWhiteSpace(currentApiKey);
        ShowEditor(!hasConnection || state == LlmConnectionState.NotConnected);
    }

    public Task<LlmConnectionResult> Completion => _completion.Task;

    void RenderStoredState(LlmConnectionState state)
    {
        var (symbol, light, dark, title, detail) = state switch
        {
            LlmConnectionState.NeedsAttention =>
                ("exclamationmark.triangle.fill", "DangerLight", "DangerDark", "Needs attention",
                    $"{_provider.Name} rejected this connection. Edit it or remove its saved key."),
            LlmConnectionState.Unverified =>
                ("wifi.exclamationmark", "WarningLight", "WarningDark", "Couldn't verify",
                    $"The settings remain saved, but {_provider.Name} could not be reached."),
            LlmConnectionState.Checking =>
                ("arrow.triangle.2.circlepath", "SecondaryLabelLight", "SecondaryLabelDark", "Checking…",
                    $"Verifying the saved connection with {_provider.Name}."),
            _ => ("checkmark.circle.fill", "SuccessLight", "SuccessDark", "Connected",
                _provider.IsLocal
                    ? EndpointEntry.Text
                    : "The API key is stored securely on this device.")
        };

        StoredIcon.Symbol = symbol;
        StoredIcon.SetAppTheme(
            SymbolImage.TintProperty,
            Resource<Color>(light),
            Resource<Color>(dark));
        StoredTitleLabel.Text = title;
        StoredDetailLabel.Text = detail;
    }

    void ShowEditor(bool show)
    {
        ConnectedSection.IsVisible = !show;
        EditorSection.IsVisible = show;
        ConnectionError.IsVisible = false;
        KeyEntry.Text = string.Empty;
        UpdateConnectState();
    }

    void OnEditClicked(object? sender, EventArgs e) => ShowEditor(true);

    async void OnRemoveClicked(object? sender, EventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        var confirmed = await DisplayAlertAsync(
            _provider.IsLocal ? "Reset connection?" : "Remove API key?",
            _provider.IsLocal
                ? $"Reset {_provider.Name} to its default endpoint? Your selected model will be kept."
                : $"Remove the saved {_provider.Name} key from this device? Your selected model will be kept.",
            _provider.IsLocal ? "Reset" : "Remove Key",
            "Cancel");
        if (!confirmed)
        {
            return;
        }

        await LlmCredentialStore.RemoveAsync(_provider.Id);
        if (_provider.IsLocal)
        {
            LlmSettings.SaveEndpoint(_provider.Id, _provider.DefaultEndpoint);
        }
        await CloseAsync(new LlmConnectionResult(LlmConnectionOutcome.Removed));
    }

    void OnRevealClicked(object? sender, EventArgs e)
    {
        KeyEntry.IsPassword = !KeyEntry.IsPassword;
        RevealButton.Text = KeyEntry.IsPassword ? "Show" : "Hide";
    }

    void OnEndpointFocused(object? sender, FocusEventArgs e) =>
        VisualStateManager.GoToState(EndpointWell, "Focused");
    void OnEndpointUnfocused(object? sender, FocusEventArgs e) =>
        VisualStateManager.GoToState(EndpointWell, "Normal");
    void OnKeyFocused(object? sender, FocusEventArgs e) =>
        VisualStateManager.GoToState(KeyWell, "Focused");
    void OnKeyUnfocused(object? sender, FocusEventArgs e) =>
        VisualStateManager.GoToState(KeyWell, "Normal");

    void OnEditorChanged(object? sender, TextChangedEventArgs e)
    {
        ConnectionError.IsVisible = false;
        UpdateConnectState();
    }

    async void OnConnectClicked(object? sender, EventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        if (!LlmProviderRegistry.TryCreateEndpoint(
            _provider,
            EndpointEntry.Text ?? string.Empty,
            out var endpoint,
            out var endpointError))
        {
            ShowError(endpointError!);
            return;
        }

        var enteredKey = KeyEntry.Text?.Trim();
        var apiKey = string.IsNullOrWhiteSpace(enteredKey) ? _currentApiKey : enteredKey;
        if (_provider.RequiresApiKey && string.IsNullOrWhiteSpace(apiKey))
        {
            ShowError($"Paste a {_provider.Name} API key to continue.");
            return;
        }

        SetBusy(true);
        try
        {
            var profile = new LlmConnectionProfile(_provider.Id, endpoint!, apiKey, _concurrency);
            await _client.ValidateConnectionAsync(profile);
            if (!string.IsNullOrWhiteSpace(enteredKey))
            {
                await LlmCredentialStore.SetAsync(_provider.Id, enteredKey);
            }

            LlmSettings.SaveEndpoint(_provider.Id, endpoint!.AbsoluteUri);
            KeyEntry.Text = string.Empty;
            await CloseAsync(new LlmConnectionResult(
                LlmConnectionOutcome.Connected,
                endpoint,
                apiKey));
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

    async void OnCancelClicked(object? sender, EventArgs e) =>
        await CloseAsync(new LlmConnectionResult(LlmConnectionOutcome.Cancelled));

    void SetBusy(bool busy)
    {
        _isBusy = busy;
        EndpointEntry.IsEnabled = !busy && _provider.Id is LlmProviderId.Ollama or LlmProviderId.LmStudio or LlmProviderId.Custom;
        KeyEntry.IsEnabled = !busy;
        RevealButton.IsEnabled = !busy;
        ConnectButton.Text = busy ? "Connecting…" : "Connect";
        UpdateConnectState();
    }

    void UpdateConnectState()
    {
        var hasKey = !_provider.RequiresApiKey ||
            !string.IsNullOrWhiteSpace(KeyEntry.Text) ||
            !string.IsNullOrWhiteSpace(_currentApiKey);
        ConnectButton.IsEnabled = !_isBusy && hasKey && !string.IsNullOrWhiteSpace(EndpointEntry.Text);
    }

    void ShowError(string message)
    {
        ConnectionErrorLabel.Text = message;
        ConnectionError.IsVisible = true;
    }

    async Task CloseAsync(LlmConnectionResult result)
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        _completion.TrySetResult(result);
        await Navigation.PopModalAsync();
    }

    protected override bool OnBackButtonPressed()
    {
        Dispatcher.Dispatch(async () =>
            await CloseAsync(new LlmConnectionResult(LlmConnectionOutcome.Cancelled)));
        return true;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _completion.TrySetResult(new LlmConnectionResult(LlmConnectionOutcome.Cancelled));
    }

    static T Resource<T>(string key) =>
        Application.Current?.Resources[key] is T value ? value : default!;
}

using ResXTranslator.Controls;

namespace ResXTranslator;

enum OpenRouterConnectionOutcome
{
    Cancelled,
    Connected,
    Removed
}

sealed record OpenRouterConnectionResult(OpenRouterConnectionOutcome Outcome, string? ApiKey = null);

partial class OpenRouterConnectionPage : ModalSheetPage
{
    readonly OpenRouterClient _client;
    readonly TaskCompletionSource<OpenRouterConnectionResult> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    bool _isBusy;
    bool _isClosing;

    public OpenRouterConnectionPage(
        OpenRouterClient client,
        bool hasStoredKey,
        OpenRouterConnectionState connectionState)
    {
        _client = client;
        PreferredSheetWidth = 520;
        PreferredSheetHeight = hasStoredKey ? 300 : 360;
        InitializeComponent();
        RenderStoredKeyState(connectionState);
        ShowEditor(!hasStoredKey);
    }

    public Task<OpenRouterConnectionResult> Completion => _completion.Task;

    void RenderStoredKeyState(OpenRouterConnectionState state)
    {
        var (symbol, lightColor, darkColor, title, detail) = state switch
        {
            OpenRouterConnectionState.NeedsAttention =>
                ("exclamationmark.triangle.fill", "DangerLight", "DangerDark", "Key needs attention",
                    "OpenRouter rejected this saved key. Replace it or remove it from this device."),
            OpenRouterConnectionState.Unverified =>
                ("wifi.exclamationmark", "WarningLight", "WarningDark", "Couldn't verify",
                    "The key remains stored securely, but OpenRouter could not be reached."),
            OpenRouterConnectionState.Checking =>
                ("arrow.triangle.2.circlepath", "SecondaryLabelLight", "SecondaryLabelDark", "Checking…",
                    "Verifying the saved key with OpenRouter."),
            _ => ("checkmark.circle.fill", "SuccessLight", "SuccessDark", "Connected",
                "The API key is stored securely on this device.")
        };

        StoredKeyIcon.Symbol = symbol;
        StoredKeyIcon.SetAppTheme(
            SymbolImage.TintProperty,
            Resource<Color>(lightColor),
            Resource<Color>(darkColor));
        StoredKeyTitleLabel.Text = title;
        StoredKeyDetailLabel.Text = detail;
    }

    void ShowEditor(bool show)
    {
        ConnectedSection.IsVisible = !show;
        EditorSection.IsVisible = show;
        ConnectionError.IsVisible = false;
        KeyEntry.Text = string.Empty;
        KeyEntry.IsPassword = true;
        RevealButton.Text = "Show";
        UpdateConnectState();

        if (show)
        {
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(180), () => KeyEntry.Focus());
        }
    }

    void OnReplaceClicked(object? sender, EventArgs e) => ShowEditor(true);

    async void OnRemoveClicked(object? sender, EventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        var confirmed = await DisplayAlertAsync(
            "Remove API key?",
            "ResXTranslator will remove the saved OpenRouter key from this device. Your selected model will be kept.",
            "Remove Key",
            "Cancel");

        if (!confirmed)
        {
            return;
        }

        _isBusy = true;

        try
        {
            await OpenRouterCredentialStore.RemoveAsync();
            await CloseAsync(new OpenRouterConnectionResult(OpenRouterConnectionOutcome.Removed));
        }
        catch (Exception ex)
        {
            ShowError($"The API key could not be removed: {ex.Message}");
        }
        finally
        {
            _isBusy = false;
        }
    }

    void OnRevealClicked(object? sender, EventArgs e)
    {
        KeyEntry.IsPassword = !KeyEntry.IsPassword;
        RevealButton.Text = KeyEntry.IsPassword ? "Show" : "Hide";
    }

    void OnKeyFocused(object? sender, FocusEventArgs e) =>
        VisualStateManager.GoToState(KeyWell, "Focused");

    void OnKeyUnfocused(object? sender, FocusEventArgs e) =>
        VisualStateManager.GoToState(KeyWell, "Normal");

    void OnKeyTextChanged(object? sender, TextChangedEventArgs e)
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

        var apiKey = KeyEntry.Text?.Trim();

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            ShowError("Paste an OpenRouter API key to continue.");
            return;
        }

        SetBusy(true);

        try
        {
            await _client.ValidateApiKeyAsync(apiKey);
            await OpenRouterCredentialStore.SetAsync(apiKey);
            KeyEntry.Text = string.Empty;
            await CloseAsync(new OpenRouterConnectionResult(OpenRouterConnectionOutcome.Connected, apiKey));
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

    async void OnCancelClicked(object? sender, EventArgs e) => await CloseAsync(
        new OpenRouterConnectionResult(OpenRouterConnectionOutcome.Cancelled));

    void SetBusy(bool busy)
    {
        _isBusy = busy;
        KeyEntry.IsEnabled = !busy;
        RevealButton.IsEnabled = !busy;
        ConnectButton.Text = busy ? "Connecting…" : "Connect";
        UpdateConnectState();
    }

    void UpdateConnectState() =>
        ConnectButton.IsEnabled = !_isBusy && !string.IsNullOrWhiteSpace(KeyEntry.Text);

    void ShowError(string message)
    {
        ConnectionErrorLabel.Text = message;
        ConnectionError.IsVisible = true;
    }

    async Task CloseAsync(OpenRouterConnectionResult result)
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
        Dispatcher.Dispatch(async () => await CloseAsync(
            new OpenRouterConnectionResult(OpenRouterConnectionOutcome.Cancelled)));
        return true;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _completion.TrySetResult(new OpenRouterConnectionResult(OpenRouterConnectionOutcome.Cancelled));
    }

    static T Resource<T>(string key) =>
        Application.Current?.Resources[key] is T value ? value : default!;
}

using ResXTranslator.Controls;

namespace ResXTranslator;

partial class TranslationSettingsPage : ModalSheetPage
{
    readonly LlmClient _client;
    readonly LlmProviderDescriptor _provider;
    readonly LlmConnectionProfile? _profile;
    readonly LlmModel? _model;
    readonly TaskCompletionSource<TranslationSettingsResult?> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly CancellationTokenSource _cancellation = new();
    int _concurrency;
    bool _isBusy;
    bool _isClosing;

    public TranslationSettingsPage(
        LlmClient client,
        LlmProviderDescriptor provider,
        string domainInstructions,
        int concurrency,
        LlmConnectionProfile? profile,
        LlmModel? model)
    {
        _client = client;
        _provider = provider;
        _profile = profile;
        _model = model;
        PreferredSheetWidth = 620;
        PreferredSheetHeight = 630;
        InitializeComponent();

        DomainEditor.Text = domainInstructions;
        _concurrency = Math.Clamp(
            concurrency,
            LlmSettings.MinimumConcurrency,
            LlmSettings.MaximumConcurrency);
        ConcurrencySectionLabel.Text = provider.Id == LlmProviderId.OpenRouter
            ? "Provider requests"
            : "Requests";
        ConcurrencyDetailLabel.Text = provider switch
        {
            { Id: LlmProviderId.OpenRouter } =>
                "Paid models use this 1–10 limit. Free and unknown-price OpenRouter models remain fixed at 2 requests. Higher values may increase cost or trigger rate limits.",
            { IsLocal: true } =>
                "Match this to the loaded server model's Max Concurrent Predictions. Start at 1 for MLX models; values above the server limit queue work and increase memory pressure.",
            _ =>
                "This provider uses the selected 1–10 request limit. Higher values may increase cost, memory use, or trigger provider rate limits."
        };
        RenderConcurrency();
        RenderGeneratorAvailability();
        UpdateActionState();
    }

    public Task<TranslationSettingsResult?> Completion => _completion.Task;

    void OnDomainFocused(object? sender, FocusEventArgs e) =>
        VisualStateManager.GoToState(DomainWell, "Focused");

    void OnDomainUnfocused(object? sender, FocusEventArgs e) =>
        VisualStateManager.GoToState(DomainWell, "Normal");

    void OnBriefFocused(object? sender, FocusEventArgs e) =>
        VisualStateManager.GoToState(BriefWell, "Focused");

    void OnBriefUnfocused(object? sender, FocusEventArgs e) =>
        VisualStateManager.GoToState(BriefWell, "Normal");

    void OnDomainTextChanged(object? sender, TextChangedEventArgs e)
    {
        SettingsError.IsVisible = false;
        UpdateActionState();
    }

    void OnBriefTextChanged(object? sender, TextChangedEventArgs e)
    {
        SettingsError.IsVisible = false;
        UpdateActionState();
    }

    void OnDecreaseConcurrencyClicked(object? sender, EventArgs e)
    {
        if (!_isBusy && _concurrency > LlmSettings.MinimumConcurrency)
        {
            _concurrency--;
            RenderConcurrency();
        }
    }

    void OnIncreaseConcurrencyClicked(object? sender, EventArgs e)
    {
        if (!_isBusy && _concurrency < LlmSettings.MaximumConcurrency)
        {
            _concurrency++;
            RenderConcurrency();
        }
    }

    void RenderConcurrency()
    {
        ConcurrencyValueLabel.Text = _concurrency == 1 ? "1 request" : $"{_concurrency} requests";
        SemanticProperties.SetDescription(
            ConcurrencyValueLabel,
            $"Maximum {_concurrency} concurrent requests for {_provider.Name}.");
        DecreaseConcurrencyButton.IsEnabled = !_isBusy && _concurrency > LlmSettings.MinimumConcurrency;
        IncreaseConcurrencyButton.IsEnabled = !_isBusy && _concurrency < LlmSettings.MaximumConcurrency;
    }

    void RenderGeneratorAvailability()
    {
        GeneratorDetailLabel.Text = _profile is not null && _model is not null
            ? $"Uses {_model.Name}. Generating instructions sends your description to {_provider.Name} and may incur provider charges."
            : $"Connect {_provider.Name} and choose a tested model to use AI generation. Manual editing remains available.";
    }

    void OnRestoreDefaultClicked(object? sender, EventArgs e)
    {
        if (!_isBusy)
        {
            DomainEditor.Text = LlmSettings.DefaultDomainInstructions;
            SettingsError.IsVisible = false;
        }
    }

    async void OnGenerateClicked(object? sender, EventArgs e)
    {
        if (_isBusy || _profile is null || _model is null)
        {
            return;
        }

        var brief = BriefEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(brief))
        {
            ShowError("Write a short description before generating instructions.");
            return;
        }

        SetBusy(true);
        SettingsError.IsVisible = false;
        GeneratorDetailLabel.Text = $"Asking {_model.Name} to draft domain and tone instructions…";

        try
        {
            var generated = await _client.GenerateDomainInstructionsAsync(
                _profile,
                _model,
                brief,
                _cancellation.Token);
            DomainEditor.Text = generated;
            GeneratorDetailLabel.Text = "Draft generated. Review it, then select Save to apply it.";
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            RenderGeneratorAvailability();
        }
        finally
        {
            SetBusy(false);
        }
    }

    void SetBusy(bool busy)
    {
        _isBusy = busy;
        DomainEditor.IsEnabled = !busy;
        BriefEntry.IsEnabled = !busy;
        GenerationActivity.IsVisible = busy;
        GenerationActivity.IsRunning = busy;
        GenerateButton.Text = busy ? "Generating…" : "Generate";
        RenderConcurrency();
        UpdateActionState();
    }

    void UpdateActionState()
    {
        SaveButton.IsEnabled = !_isBusy && !string.IsNullOrWhiteSpace(DomainEditor.Text);
        GenerateButton.IsEnabled = !_isBusy && _profile is not null && _model is not null &&
            !string.IsNullOrWhiteSpace(BriefEntry.Text);
    }

    async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        var domainInstructions = DomainEditor.Text?.Trim();
        if (string.IsNullOrWhiteSpace(domainInstructions))
        {
            ShowError("Domain and tone instructions cannot be empty. Restore the default or write your own instructions.");
            return;
        }

        await CloseAsync(new TranslationSettingsResult(domainInstructions, _concurrency));
    }

    void ShowError(string message)
    {
        SettingsErrorLabel.Text = message;
        SettingsError.IsVisible = true;
    }

    async void OnCancelClicked(object? sender, EventArgs e) => await CloseAsync(null);

    async Task CloseAsync(TranslationSettingsResult? result)
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        await _cancellation.CancelAsync();
        _completion.TrySetResult(result);
        await Navigation.PopModalAsync();
    }

    protected override bool OnBackButtonPressed()
    {
        Dispatcher.Dispatch(async () => await CloseAsync(null));
        return true;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _cancellation.Cancel();
        _completion.TrySetResult(null);
    }
}

using ResXTranslator.Controls;

namespace ResXTranslator;

partial class TranslationSettingsPage : ModalSheetPage
{
    readonly OpenRouterClient _client;
    readonly string? _apiKey;
    readonly OpenRouterModel? _model;
    readonly TaskCompletionSource<TranslationSettingsResult?> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly CancellationTokenSource _cancellation = new();
    int _paidModelConcurrency;
    bool _isBusy;
    bool _isClosing;

    public TranslationSettingsPage(
        OpenRouterClient client,
        string domainInstructions,
        int paidModelConcurrency,
        string? apiKey,
        OpenRouterModel? model)
    {
        _client = client;
        _apiKey = apiKey;
        _model = model;
        PreferredSheetWidth = 620;
        PreferredSheetHeight = 630;
        InitializeComponent();

        DomainEditor.Text = domainInstructions;
        _paidModelConcurrency = Math.Clamp(
            paidModelConcurrency,
            OpenRouterSettings.MinimumPaidConcurrency,
            OpenRouterSettings.MaximumPaidConcurrency);
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
        if (_isBusy || _paidModelConcurrency <= OpenRouterSettings.MinimumPaidConcurrency)
        {
            return;
        }

        _paidModelConcurrency--;
        RenderConcurrency();
    }

    void OnIncreaseConcurrencyClicked(object? sender, EventArgs e)
    {
        if (_isBusy || _paidModelConcurrency >= OpenRouterSettings.MaximumPaidConcurrency)
        {
            return;
        }

        _paidModelConcurrency++;
        RenderConcurrency();
    }

    void RenderConcurrency()
    {
        ConcurrencyValueLabel.Text = _paidModelConcurrency == 1
            ? "1 request"
            : $"{_paidModelConcurrency} requests";
        SemanticProperties.SetDescription(
            ConcurrencyValueLabel,
            $"Maximum {_paidModelConcurrency} concurrent requests for paid models.");
        DecreaseConcurrencyButton.IsEnabled = !_isBusy &&
            _paidModelConcurrency > OpenRouterSettings.MinimumPaidConcurrency;
        IncreaseConcurrencyButton.IsEnabled = !_isBusy &&
            _paidModelConcurrency < OpenRouterSettings.MaximumPaidConcurrency;
    }

    void RenderGeneratorAvailability()
    {
        GeneratorDetailLabel.Text = _apiKey is not null && _model is not null
            ? $"Uses {_model.Name}. Generating instructions sends your description to OpenRouter and may incur model charges."
            : "Connect an OpenRouter account and choose an available model to use AI generation. Manual editing remains available.";
    }

    void OnRestoreDefaultClicked(object? sender, EventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        DomainEditor.Text = OpenRouterSettings.DefaultDomainInstructions;
        SettingsError.IsVisible = false;
    }

    async void OnGenerateClicked(object? sender, EventArgs e)
    {
        if (_isBusy || string.IsNullOrWhiteSpace(_apiKey) || _model is null)
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
                _apiKey,
                _model,
                brief,
                _cancellation.Token);
            DomainEditor.Text = generated;
            GeneratorDetailLabel.Text = "Draft generated. Review it, then select Save to apply it.";
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
            return;
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
        GenerateButton.IsEnabled = !_isBusy &&
            !string.IsNullOrWhiteSpace(_apiKey) &&
            _model is not null &&
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

        await CloseAsync(new TranslationSettingsResult(
            domainInstructions,
            _paidModelConcurrency));
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

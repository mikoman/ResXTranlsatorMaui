using System.Collections.ObjectModel;
using ResXTranslator.Controls;

namespace ResXTranslator;

partial class LlmModelPage : ModalSheetPage
{
    readonly LlmClient _client;
    readonly LlmProviderDescriptor _provider;
    readonly LlmConnectionProfile _profile;
    readonly string? _selectedModelId;
    readonly TaskCompletionSource<LlmModel?> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly CancellationTokenSource _cancellation = new();
    readonly ObservableCollection<LlmModelListItem> _rows = [];
    IReadOnlyList<LlmModel> _models;
    bool _isBusy;
    bool _isClosing;
    bool _loaded;
    string? _loadError;

    public LlmModelPage(
        LlmClient client,
        LlmProviderDescriptor provider,
        LlmConnectionProfile profile,
        IReadOnlyList<LlmModel> models,
        string? selectedModelId)
    {
        _client = client;
        _provider = provider;
        _profile = profile;
        _models = models;
        _selectedModelId = selectedModelId;
        PreferredSheetWidth = 620;
        PreferredSheetHeight = 540;
        InitializeComponent();
        PageTitleLabel.Text = $"Choose {_provider.Name} Model";
        ModelSearchEntry.Placeholder = $"Search {_provider.Name} models";
        ModelList.ItemsSource = _rows;
        RenderRows();
        Loaded += OnLoaded;
    }

    public Task<LlmModel?> Completion => _completion.Task;

    async void OnLoaded(object? sender, EventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await RefreshModelsAsync();
    }

    void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        ClearSearchButton.IsVisible = !string.IsNullOrEmpty(e.NewTextValue);
        RenderRows();
    }

    void OnManualModelTextChanged(object? sender, TextChangedEventArgs e)
    {
        CatalogNotice.IsVisible = false;
        UpdateActionState();
    }

    void OnSearchFocused(object? sender, FocusEventArgs e) =>
        VisualStateManager.GoToState(ModelSearchWell, "Focused");

    void OnSearchUnfocused(object? sender, FocusEventArgs e) =>
        VisualStateManager.GoToState(ModelSearchWell, "Normal");

    void OnManualModelFocused(object? sender, FocusEventArgs e) =>
        VisualStateManager.GoToState(ManualModelWell, "Focused");

    void OnManualModelUnfocused(object? sender, FocusEventArgs e) =>
        VisualStateManager.GoToState(ManualModelWell, "Normal");

    void OnClearSearchClicked(object? sender, EventArgs e)
    {
        ModelSearchEntry.Text = string.Empty;
        ModelSearchEntry.Focus();
    }

    async void OnRefreshClicked(object? sender, EventArgs e) => await RefreshModelsAsync();

    async Task RefreshModelsAsync()
    {
        if (_isBusy)
        {
            return;
        }

        SetBusy(true);
        _loadError = null;
        CatalogNotice.IsVisible = false;
        RenderRows();

        try
        {
            _models = await _client.GetModelsAsync(_profile, _cancellation.Token);
            if (!string.IsNullOrEmpty(_selectedModelId) &&
                !_models.Any(model => model.Id == _selectedModelId))
            {
                ShowNotice("The saved model is not in the current catalog. You can still enter its ID manually and test it.");
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _loadError = ex.Message;
            ShowNotice(_models.Count > 0
                ? "The catalog could not be refreshed. Showing the last loaded models; manual model IDs remain available."
                : $"The model catalog is unavailable: {ex.Message} Manual model IDs remain available.");
        }
        finally
        {
            SetBusy(false);
            RenderRows();
        }
    }

    void ShowNotice(string message)
    {
        CatalogNoticeLabel.Text = message;
        CatalogNotice.IsVisible = true;
    }

    void RenderRows()
    {
        var query = ModelSearchEntry?.Text?.Trim();
        var filtered = string.IsNullOrWhiteSpace(query)
            ? _models
            : _models.Where(model =>
                model.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                model.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                model.Provider.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();

        _rows.Clear();
        foreach (var model in filtered)
        {
            _rows.Add(new LlmModelListItem(
                model,
                model.Id == _selectedModelId,
                LlmSettings.GetParallelRequestLimit(_provider, model, _profile.Concurrency)));
        }

        if (_isBusy && _models.Count == 0)
        {
            ShowState("Loading models…", $"Fetching {_provider.Name}'s model catalog.", loading: true);
            return;
        }

        if (_rows.Count == 0)
        {
            ModelStateIcon.Symbol = _loadError is null ? "magnifyingglass" : "wifi.exclamationmark";
            ShowState(
                _loadError is null ? "No matching models" : "Catalog unavailable",
                "Enter an exact model ID above to run the required compatibility test.");
            return;
        }

        ModelListCard.IsVisible = true;
        ModelState.IsVisible = false;
    }

    void ShowState(string title, string detail, bool loading = false)
    {
        ModelListCard.IsVisible = false;
        ModelState.IsVisible = true;
        ModelActivity.IsVisible = loading;
        ModelActivity.IsRunning = loading;
        ModelStateIcon.IsVisible = !loading;
        ModelStateTitle.Text = title;
        ModelStateDetail.Text = detail;
    }

    async void OnModelSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not LlmModelListItem item)
        {
            return;
        }

        ModelList.SelectedItem = null;
        await TestAndCloseAsync(item.Model);
    }

    async void OnUseManualModelClicked(object? sender, EventArgs e)
    {
        var id = ManualModelEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            ShowNotice("Enter the exact provider model ID first.");
            return;
        }

        var model = _models.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, id, StringComparison.Ordinal))
            ?? new LlmModel(id, id, null, null) { Provider = _provider.Name };
        await TestAndCloseAsync(model);
    }

    async Task TestAndCloseAsync(LlmModel model)
    {
        if (_isBusy)
        {
            return;
        }

        if (LlmSettings.IsModelCompatibilityVerified(_provider.Id, _profile.Endpoint, model.Id))
        {
            await CloseAsync(model);
            return;
        }

        SetBusy(true);
        ShowNotice($"Testing {model.Id} with a small strict JSON Schema request. Cloud providers may charge for this request…");
        try
        {
            await _client.TestModelAsync(_profile, model, _cancellation.Token);
            LlmSettings.SaveModelCompatibility(_provider.Id, _profile.Endpoint, model.Id);
            await CloseAsync(model);
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ShowNotice($"This model cannot be used until its strict-schema test succeeds: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    void SetBusy(bool busy)
    {
        _isBusy = busy;
        ModelSearchEntry.IsEnabled = !busy;
        ManualModelEntry.IsEnabled = !busy;
        RefreshButton.IsEnabled = !busy;
        UpdateActionState();
    }

    void UpdateActionState() =>
        UseManualModelButton.IsEnabled = !_isBusy && !string.IsNullOrWhiteSpace(ManualModelEntry.Text);

    async void OnCancelClicked(object? sender, EventArgs e) => await CloseAsync(null);

    async Task CloseAsync(LlmModel? model)
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        await _cancellation.CancelAsync();
        _completion.TrySetResult(model);
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

    sealed record LlmModelListItem(LlmModel Model, bool IsSelected, int ParallelRequestLimit)
    {
        public string Name => Model.Name;
        public string Id => Model.Id;
        public string PriceDescription =>
            $"{Model.PriceDescription} · {ParallelRequestLimit} parallel requests";
        public bool RequiresReasoning => Model.RequiresReasoning;
        public string ReasoningDescription => "Reasoning required · excluded from response";
    }
}

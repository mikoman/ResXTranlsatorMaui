using System.Collections.ObjectModel;
using ResXTranslator.Controls;

namespace ResXTranslator;

partial class OpenRouterModelPage : ModalSheetPage
{
    readonly OpenRouterClient _client;
    readonly string _apiKey;
    readonly string? _selectedModelId;
    readonly TaskCompletionSource<OpenRouterModel?> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly CancellationTokenSource _cancellation = new();
    readonly ObservableCollection<OpenRouterModelListItem> _rows = [];
    IReadOnlyList<OpenRouterModel> _models;
    bool _isLoading;
    bool _isClosing;
    bool _loaded;
    string? _loadError;

    public OpenRouterModelPage(
        OpenRouterClient client,
        string apiKey,
        IReadOnlyList<OpenRouterModel> models,
        string? selectedModelId)
    {
        _client = client;
        _apiKey = apiKey;
        _models = models;
        _selectedModelId = selectedModelId;
        PreferredSheetWidth = 580;
        PreferredSheetHeight = 430;
        InitializeComponent();
        ModelList.ItemsSource = _rows;
        RenderRows();
        Loaded += OnLoaded;
    }

    public Task<OpenRouterModel?> Completion => _completion.Task;

    async void OnLoaded(object? sender, EventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await RefreshModelsAsync();
    }

    void OnSearchTextChanged(object? sender, TextChangedEventArgs e) => RenderRows();

    async void OnRefreshClicked(object? sender, EventArgs e) => await RefreshModelsAsync();

    async Task RefreshModelsAsync()
    {
        if (_isLoading)
        {
            return;
        }

        _isLoading = true;
        _loadError = null;
        CatalogNotice.IsVisible = false;
        RenderRows();

        try
        {
            var models = await _client.GetModelsAsync(_apiKey, _cancellation.Token);

            if (models.Count == 0)
            {
                throw new InvalidOperationException("OpenRouter returned no compatible structured-output models.");
            }

            _models = models;

            if (!string.IsNullOrEmpty(_selectedModelId) &&
                !_models.Any(model => model.Id == _selectedModelId))
            {
                ShowNotice("The previously selected model is no longer in OpenRouter's compatible catalog.");
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _loadError = ex.Message;

            if (_models.Count > 0)
            {
                ShowNotice("The catalog could not be refreshed. Showing the last loaded models.");
            }
        }
        finally
        {
            _isLoading = false;
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
        var query = ModelSearchBar?.Text?.Trim();
        var filtered = string.IsNullOrWhiteSpace(query)
            ? _models
            : _models.Where(model =>
                model.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                model.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                model.Provider.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();

        _rows.Clear();

        foreach (var model in filtered)
        {
            _rows.Add(new OpenRouterModelListItem(model, model.Id == _selectedModelId));
        }

        if (_isLoading && _models.Count == 0)
        {
            ShowState("Loading models…", "Fetching OpenRouter's compatible model catalog.", loading: true);
            return;
        }

        if (_models.Count == 0 && _loadError is not null)
        {
            ModelStateIcon.Symbol = "wifi.exclamationmark";
            ShowState("Models unavailable", _loadError, retry: true);
            return;
        }

        if (_rows.Count == 0)
        {
            ModelStateIcon.Symbol = "magnifyingglass";
            ShowState("No matching models", "Try a model name, provider, or OpenRouter model ID.");
            return;
        }

        ModelListCard.IsVisible = true;
        ModelState.IsVisible = false;
    }

    void ShowState(string title, string detail, bool loading = false, bool retry = false)
    {
        ModelListCard.IsVisible = false;
        ModelState.IsVisible = true;
        ModelActivity.IsVisible = loading;
        ModelActivity.IsRunning = loading;
        ModelStateIcon.IsVisible = !loading;
        ModelStateTitle.Text = title;
        ModelStateDetail.Text = detail;
        ModelRetryButton.IsVisible = retry;
    }

    async void OnModelSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not OpenRouterModelListItem item)
        {
            return;
        }

        ModelList.SelectedItem = null;
        await CloseAsync(item.Model);
    }

    async void OnCancelClicked(object? sender, EventArgs e) => await CloseAsync(null);

    async Task CloseAsync(OpenRouterModel? model)
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        _cancellation.Cancel();
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

    sealed record OpenRouterModelListItem(OpenRouterModel Model, bool IsSelected)
    {
        public string Name => Model.Name;
        public string Id => Model.Id;
        public string PriceDescription => Model.PriceDescription;
    }
}

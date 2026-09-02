using ResXTranslator.Controls;

namespace ResXTranslator;

partial class LlmProviderPage : ModalSheetPage
{
    readonly TaskCompletionSource<LlmProviderId?> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    bool _isClosing;

    public LlmProviderPage(
        IReadOnlyList<LlmProviderDescriptor> providers,
        LlmProviderId selectedProvider)
    {
        PreferredSheetWidth = 540;
        PreferredSheetHeight = 520;
        InitializeComponent();
        ProviderList.ItemsSource = providers.Where(provider => provider.IsAvailable).Select(provider => new ProviderRow(
            provider.Id,
            provider.Name,
            provider.IsLocal
                ? provider.IsAvailable
                    ? "Local inference · endpoint runs on this Mac or private network"
                    : "Local inference · available on Mac Catalyst"
                : provider.Id == LlmProviderId.Custom
                    ? "Strict-schema OpenAI-compatible HTTPS or private-network HTTP"
                    : "Cloud provider · API key stored securely on this device",
            provider.Id == selectedProvider,
            provider.IsAvailable)).ToArray();
    }

    public Task<LlmProviderId?> Completion => _completion.Task;

    async void OnProviderSelected(object? sender, SelectionChangedEventArgs e)
    {
        ProviderList.SelectedItem = null;
        if (e.CurrentSelection.FirstOrDefault() is not ProviderRow row)
        {
            return;
        }

        if (!row.IsAvailable)
        {
            await DisplayAlertAsync(
                "Available on Mac",
                "Ollama and LM Studio local endpoints are supported on Mac Catalyst in this release.",
                "OK");
            return;
        }

        await CloseAsync(row.Id);
    }

    async void OnCancelClicked(object? sender, EventArgs e) => await CloseAsync(null);

    async Task CloseAsync(LlmProviderId? providerId)
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        _completion.TrySetResult(providerId);
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
        _completion.TrySetResult(null);
    }

    sealed record ProviderRow(
        LlmProviderId Id,
        string Name,
        string Detail,
        bool IsSelected,
        bool IsAvailable);
}

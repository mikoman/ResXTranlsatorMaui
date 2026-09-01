using System.Collections.ObjectModel;
using ResXTranslator.Controls;

namespace ResXTranslator;

partial class LanguageSelectionPage : ModalSheetPage
{
    readonly string? _selectedCultureName;
    readonly HashSet<string> _unavailableCultureNames;
    readonly TaskCompletionSource<TargetLanguageOption?> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly ObservableCollection<LanguageListItem> _rows = [];
    bool _isClosing;

    public LanguageSelectionPage(
        string? selectedCultureName,
        IEnumerable<string>? unavailableCultureNames = null)
    {
        _selectedCultureName = selectedCultureName;
        _unavailableCultureNames = unavailableCultureNames?.ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        PreferredSheetWidth = 580;
        PreferredSheetHeight = 430;
        InitializeComponent();
        LanguageList.ItemsSource = _rows;
        RenderRows();
    }

    public Task<TargetLanguageOption?> Completion => _completion.Task;

    void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        ClearSearchButton.IsVisible = !string.IsNullOrEmpty(e.NewTextValue);
        LanguageNotice.IsVisible = false;
        RenderRows();
    }

    void OnSearchFocused(object? sender, FocusEventArgs e) =>
        VisualStateManager.GoToState(LanguageSearchWell, "Focused");

    void OnSearchUnfocused(object? sender, FocusEventArgs e) =>
        VisualStateManager.GoToState(LanguageSearchWell, "Normal");

    void OnClearSearchClicked(object? sender, EventArgs e)
    {
        LanguageSearchEntry.Text = string.Empty;
        LanguageSearchEntry.Focus();
    }

    void RenderRows()
    {
        var query = TargetLanguageOption.NormalizeSearchText(LanguageSearchEntry?.Text?.Trim() ?? string.Empty);
        var filtered = string.IsNullOrEmpty(query)
            ? LanguageCatalog.All
            : LanguageCatalog.All.Where(language => language.SearchText.Contains(query, StringComparison.Ordinal));

        _rows.Clear();

        foreach (var language in filtered)
        {
            var isAvailable = !_unavailableCultureNames.Contains(language.ColumnHeader);
            _rows.Add(new LanguageListItem(
                language,
                isAvailable,
                isAvailable && string.Equals(
                    language.ColumnHeader,
                    _selectedCultureName,
                    StringComparison.OrdinalIgnoreCase)));
        }

        LanguageListCard.IsVisible = _rows.Count > 0;
        LanguageState.IsVisible = _rows.Count == 0;
    }

    async void OnLanguageSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not LanguageListItem item)
        {
            return;
        }

        LanguageList.SelectedItem = null;

        if (!item.IsAvailable)
        {
            LanguageNoticeLabel.Text = $"{item.Language.ColumnHeader} is already present in the selected spreadsheet.";
            LanguageNotice.IsVisible = true;
            return;
        }

        await CloseAsync(item.Language);
    }

    async void OnCancelClicked(object? sender, EventArgs e) => await CloseAsync(null);

    async Task CloseAsync(TargetLanguageOption? language)
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        _completion.TrySetResult(language);
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

    sealed record LanguageListItem(
        TargetLanguageOption Language,
        bool IsAvailable,
        bool IsSelected)
    {
        public string DisplayName => Language.DisplayName;
        public string Detail => Language.Detail;
        public string UnavailableReason => IsAvailable ? string.Empty : "Already added";
        public double Opacity => IsAvailable ? 1 : 0.5;
        public string AccessibilityDescription => IsAvailable
            ? $"{DisplayName}, {Detail}"
            : $"{DisplayName}, {Detail}, already present in spreadsheet";
    }
}

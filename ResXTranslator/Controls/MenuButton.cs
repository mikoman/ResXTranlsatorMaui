using System.Collections.ObjectModel;

namespace ResXTranslator.Controls;

/// <summary>
/// A <see cref="Button"/> that opens the platform's own pull-down menu: a
/// <c>UIMenu</c> on Mac Catalyst and iOS, a <c>MenuFlyout</c> on Windows, and an
/// action sheet on Android. Replaces <see cref="Picker"/>, which renders as an
/// iOS scroll wheel on Mac Catalyst.
/// </summary>
/// <remarks>
/// On Apple platforms the platform attach sets <c>ShowsMenuAsPrimaryAction</c>,
/// which suppresses <c>TouchUpInside</c> — so <see cref="Button.Clicked"/> never
/// raises on a MenuButton. Use <see cref="SelectionChanged"/> instead.
/// </remarks>
class MenuButton : Button
{
    /// <summary>Mapper key for the platform menu attach; see MauiProgram.</summary>
    internal const string MenuMapperKey = "MenuButton.NativeMenu";

    public static readonly BindableProperty SelectedOptionProperty = BindableProperty.Create(
        nameof(SelectedOption), typeof(MenuOption), typeof(MenuButton), null,
        propertyChanged: OnMenuStateChanged);

    public static readonly BindableProperty PlaceholderProperty = BindableProperty.Create(
        nameof(Placeholder), typeof(string), typeof(MenuButton), string.Empty,
        propertyChanged: OnMenuStateChanged);

    public MenuButton() => Options.CollectionChanged += OnOptionsChanged;

    /// <summary>The rows to offer, in display order.</summary>
    public ObservableCollection<MenuOption> Options { get; } = [];

    public MenuOption? SelectedOption
    {
        get => (MenuOption?)GetValue(SelectedOptionProperty);
        set => SetValue(SelectedOptionProperty, value);
    }

    /// <summary>Label shown while <see cref="SelectedOption"/> is null.</summary>
    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public event EventHandler<MenuOption>? SelectionChanged;

    /// <summary>
    /// Replaces every row in one shot, preserving the current selection by
    /// <see cref="MenuOption.Title"/> where the new list still offers it.
    /// </summary>
    public void SetOptions(IEnumerable<MenuOption> options)
    {
        var previousTitle = SelectedOption?.Title;

        Options.CollectionChanged -= OnOptionsChanged;
        Options.Clear();

        foreach (var option in options)
        {
            Options.Add(option);
        }

        Options.CollectionChanged += OnOptionsChanged;

        SelectedOption = Options.FirstOrDefault(option =>
            option.Title == previousTitle && option.IsEnabled);

        RebuildMenu();
    }

    /// <summary>Called by the platform attach when the user picks a row.</summary>
    internal void ReportSelection(MenuOption option)
    {
        SelectedOption = option;
        SelectionChanged?.Invoke(this, option);
    }

    void OnOptionsChanged(object? sender, EventArgs e) => RebuildMenu();

    void RebuildMenu()
    {
        Text = SelectedOption?.Title ?? Placeholder;
        Handler?.UpdateValue(MenuMapperKey);
    }

    static void OnMenuStateChanged(BindableObject bindable, object oldValue, object newValue) =>
        ((MenuButton)bindable).RebuildMenu();
}

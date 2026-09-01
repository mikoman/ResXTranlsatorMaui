namespace ResXTranslator.Controls;

/// <summary>How much visual weight an <see cref="ActionButton"/> carries.</summary>
enum ButtonProminence
{
    /// <summary>Tinted label, no bezel. A subordinate action.</summary>
    Plain,

    /// <summary>Filled with the accent tint. The one primary action on a surface.</summary>
    Filled
}

/// <summary>
/// A <see cref="Button"/> whose weight is expressed through the platform's own
/// button configuration rather than by painting over it.
/// </summary>
/// <remarks>
/// In the Mac user interface idiom a UIButton adopts AppKit metrics and draws its
/// own bezel, which overrides MAUI's BackgroundColor and flattens every button to
/// the same weight. Setting <c>UIButton.Configuration</c> to the filled or plain
/// configuration is how the hierarchy is expressed natively instead.
/// </remarks>
class ActionButton : Button
{
    internal const string ProminenceMapperKey = "ActionButton.Prominence";

    public static readonly BindableProperty ProminenceProperty = BindableProperty.Create(
        nameof(Prominence), typeof(ButtonProminence), typeof(ActionButton), ButtonProminence.Plain,
        propertyChanged: OnProminenceChanged);

    public static readonly BindableProperty AccentProperty = BindableProperty.Create(
        nameof(Accent), typeof(Color), typeof(ActionButton), null,
        propertyChanged: OnProminenceChanged);

    public ButtonProminence Prominence
    {
        get => (ButtonProminence)GetValue(ProminenceProperty);
        set => SetValue(ProminenceProperty, value);
    }

    /// <summary>Fill colour when filled, label colour when plain.</summary>
    public Color? Accent
    {
        get => (Color?)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);

        // Enablement is part of the native configuration, not a visual state.
        if (propertyName == nameof(IsEnabled))
        {
            Handler?.UpdateValue(ProminenceMapperKey);
        }
    }

    static void OnProminenceChanged(BindableObject bindable, object oldValue, object newValue) =>
        ((ActionButton)bindable).Handler?.UpdateValue(ProminenceMapperKey);
}

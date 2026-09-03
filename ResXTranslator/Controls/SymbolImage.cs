#if MACCATALYST
using Microsoft.Maui.Platform;
using UIKit;
#endif

namespace ResXTranslator.Controls;

/// <summary>
/// An <see cref="Image"/> that renders a native symbol on each desktop platform.
/// </summary>
/// <remarks>
/// Mac symbols are painted by a handler mapper (see MauiProgram) rather than a custom
/// <c>IImageSourceService</c>: the service route works but costs ~4x the code to
/// buy symbol support in arbitrary Image.Source bindings, which a single-page app
/// does not need. There is no supported font trick — SF Symbols live in private
/// system fonts. Windows uses the equivalent Segoe Fluent Icons glyph.
/// </remarks>
class SymbolImage : Image
{
    internal const string SymbolMapperKey = "SymbolImage.Symbol";

    public static readonly BindableProperty SymbolProperty = BindableProperty.Create(
        nameof(Symbol), typeof(string), typeof(SymbolImage), string.Empty,
        propertyChanged: OnSymbolChanged);

    public static readonly BindableProperty PointSizeProperty = BindableProperty.Create(
        nameof(PointSize), typeof(double), typeof(SymbolImage), 15d,
        propertyChanged: OnSymbolChanged);

    public static readonly BindableProperty TintProperty = BindableProperty.Create(
        nameof(Tint), typeof(Color), typeof(SymbolImage), null,
        propertyChanged: OnSymbolChanged);

    public static readonly BindableProperty BoldProperty = BindableProperty.Create(
        nameof(Bold), typeof(bool), typeof(SymbolImage), false,
        propertyChanged: OnSymbolChanged);

    /// <summary>SF Symbol name, e.g. <c>doc.badge.plus</c>.</summary>
    public string Symbol
    {
        get => (string)GetValue(SymbolProperty);
        set => SetValue(SymbolProperty, value);
    }

    public double PointSize
    {
        get => (double)GetValue(PointSizeProperty);
        set => SetValue(PointSizeProperty, value);
    }

    /// <summary>
    /// Baked into the UIImage. MAUI's Image has no tint property, and
    /// UIImageView.TintColor only affects template images.
    /// </summary>
    public Color? Tint
    {
        get => (Color?)GetValue(TintProperty);
        set => SetValue(TintProperty, value);
    }

    public bool Bold
    {
        get => (bool)GetValue(BoldProperty);
        set => SetValue(BoldProperty, value);
    }

#if MACCATALYST
    internal UIImage? Render()
    {
        if (string.IsNullOrEmpty(Symbol))
        {
            return null;
        }

        var configuration = UIImageSymbolConfiguration.Create(
            (nfloat)PointSize,
            Bold ? UIImageSymbolWeight.Semibold : UIImageSymbolWeight.Regular);

        var image = UIImage.GetSystemImage(Symbol)?.ApplyConfiguration(configuration);

        if (image is not null && Tint is not null)
        {
            // AlwaysOriginal is load-bearing: the single-argument overload uses
            // Automatic, which leaves a symbol as a template image and lets
            // UIImageView.TintColor win — so every icon comes out system blue.
            image = image.ApplyTintColor(Tint.ToPlatform(), UIImageRenderingMode.AlwaysOriginal);
        }

        return image;
    }
#endif

    static void OnSymbolChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var symbol = (SymbolImage)bindable;
#if WINDOWS
        symbol.Source = symbol.RenderWindows();
#endif
        symbol.Handler?.UpdateValue(SymbolMapperKey);
    }

#if WINDOWS
    FontImageSource? RenderWindows()
    {
        var glyph = Symbol switch
        {
            "checkmark" => "\uE73E",
            "checkmark.circle.fill" => "\uE930",
            "doc.badge.plus" => "\uE710",
            "doc.text.fill" => "\uE8A5",
            "exclamationmark.triangle.fill" => "\uE7BA",
            "folder.fill" => "\uE8B7",
            "gearshape.fill" => "\uE713",
            "info.circle.fill" => "\uE946",
            "key.fill" => "\uE192",
            "magnifyingglass" => "\uE721",
            _ => null
        };

        return glyph is null
            ? null
            : new FontImageSource
            {
                Glyph = glyph,
                FontFamily = "Segoe Fluent Icons",
                Size = PointSize,
                Color = Tint ?? Colors.Black
            };
    }
#endif
}

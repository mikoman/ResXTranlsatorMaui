#if IOS || MACCATALYST
using UIKit;
#elif WINDOWS
using Microsoft.UI.Xaml.Controls;
#endif

namespace ResXTranslator.Controls;

/// <summary>
/// Attaches the platform's native pull-down to a <see cref="MenuButton"/>.
/// </summary>
/// <remarks>
/// One file with conditional compilation rather than <c>Platforms/</c> folders:
/// the Apple branch is identical for ios and maccatalyst, and the MAUI
/// SingleProject targets only re-include the five known platform folders per
/// target framework — a shared <c>Platforms/Apple/</c> would compile under none.
/// On maccatalyst the SDK defines MACCATALYST but not IOS, hence the pair.
/// </remarks>
static class MenuButtonPlatform
{
#if IOS || MACCATALYST
    public static void Apply(MenuButton button, UIButton native)
    {
        if (button.Options.Count == 0)
        {
            native.Menu = null;
            native.ShowsMenuAsPrimaryAction = false;
            return;
        }

        var actions = new UIMenuElement[button.Options.Count];

        for (var index = 0; index < button.Options.Count; index++)
        {
            var option = button.Options[index];
            var action = UIAction.Create(
                option.Subtitle is null ? option.Title : $"{option.Title} — {option.Subtitle}",
                image: null,
                identifier: null,
                handler: _ => button.ReportSelection(option));

            action.State = ReferenceEquals(option, button.SelectedOption)
                ? UIMenuElementState.On
                : UIMenuElementState.Off;

            if (!option.IsEnabled)
            {
                action.Attributes = UIMenuElementAttributes.Disabled;
            }

            actions[index] = action;
        }

        native.Menu = UIMenu.Create(actions);

        // Without Fixed, UIKit reverses the item order when the menu opens upward.
        // The property is 16.0+; below that the order is UIKit's to choose.
        if (OperatingSystem.IsIOSVersionAtLeast(16) || OperatingSystem.IsMacCatalystVersionAtLeast(16))
        {
            native.PreferredMenuElementOrder = UIContextMenuConfigurationElementOrder.Fixed;
        }

        // Turns the button into the menu's anchor. Also suppresses TouchUpInside,
        // which is why MenuButton does not use Clicked.
        native.ShowsMenuAsPrimaryAction = true;
    }
#elif WINDOWS
    public static void Apply(MenuButton button, Button native)
    {
        if (button.Options.Count == 0)
        {
            native.Flyout = null;
            return;
        }

        var flyout = new MenuFlyout();

        foreach (var option in button.Options)
        {
            // RadioMenuFlyoutItem gives WinUI's own single-selection checkmark.
            var item = new RadioMenuFlyoutItem
            {
                Text = option.Subtitle is null ? option.Title : $"{option.Title} — {option.Subtitle}",
                IsChecked = ReferenceEquals(option, button.SelectedOption),
                IsEnabled = option.IsEnabled,
                GroupName = "MenuButtonOptions"
            };

            var captured = option;
            item.Click += (_, _) => button.ReportSelection(captured);
            flyout.Items.Add(item);
        }

        native.Flyout = flyout;
    }
#endif

    /// <summary>
    /// Fallback for platforms with no anchored pull-down: an action sheet.
    /// Android only — a UIMenu is already the correct affordance on iPhone.
    /// </summary>
    public static async Task ShowFallbackAsync(MenuButton button)
    {
        var page = FindPage(button);

        if (page is null)
        {
            return;
        }

        var selectable = button.Options.Where(option => option.IsEnabled).ToArray();

        if (selectable.Length == 0)
        {
            return;
        }

        var titles = selectable.Select(option => option.Title).ToArray();
        var choice = await page.DisplayActionSheetAsync(button.Placeholder, "Cancel", null, titles);

        // Dismissal returns the cancel title, or null on some platforms.
        var picked = selectable.FirstOrDefault(option => option.Title == choice);

        if (picked is not null)
        {
            button.ReportSelection(picked);
        }
    }

    // Application.Current.MainPage is gone in .NET 10, so walk the tree.
    static Page? FindPage(Element element)
    {
        for (Element? current = element; current is not null; current = current.Parent)
        {
            if (current is Page page)
            {
                return page;
            }
        }

        return null;
    }
}

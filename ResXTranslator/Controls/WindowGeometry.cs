#if MACCATALYST
using CoreGraphics;
using UIKit;
#endif

namespace ResXTranslator.Controls;

/// <summary>
/// Sizes the desktop window on Mac Catalyst.
/// </summary>
/// <remarks>
/// MAUI's <c>Window.Width</c>/<c>Height</c> path does not land here, for two
/// reasons that compound: it routes through
/// <c>UIWindowScene.RequestGeometryUpdate</c>, which needs a scene that does not
/// exist while <c>CreateWindow</c> runs, and assigning the same value again later
/// fires no property change so it never reaches the platform a second time.
/// Calling the scene API directly fixes that, but macOS still applies its own
/// restored frame shortly after the scene connects and overwrites an immediate
/// request — hence the second, delayed request, which is the one that sticks.
/// </remarks>
static class WindowGeometry
{
    public const double PreferredWidth = 720;
    public const double PreferredHeight = 680;
    public const double MinimumWidth = 660;
    public const double MinimumHeight = 560;

    /// <summary>Delay for the request that outlives macOS's own frame restoration.</summary>
    static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(450);

    static bool _applied;

    /// <summary>Applies the preferred size once, centred on the main display.</summary>
    public static void ApplyOnce(Window? window)
    {
        if (_applied || window is null)
        {
            return;
        }

#if MACCATALYST
        if (window.Handler?.PlatformView is not UIWindow platformWindow ||
            platformWindow.WindowScene is not { } scene)
        {
            return;
        }

        _applied = true;

        if (scene.SizeRestrictions is { } restrictions)
        {
            restrictions.MinimumSize = new CGSize(MinimumWidth, MinimumHeight);
        }

        if (!OperatingSystem.IsMacCatalystVersionAtLeast(16))
        {
            return;
        }

        var display = DeviceDisplay.Current.MainDisplayInfo;
        var x = 0d;
        var y = 0d;

        if (display.Width > 0 && display.Density > 0)
        {
            x = Math.Max(0, (display.Width / display.Density - PreferredWidth) / 2);
            y = Math.Max(0, (display.Height / display.Density - PreferredHeight) / 2);
        }

        var preferences = new UIWindowSceneGeometryPreferencesMac
        {
            SystemFrame = new CGRect(x, y, PreferredWidth, PreferredHeight)
        };

        // First request narrows the window immediately so the correction is small.
        scene.RequestGeometryUpdate(preferences, _ => { });

        // Second request lands after macOS has finished imposing its own frame.
        // The guard is repeated inside the lambda: platform-version analysis does
        // not flow across the closure.
        window.Dispatcher.DispatchDelayed(SettleDelay, () =>
        {
            if (OperatingSystem.IsMacCatalystVersionAtLeast(16))
            {
                scene.RequestGeometryUpdate(preferences, _ => { });
            }
        });
#endif
    }
}

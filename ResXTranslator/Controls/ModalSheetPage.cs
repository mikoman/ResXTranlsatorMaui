#if IOS || MACCATALYST
using CoreGraphics;
using UIKit;
#endif

namespace ResXTranslator.Controls;

/// <summary>
/// A focused modal surface that uses Apple's form-sheet presentation while
/// retaining MAUI's normal modal-page behaviour on other platforms.
/// </summary>
class ModalSheetPage : ContentPage
{
    public double PreferredSheetWidth { get; init; } = 520;

    public double PreferredSheetHeight { get; init; } = 380;

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

#if IOS || MACCATALYST
        if (Handler?.PlatformView is UIViewController controller)
        {
            controller.ModalPresentationStyle = UIModalPresentationStyle.FormSheet;
            controller.PreferredContentSize = new CGSize(PreferredSheetWidth, PreferredSheetHeight);
        }
#endif
    }
}

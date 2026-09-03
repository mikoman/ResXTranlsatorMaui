#if MACCATALYST
using Foundation;
using UIKit;
using UniformTypeIdentifiers;
#endif

namespace ResXTranslator;

sealed class PickedFolder(string path, Action? release = null) : IDisposable
{
    readonly Action? _release = release;

    public string Path { get; } = path;

    public void Dispose() => _release?.Invoke();
}

static class FolderPickerService
{
    public static async Task<PickedFolder?> PickAsync()
    {
#if MACCATALYST
        var presenter = GetPresenter();

        if (presenter is null)
        {
            throw new InvalidOperationException("The folder picker could not be presented.");
        }

        var completion = new TaskCompletionSource<PickedFolder?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var picker = new UIDocumentPickerViewController([UTTypes.Folder], false)
        {
            AllowsMultipleSelection = false
        };
        var pickerDelegate = new FolderPickerDelegate(completion);
        picker.Delegate = pickerDelegate;
        presenter.PresentViewController(picker, true, null);
        return await completion.Task;
#elif WINDOWS
        var window = Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView
            as Microsoft.UI.Xaml.Window;
        if (window is null)
        {
            throw new InvalidOperationException("The folder picker could not find the application window.");
        }

        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*");
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
        var folder = await picker.PickSingleFolderAsync();
        return folder is null ? null : new PickedFolder(folder.Path);
#else
        throw new PlatformNotSupportedException("ResXTranslator supports desktop folder selection only.");
#endif
    }

#if MACCATALYST
    static UIViewController? GetPresenter()
    {
        var controller = UIApplication.SharedApplication.ConnectedScenes
            .OfType<UIWindowScene>()
            .SelectMany(scene => scene.Windows)
            .FirstOrDefault(window => window.IsKeyWindow)
            ?.RootViewController;

        while (controller?.PresentedViewController is { } presented)
        {
            controller = presented;
        }

        return controller;
    }

    sealed class FolderPickerDelegate(TaskCompletionSource<PickedFolder?> completion)
        : UIDocumentPickerDelegate
    {
        public override void DidPickDocument(UIDocumentPickerViewController controller, NSUrl url) =>
            Complete(url);

        public override void DidPickDocument(
            UIDocumentPickerViewController controller,
            NSUrl[] urls) => Complete(urls.FirstOrDefault());

        public override void WasCancelled(UIDocumentPickerViewController controller) =>
            completion.TrySetResult(null);

        void Complete(NSUrl? url)
        {
            if (url?.Path is not { } path)
            {
                completion.TrySetResult(null);
                return;
            }

            var hasSecurityScope = url.StartAccessingSecurityScopedResource();
            completion.TrySetResult(new PickedFolder(
                path,
                hasSecurityScope ? url.StopAccessingSecurityScopedResource : null));
        }
    }
#endif
}

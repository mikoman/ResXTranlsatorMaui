namespace ResXTranslator;

public partial class App : Application
{
	readonly AppShell _shell;

	public App(IServiceProvider services)
	{
		InitializeComponent();
		// Resolve pages only after application resources are loaded. Constructing
		// MainPage before this point makes StaticResource lookups fail at startup.
		_shell = services.GetRequiredService<AppShell>();
	}

	// .NET 10 removed Application.MainPage. Windows are created here instead.
	protected override Window CreateWindow(IActivationState? activationState) =>
		// Size and position are applied from MainPage once the native window exists.
		new Window(_shell)
		{
			Title = "ResXTranslator"
		};
}

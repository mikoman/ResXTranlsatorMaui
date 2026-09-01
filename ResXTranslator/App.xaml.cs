namespace ResXTranslator;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
	}

	// .NET 10 removed Application.MainPage. Windows are created here instead.
	protected override Window CreateWindow(IActivationState? activationState) =>
		// Size and position are applied from MainPage once the platform scene
		// exists — see Controls/WindowGeometry for why this cannot happen here.
		new Window(new AppShell())
		{
			Title = "ResXTranslator"
		};
}

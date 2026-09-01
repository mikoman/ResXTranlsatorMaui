namespace ResXTranslator;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
	}

	// .NET 10 removed Application.MainPage. Windows are created here instead.
	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell())
		{
			Title = "ResXTranslator"
		};
	}
}

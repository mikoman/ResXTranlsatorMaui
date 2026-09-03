using Microsoft.Extensions.Logging;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using ResXTranslator.Controls;

namespace ResXTranslator;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				// Registered as aliases only. Nothing in Styles.xaml names them, so
				// every control falls through to the platform system face: SF Pro on
				// Mac Catalyst and Segoe UI Variable on Windows.
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		builder.Services.AddSingleton<OpenRouterClient>();
		builder.Services.AddSingleton<LlmProviderRegistry>();
		builder.Services.AddSingleton<LlmClient>();
		builder.Services.AddSingleton(serviceProvider => new MainPage(
			serviceProvider.GetRequiredService<LlmClient>(),
			serviceProvider.GetRequiredService<LlmProviderRegistry>()));
		builder.Services.AddSingleton<AppShell>();

		CustomizeHandlers();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}

	/// <summary>
	/// Handler customizations. Mappers are global, so each one guards on the
	/// concrete type it cares about. A custom mapper key re-runs on every handler
	/// connect, so these survive handler recycling, and
	/// <c>Handler.UpdateValue(key)</c> re-runs one on demand.
	/// </summary>
	static void CustomizeHandlers()
	{
		ButtonHandler.Mapper.AppendToMapping(ActionButton.ProminenceMapperKey, (handler, view) =>
		{
			if (view is not ActionButton button)
			{
				return;
			}

#if MACCATALYST
			if (handler.PlatformView is not UIKit.UIButton native)
			{
				return;
			}

			var configuration = button.Prominence == ButtonProminence.Filled
				? UIKit.UIButtonConfiguration.FilledButtonConfiguration
				: UIKit.UIButtonConfiguration.PlainButtonConfiguration;

			if (button.Accent is { } accent)
			{
				var platformAccent = accent.ToPlatform();

				if (button.Prominence == ButtonProminence.Filled)
				{
					// Disabled goes neutral, the way macOS dims a default button. A
					// faded tint keeps the fill but leaves the label unreadable
					// against it, which is worse than losing the colour.
					configuration.BaseBackgroundColor = button.IsEnabled
						? platformAccent
						: UIKit.UIColor.SecondarySystemFill;
					configuration.BaseForegroundColor = button.IsEnabled
						? UIKit.UIColor.White
						: UIKit.UIColor.SecondaryLabel;
				}
				else
				{
					configuration.BaseForegroundColor = button.IsEnabled
						? platformAccent
						: UIKit.UIColor.SecondaryLabel;
				}
			}

			native.Configuration = configuration;
			native.SetNeedsUpdateConfiguration();
#endif
		});

		ImageHandler.Mapper.AppendToMapping(SymbolImage.SymbolMapperKey, (handler, view) =>
		{
#if MACCATALYST
			if (view is SymbolImage symbol && handler.PlatformView is UIKit.UIImageView imageView)
			{
				if (symbol.Tint is { } tint)
				{
					imageView.TintColor = tint.ToPlatform();
				}

				imageView.Image = symbol.Render();
			}
#endif
		});

#if MACCATALYST
		// The Entry sits inside our own FieldWell border. Clearing the native
		// RoundedRect bezel stops it reading as double-framed.
		EntryHandler.Mapper.AppendToMapping("Entry.NoNativeBezel", (handler, _) =>
		{
			if (handler.PlatformView is UIKit.UITextField field)
			{
				field.BorderStyle = UIKit.UITextBorderStyle.None;
			}
		});
#endif
	}
}

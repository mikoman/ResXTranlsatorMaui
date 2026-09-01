# ResXTranslator

ResXTranslator is a straightforward and UI-based .resx file translator designed for simplicity and efficiency. While its core functionality is tailored to specific needs, it can be effortlessly adapted to support a broader range of languages or additional features. The design is basic barely enough to be functional. You can change the list of languages in `MainPage.xaml.cs` if you need to do so.

Built with **.NET 10** and **.NET MAUI**, and it runs as a native **Mac Catalyst** app on macOS.

## Features

- Utilizes the **DeepL API** for translations using the [DeepL API .NET library](https://www.nuget.org/packages/DeepL.net/).
- Exports translations to Excel using the MIT-licensed [ClosedXML library](https://github.com/ClosedXML/ClosedXML).
- Imports translation audit documents from CSV or Excel (`.xlsx`) and adds new language/value and status columns without overwriting the source.
- Exports spreadsheet translations to either Excel or CSV while preserving the original column order and multiline CSV values.
- Support for multiple languages (easy to extend).
- Translations are batched (up to 50 strings per API request) with live progress.
- Output files are written next to the source `.resx` file, with no machine-specific paths.

🔜 **Coming Soon:** Google Translate API integration!

## Running on macOS

### Prerequisites

- macOS with Xcode installed (`xcode-select --install` plus a full Xcode from the App Store).
- The [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
- The MAUI workloads. Mac Catalyst is the target that produces a macOS app:

```bash
sudo dotnet workload install maui-maccatalyst
# or, to install every platform this project can target:
sudo dotnet workload install maui
```

`sudo` is required because the SDK lives in `/usr/local/share/dotnet`. Verify with `dotnet workload list`.

### Build and run

```bash
# Build the macOS (Mac Catalyst) app
dotnet build ResXTranslator/ResXTranslator.csproj -f net10.0-maccatalyst

# Build and launch it
dotnet build ResXTranslator/ResXTranslator.csproj -f net10.0-maccatalyst -t:Run
```

Debug builds target the architecture of the Mac you build on (`maccatalyst-arm64` on Apple Silicon).
The built app bundle lands in `ResXTranslator/bin/Debug/net10.0-maccatalyst/<rid>/ResXTranslator.app`.

To build the other targets, swap the framework: `net10.0-ios`, `net10.0-android`, or `net10.0-windows10.0.19041.0` (Windows only).

## Usage

1. Paste your DeepL API key into the field at the top. It is stored locally with the MAUI `Preferences` API, so you only enter it once.
2. Click **Select RESX, Excel, or CSV File** and pick a `.resx`, `.xlsx`, or `.csv` file. Spreadsheet files must follow the translation audit layout with a `Default` column and adjacent language/status pairs such as `fr` and `fr Status`.
3. Optionally pick a single target language; leaving the picker empty translates into every configured language for RESX, or every configured language missing from a spreadsheet.
4. Click **Translate**. RESX results are written as `<SourceName>.<culture>.resx` (for example `AppResources.pt-PT.resx`). For CSV/Excel input, each missing language is inserted after the last language/status pair and a `.translated.csv` or `.translated.xlsx` copy is written. The source file is not overwritten.
5. **Export to Excel** or **Export to CSV** writes the currently loaded data to the selected format in the same output folder.

## Dependencies

ResXTranslator uses these NuGet packages:
- [DeepL.net](https://www.nuget.org/packages/DeepL.net/) for smooth integration with the DeepL translation service.
- [ClosedXML](https://github.com/ClosedXML/ClosedXML) for reading and writing `.xlsx` workbooks. ClosedXML is distributed under the MIT license.
- The app includes its own small CSV reader/writer, so CSV support has no additional package dependency.

## Inspiration

This project is inspired by some great tools available on GitHub:

- [resxtranslator by HakanL](https://github.com/HakanL/resxtranslator)
- [ResxTranslator by stevencohn](https://github.com/stevencohn/ResxTranslator)
- [Resx-Translator by DamienDoumer](https://github.com/DamienDoumer/Resx-Translator)

However, while the existing tools offer a wide array of features, I needed a more streamlined solution. The primary motivator was the development environment on macOS, where porting from .NET 4.x would be more time-consuming than creating this focused translator.

## Note to Users

This is a personal project, designed primarily for my needs. If you're seeking a comprehensive solution, you might want to explore other options. But if you're looking for a quick and efficient translator that works seamlessly on macOS, give ResXTranslator a try!

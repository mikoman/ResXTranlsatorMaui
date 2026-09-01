# ResXTranslator

ResXTranslator is a focused, native utility for translating `.resx`, CSV, and Excel localization files into languages and regional variants from the platform's global culture catalog.

Built with **.NET 10** and **.NET MAUI**, and it runs as a native **Mac Catalyst** app on macOS.

## Features

- Uses an LLM through the [OpenRouter API](https://openrouter.ai/) with a fixed sports fan-engagement and ticketing localization context.
- Loads OpenRouter's compatible model catalog, supports model search and pricing comparison, and stores the chosen model for the next run.
- Provides searchable world-language selection, including neutral languages and regional/script variants such as French (Canada) (`fr-CA`) and English (Singapore) (`en-SG`).
- Stores the OpenRouter API key in the platform's secure credential storage rather than application preferences (the login Keychain on macOS).
- Exports translations to Excel using the MIT-licensed [ClosedXML library](https://github.com/ClosedXML/ClosedXML).
- Imports translation audit documents from CSV or Excel (`.xlsx`) and adds new language/value and status columns without overwriting the source.
- Exports spreadsheet translations to either Excel or CSV while preserving the original column order and multiline CSV values.
- Support for multiple languages (easy to extend).
- Translates either one source file or every source `.resx` found recursively in a chosen folder.
- Translations are bounded by both item count (up to 50 strings) and payload size, returned as validated structured output, and shown with animated file/language/batch progress, elapsed time, and token usage.
- Output files are written next to the source `.resx` file, with no machine-specific paths.

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

1. Under **OpenRouter**, open **Manage…**, paste an [OpenRouter API key](https://openrouter.ai/keys), and connect. The validated key is saved in the platform's secure credential store and is never shown again after the sheet closes.
2. Choose **Model…**, search the live structured-output model catalog, review its input/output token prices, and select a model.
3. Choose or drag in a `.resx`, `.xlsx`, or `.csv` file, or choose a folder to recursively translate every source `.resx` inside it. Generated localized RESX files are ignored when that folder is scanned again. Spreadsheet files must follow the translation audit layout with a `Default` column and adjacent language/status pairs such as `fr` and `fr Status`.
4. Choose one target language. Search by language, country or region, native name, or culture code. The selected display name and BCP-47 code are both sent to the model; the code is used for the RESX filename or spreadsheet column.
5. Click **Translate**. The LLM translates each complete resource string with sports fan-engagement and ticketing context while preserving placeholders and markup. The animated status identifies the current file, language, batch, completed entry count, model wait, and elapsed time. RESX results are written as `<SourceName>.<culture>.resx` (for example `AppResources.pt-PT.resx`) beside each originating file. CSV/Excel input produces a non-overwriting `.translated` copy.
6. **Export to Excel** or **Export to CSV** writes the currently loaded data to the selected format in the same output folder.

## Dependencies

ResXTranslator uses these NuGet packages:
- [ClosedXML](https://github.com/ClosedXML/ClosedXML) for reading and writing `.xlsx` workbooks. ClosedXML is distributed under the MIT license.
- OpenRouter integration and CSV support use .NET platform APIs and add no extra package dependencies.

## Inspiration

This project is inspired by some great tools available on GitHub:

- [resxtranslator by HakanL](https://github.com/HakanL/resxtranslator)
- [ResxTranslator by stevencohn](https://github.com/stevencohn/ResxTranslator)
- [Resx-Translator by DamienDoumer](https://github.com/DamienDoumer/Resx-Translator)

However, while the existing tools offer a wide array of features, I needed a more streamlined solution. The primary motivator was the development environment on macOS, where porting from .NET 4.x would be more time-consuming than creating this focused translator.

## Note to Users

This is a personal project, designed primarily for my needs. If you're seeking a comprehensive solution, you might want to explore other options. But if you're looking for a quick and efficient translator that works seamlessly on macOS, give ResXTranslator a try!

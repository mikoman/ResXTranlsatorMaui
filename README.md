# ResXTranslator

ResXTranslator is a straightforward and UI-based .resx file translator designed for simplicity and efficiency. While its core functionality is tailored to specific needs, it can be effortlessly adapted to support a broader range of languages or additional features. The design is basic barely enough to be functional. Code structure is also hacky as it was done for a quick translation. You can change the list of languages using the CultureInfo class to populate the picker if you need to do so. 

## Features

- Utilizes the **DeepL API** for translations using the [DeepL API .NET library](https://www.nuget.org/packages/DeepL/).
- Exports translations to an Excel sheet using the [EPPlus library](https://www.nuget.org/packages/EPPlus/).
- Support for multiple languages (easy to extend).

🔜 **Coming Soon:** Google Translate API integration!

## Inspiration

This project is inspired by some great tools available on GitHub:

- [resxtranslator by HakanL](https://github.com/HakanL/resxtranslator)
- [ResxTranslator by stevencohn](https://github.com/stevencohn/ResxTranslator)
- [Resx-Translator by DamienDoumer](https://github.com/DamienDoumer/Resx-Translator)

However, while the existing tools offer a wide array of features, I needed a more streamlined solution. The primary motivator was the development environment on MacOS, where porting from .NET 4.x to .NET 6 would be more time-consuming than creating this focused translator.

## Note to Users

This is a personal project, designed primarily for my needs. If you're seeking a comprehensive solution, you might want to explore other options. But if you're looking for a quick and efficient translator that works seamlessly on MacOS, give ResXTranslator a try!

## Dependencies

ResXTranslator uses these two NuGet packages:
- [DeepL API .NET library](https://www.nuget.org/packages/DeepL/) for smooth integration with the DeepL translation service.
- [EPPlus](https://www.nuget.org/packages/EPPlus/) for generating and managing Excel spreadsheets in .NET.

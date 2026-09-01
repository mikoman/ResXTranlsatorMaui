# ResXTranslator design contract

## Direction

ResXTranslator is a compact native utility, not a dashboard or a web form. Its visual language follows Apple's grouped settings surfaces: semantic system-like backgrounds, inset row cards, hairline separators, SF Symbols, restrained system typography, and one violet primary action. The Mac Catalyst window remains `620×476`; long content and focused setup tasks scroll inside the window.

The main-page story is source → OpenRouter account → compatible model → target language → Translate → progress/result. API-key management, model search, and world-language search remain separate modal sheets so credentials and catalog complexity do not clutter the primary workflow.

## Visual rules

- Use the semantic light/dark tokens in `Resources/Styles/Colors.xaml`; small light-theme secondary and error text must maintain at least 4.5:1 contrast.
- Reserve the violet fill for the primary action. Plain violet actions handle choosing, managing, refreshing, and revealing.
- Use SF Symbols through `SymbolImage`; do not introduce a second icon family.
- Cards use 10pt rounded corners, one-pixel semantic separators, and grouped-list row spacing.
- Text fields use `FieldWell`, including the violet focus ring. Search uses a native `Entry` inside that well to avoid Catalyst's off-style `UISearchBar` cancel treatment.
- Target language is an explicit single selection from the platform culture catalog. The searchable sheet exposes neutral languages plus regional/script variants and their BCP-47 codes; there is deliberately no potentially costly “all world languages” action.
- Progress is both determinate and active: the label reports file, language, batch, entry count, model wait, and elapsed time. The pulse becomes static when Reduce Motion is enabled.
- Every action remains keyboard focusable and has an accessible description where its visible label is not sufficient.

## Source and result behavior

- A single RESX, CSV, or XLSX can be selected. A folder selection recursively discovers source RESX files, ignores generated culture-code outputs, and writes each localized file beside its originating RESX.
- Folder and file actions stay explicit (`Choose File…`, `Choose Folder…`, `Change Folder…`).
- Result paths are plain native actions that reveal their output in the platform file manager.
- Sheets provide explicit Cancel controls; removing a stored credential requires confirmation.

## Runtime evidence and asset provenance

The bounded finish review used cropped screenshots from a locally built, ad-hoc-signed Mac Catalyst Debug app on 1 September 2026. Captures covered the main folder state, connected account sheet, searchable live model catalog, and an OpenRouter error state. They were kept in `/tmp` for review and are not shipping assets or repository content. iOS and Android were compile-validated only; no device-level visual claim is made for them.

This feature adds no generated images or raster assets. Existing app icon, splash, and `dotnet_bot.svg` files are inherited .NET MAUI project vectors already present in the repository; they were not created or modified as part of the OpenRouter/folder work. The runtime interface is rendered from XAML, semantic colors, native controls, and SF Symbols.

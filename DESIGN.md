# ResXTranslator design contract

## Direction

ResXTranslator is a compact native utility, not a dashboard or a web form. Its visual language follows Apple's grouped settings surfaces: semantic system-like backgrounds, inset row cards, hairline separators, SF Symbols, restrained system typography, and one violet primary action. The Mac Catalyst window uses a roomier `720×680` working size so translation progress remains visible without routine scrolling; unusually long content and focused setup tasks still scroll inside the window.

The main-page story is source → OpenRouter account → compatible model → target language → Translate → progress/result. API-key management, model search, translation settings, and world-language search remain separate modal sheets so credentials and configuration complexity do not clutter the primary workflow.

## Visual rules

- Use the semantic light/dark tokens in `Resources/Styles/Colors.xaml`; small light-theme secondary and error text must maintain at least 4.5:1 contrast.
- Reserve the violet fill for the primary action. Plain violet actions handle choosing, managing, refreshing, and revealing.
- Use SF Symbols through `SymbolImage`; do not introduce a second icon family.
- Cards use 10pt rounded corners, one-pixel semantic separators, and grouped-list row spacing.
- Text fields use `FieldWell`, including the violet focus ring. Search uses a native `Entry` inside that well to avoid Catalyst's off-style `UISearchBar` cancel treatment.
- Target language is an explicit single selection from the platform culture catalog. The searchable sheet exposes neutral languages plus regional/script variants and their BCP-47 codes; there is deliberately no potentially costly “all world languages” action.
- Progress is both determinate and active: the title reports the number of active OpenRouter requests, a stable right-aligned timer reports whole-operation elapsed time, and supporting text moves through request sent, provider connected, response streaming, validation, and file-writing stages. It includes completed entries, completed and total batches with a percentage, per-batch retry attempts, request IDs shared with the redacted diagnostics log, streamed response size, and per-request elapsed time. The final whole-operation duration is repeated in success, cancellation, failure, and diagnostics. The pulse becomes static when Reduce Motion is enabled.
- Concurrent progress remains one compact status group: the title reports active-request count, the summary reports entries/batches and the effective limit, and up to four short request lines expose file, batch, phase, response size, and elapsed time. Higher concurrency is summarized with an additional active-request count instead of expanding the main surface indefinitely.
- Long-running translation exposes plain `Cancel Translation` and `Open Diagnostics Log` actions. Cancellation is immediate, and no partial current-batch output is applied.
- Every action remains keyboard focusable and has an accessible description where its visible label is not sufficient.

## Source and result behavior

- A single RESX, CSV, or XLSX can be selected. A folder selection recursively discovers source RESX files, ignores generated culture-code outputs, and writes each localized file beside its originating RESX.
- A global round-robin work queue interleaves batches across files and uses spare slots for additional batches from a large file. Free and unknown/variable-price models use two concurrent requests; paid models use a persistent 1–10 preference that defaults to four. A provider-specific malformed response retries the same batch through up to five distinct providers by excluding only that batch's previous routes; providers are never globally denied. Network results remain in memory until the entire queue validates, preventing partial output from a failed parallel run.
- Translation settings use an explicit draft, Save, and Cancel flow. Domain-and-tone guidance defaults to the sports/ticketing brief and can be edited manually or generated with one potentially billable call to the selected model. The generator's prompt and the translation rules for locale selection, placeholders, markup, untrusted source data, and exact IDs remain immutable.
- Folder and file actions stay explicit (`Choose File…`, `Choose Folder…`, `Change Folder…`).
- Result paths are plain native actions that reveal their output in the platform file manager.
- Sheets provide explicit Cancel controls; removing a stored credential requires confirmation.

## Runtime evidence and asset provenance

The bounded finish review used cropped screenshots from a locally built, ad-hoc-signed Mac Catalyst Debug app on 1 September 2026. Captures covered the main folder state, connected account sheet, searchable live model catalog, and an OpenRouter error state. They were kept in `/tmp` for review and are not shipping assets or repository content. iOS and Android were compile-validated only; no device-level visual claim is made for them.

The app icon, splash mark, and README mark share a custom localization-prism identity: two structured message sheets cross from source to target around a transformation spark. The production artwork is deterministic SVG, uses the same deep-indigo foundation as the interface, and avoids platform- or provider-specific branding. The runtime interface is rendered from XAML, semantic colors, native controls, and SF Symbols.

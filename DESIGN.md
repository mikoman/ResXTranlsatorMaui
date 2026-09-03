# ResXTranslator design contract

## Direction

ResXTranslator is a compact native desktop utility, not a dashboard or a web form. Its visual language uses semantic system-like backgrounds, inset row cards, hairline separators, native platform symbols, restrained system typography, and one violet primary action. Mac Catalyst and Windows use a roomier `720×680` working size so translation progress remains visible without routine scrolling; unusually long content and focused setup tasks still scroll inside the window.

The main-page story is source → LLM provider → connection → tested model → target language → Translate → progress/result. Provider selection, credential/endpoint management, model search/testing, translation settings, and world-language search remain separate modal sheets so configuration complexity does not clutter the primary workflow.

## Visual rules

- Use the semantic light/dark tokens in `Resources/Styles/Colors.xaml`; small light-theme secondary and error text must maintain at least 4.5:1 contrast.
- Reserve the violet fill for the primary action. Plain violet actions handle choosing, managing, refreshing, and revealing.
- Use `SymbolImage` for SF Symbols on Mac Catalyst and mapped Segoe Fluent Icons on Windows; do not introduce a third icon family.
- Cards use 10pt rounded corners, one-pixel semantic separators, and grouped-list row spacing.
- Text fields use `FieldWell`, including the violet focus ring. Search uses a native `Entry` inside that well to avoid Catalyst's off-style `UISearchBar` cancel treatment.
- Target language is an explicit single selection from the platform culture catalog. The searchable sheet exposes neutral languages plus regional/script variants and their BCP-47 codes; there is deliberately no potentially costly “all world languages” action.
- Progress is both determinate and active: the title reports the number of active requests for the selected provider, a stable right-aligned timer reports whole-operation elapsed time, and supporting text moves through request sent, provider connected, response streaming, validation, retry, and file-writing stages. It includes completed entries, completed and total batches with a percentage, per-batch retry attempts, request IDs shared with the redacted diagnostics log, streamed response size, and per-request elapsed time. The final whole-operation duration is repeated in success, cancellation, failure, and diagnostics. The pulse becomes static when Reduce Motion is enabled.
- Concurrent progress remains one compact status group: the title reports active-request count, the summary reports entries/batches and the effective limit, and up to four short request lines expose file, batch, phase, response size, and elapsed time. Higher concurrency is summarized with an additional active-request count instead of expanding the main surface indefinitely.
- Long-running translation exposes plain `Cancel Translation` and `Open Diagnostics Log` actions. Cancellation is immediate, and no partial current-batch output is applied.
- Every action remains keyboard focusable and has an accessible description where its visible label is not sufficient.

## Source and result behavior

- A single RESX, CSV, or XLSX can be selected. A folder selection recursively discovers source RESX files, ignores generated culture-code outputs, and writes each localized file beside its originating RESX.
- A global round-robin work queue interleaves batches across files and uses spare slots for additional batches from a large file. Every provider has a persistent 1–10 concurrency preference (cloud default four, local default one); OpenRouter free/unknown-price models retain a two-request cap. Each batch's strict schema defines the exact requested IDs as required translation properties, and the compatibility probe verifies non-sequential IDs before a model becomes usable. Direct requests carry a batch-sized output-token allowance; the larger client character guard is retained only as protection against an endpoint that ignores that allowance. Direct adapters retry connection failures and HTTP 408/429/5xx at most twice after the initial request, honoring `Retry-After`; they do not retry after cloud response content or malformed output. Local presets may discard one oversized or incomplete response and recover that batch once as two smaller sequential requests. OpenRouter alone may retry a failed batch through up to five distinct internal routes. No request ever switches to another configured provider. Network results remain in memory until the entire queue validates, preventing partial output from a failed parallel run.
- API keys are stored separately in secure storage. Ordinary preferences contain only active provider, endpoint, model, strict-schema compatibility fingerprint, concurrency, and global domain instructions. Provider switching restores its own non-secret state.
- Ollama and LM Studio are desktop presets with editable loopback/private-network endpoints. Mac Catalyst declares local-network access and a narrow ATS local-network exception; Windows runs unpackaged and declares packaged LAN capabilities for future distribution. The app never discovers or manages inference servers.
- Translation settings use an explicit draft, Save, and Cancel flow. Domain-and-tone guidance defaults to the sports/ticketing brief and can be edited manually or generated with one potentially billable call to the selected model. The generator's prompt and the translation rules for locale selection, placeholders, markup, untrusted source data, and exact IDs remain immutable.
- Folder and file actions stay explicit (`Choose File…`, `Choose Folder…`, `Change Folder…`).
- Result paths are plain native actions that reveal their output in the platform file manager.
- Sheets provide explicit Cancel controls; removing a stored credential requires confirmation.

## Runtime evidence and asset provenance

The original bounded finish review used cropped screenshots from a locally built, ad-hoc-signed Mac Catalyst Debug app on 1 September 2026. Those captures predate multi-provider and Windows support and are not shipping assets or repository content. Windows requires separate build and runtime validation on a Windows host.

The app icon, splash mark, and README mark share a custom localization-prism identity: two structured message sheets cross from source to target around a transformation spark. The production artwork is deterministic SVG, uses the same deep-indigo foundation as the interface, and avoids platform- or provider-specific branding. The runtime interface is rendered from XAML, semantic colors, native controls, and SF Symbols.

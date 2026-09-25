# Uword

Windows selection translator. A selection marker appears after selecting text; hover over it for one second to translate with a user-configured OpenAI-compatible chat completions endpoint.

## Structure

- `src/Uword.App/Input`: low-level mouse hook and selection gesture classification.
- `src/Uword.App/Selection`: isolated UI Automation query process and selection data contract.
- `src/Uword.App/Workflow`: current-selection state, timeout, hover timer, and request cancellation.
- `src/Uword.App/Translation`: settings, DPAPI-encrypted API key, prompt templates, paragraph batching, HTTP client, rate limiting.
- `src/Uword.App/Overlay`: non-activating marker and translation/original-text preview.
- `src/Uword.App/Diagnostics`: tray-accessible status window.
- `src/Uword.App/Interop`: Win32 declarations.
- `tests/Uword.Tests`: deterministic gesture and bounds checks.

## Build and run

Requires Windows and the .NET 10 SDK. From the repository root:

```powershell
dotnet build Uword.slnx
dotnet run --project src/Uword.App
dotnet run --project tests/Uword.Tests
dotnet run --project tests/Uword.Tests -- --integration
```

To create a standalone Windows x64 build (no .NET runtime installation needed):

```powershell
dotnet publish src/Uword.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/win-x64
```

Run `artifacts/win-x64/Uword.App.exe`. Keep `THIRD_PARTY_NOTICES.md` with redistributed builds.

The app starts in the notification area. Right-click its tray icon and open **翻译设置**. Fill in Base URL (for example `https://api.openai.com/v1`), API Key, model, and target language. Adjust temperature (default 0), prompts, requests per second (default 2), maximum text length (default 1200 characters), and maximum paragraphs (default 4). Click **测试连接** to make a sample API call, then **保存设置**. A full `/chat/completions` URL also works. HTTPS is required except for loopback HTTP endpoints.

Double-click the tray icon for status; right-click to pause selection or exit. Drag-select or double-click a word in another application. If UI Automation exposes selected text, a green marker appears near the selection. Hover over it for one second to send the selected text to the configured endpoint. The preview shows the translation and the original text. Click outside or close the preview to cancel the current translation and dismiss it.

The client sends a non-streaming `POST /chat/completions` with system and user messages. Prompt variables `{{to}}` and `{{text}}` are supported; optional placeholders from the reference prompt are deliberately not implemented. Multi-paragraph batches use a standalone `%%` separator and are validated against the source paragraph count. A malformed multi-paragraph response falls back to individual requests. Oversized paragraphs are split at a nearby sentence/word boundary when possible; translated fragments are joined with a space. Each request respects the configured source length and paragraph count, and the global rate limit also applies to retries and connection tests. HTTP 429 and 5xx failures are retried at most twice; an unsupported temperature is reported as an API error, not silently removed.

Settings are stored under `%LOCALAPPDATA%\Uword`. The API Key is stored separately using Windows DPAPI for the current user. Text and responses are not written to diagnostics. Text leaves the machine only on marker hover or an explicit connection test; the connection test sends a short fixed sample. The clipboard is never changed.

The initial implementation detects mouse selections only. Each candidate launches a short-lived copy of Uword to query UI Automation; a crashing or blocked accessibility provider cannot terminate the tray process. Applications without a UI Automation text selection do not show a marker. Elevated applications, secure desktops, and some custom-rendered editors are not guaranteed to work. A slow provider may time out; no clipboard fallback is enabled. Use the status window to distinguish an empty selection from an unsupported text pattern or timeout.

The first query for each source process has a 4-second budget; later queries have a 1.5-second budget. An empty selection or missing text pattern is retried twice with short delays. The status window can open `%LOCALAPPDATA%\Uword\diagnostics.log`, which contains process name, gesture, each attempt's outcome, timing, and translation failure category, but never selected text, API keys, or API response bodies. The log is truncated after 1 MB.

## Manual compatibility check

For each target application, test a drag selection, a double-click selection, an ordinary click, and a selection near a screen edge. Repeat on a second monitor or at another display scale when available. Record whether the marker appeared, whether the translation and original text match expectations, and whether the original application's focus and clipboard remained unchanged. Test API errors and selecting new text before the prior request finishes.

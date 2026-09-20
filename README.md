# Quillvora for Windows

Formerly SourceScribe. Existing settings and recovery continue to use `%LOCALAPPDATA%\SourceScribe` for compatibility. Launch using `Quillvora.lnk` or `Start Quillvora.cmd`. See [User manual](USER_MANUAL.md) and [Technical handoff](HANDOFF.md).

A native Windows desktop transcription app with independently selectable PC playback, microphone/line-in and NDI audio, local speech recognition, and an AI summary workspace.

## Run

Open **dist/Quillvora/Quillvora.exe**. Keep the entire folder together; it includes the runtime libraries and a multilingual Tiny Whisper model. The portable build includes .NET, so no Python or .NET installation is needed.

Requirements: Windows x64 (Windows 11 recommended), Microsoft Visual C++ 2022 x64 runtime, and an AVX-capable CPU for the bundled Whisper runtime. NDI capture additionally requires the separately installed x64 NDI Runtime. This computer already has NDI 6 Runtime installed. The app does not redistribute NDI.

## Use

1. Choose the audio sources on the left. PC audio captures an entire playback endpoint, not a specific application. Microphones, line-in and audio interfaces appear as Windows recording endpoints. NDI sources appear after **Refresh**; start the sender first. **Select all** toggles all discovered sources.
2. Press **Start capture**. The transcript receives timestamped, source-labelled lines as each audio block is recognized. The default block is 8 seconds; recognition time adds latency. Settings allow 4–20 seconds. This is near-live block transcription, not word-by-word streaming.
3. **Save all so far** saves a text snapshot without interrupting capture. Highlight any transcript portion and choose **Save selection**. File → **Save session** preserves timestamps and all summary blocks as JSON. **Stop & finish** drains queued audio before returning to idle.
4. Configure a summary provider under **Settings**. Choose **All transcript so far**, **Selected transcript text**, or **Last 10 minutes**, then enter a prompt. For example: `Summarize last 10 minutes` or `Summarize the section talking about the budget, including decisions and open questions`. Numeric last/past/previous seconds, minutes and hours are filtered locally; topic requests are matched by the AI against the supplied context. A selected-text scope takes precedence over a time expression; the explicit Last 10 minutes scope always uses ten minutes.
5. Generate **Prose** or a **Slide outline**. Each result becomes a separate summary block. Settings let you request four or five points per slide. Sparse evidence may yield fewer points instead of invented filler.
6. Ctrl-click summary blocks and **Export selected PPTX**, or **Export all** to combine all summaries into one presentation. Existing slide outlines export locally. Prose blocks are converted into outlines using the configured AI before export. File → Summaries also offers saving the displayed summary text. PowerPoint itself is not required for export.

Summaries are snapshots: capture continues, and new lines are not added to an in-flight summary. Time ranges are relative to the session's capture clock, including silence; stopping capture pauses that clock. Pending audio may not yet have transcript lines. Stop and drain the queue before generating a final complete session summary. Device labels identify sources, not individual speakers.

## AI providers

| Provider | Default base URL | Setup |
| --- | --- | --- |
| OpenAI | `https://api.openai.com/v1` | API key and an available Responses API model |
| Gemini | `https://generativelanguage.googleapis.com/v1beta` | Google AI API key and model name |
| Claude | `https://api.anthropic.com/v1` | Anthropic API key and model name |
| Ollama | `http://localhost:11434` | Start Ollama, install a model, enter its exact name |
| LM Studio | `http://localhost:1234/v1` | Load a model, start its local server, enter the model identifier and token if enabled |

Model names are editable examples, not guarantees of account availability. Chat product subscriptions do not configure these API credentials. No cloud API key is bundled. Remote base URLs require HTTPS; HTTP is allowed for localhost. Large transcripts are summarized in successive blocks and then synthesized; local models still need sufficient context capacity (approximately 16K tokens is a useful starting point). Longer inputs require multiple provider requests.

## Data and recovery

- Speech recognition is local. Raw audio lives in bounded memory buffers and is not recorded to disk.
- Cloud summaries send only the scoped transcript/evidence and prompt to your configured provider. Local AI servers keep summary inference on that server. No automatic cloud fallback is used.
- API keys are encrypted with Windows DPAPI for the current user. Settings and recovery files live in `%LOCALAPPDATA%\SourceScribe`. Transcripts and summaries are plain text/JSON, not encrypted.
- Recovery autosaves every 10 seconds and on stop. Opening/replacing a session archives the previous session under `Sessions` in the same directory. Startup offers to restore the latest recovery.
- A bounded 120-block queue limits memory use. The status bar reports queue depth and dropped blocks. If recognition falls behind, use Tiny, fewer sources, or a faster computer. Capture or inference errors are shown in the status bar.

## Practical limits

Use headphones if capturing PC playback and a nearby microphone; otherwise the microphone may record the playback again. Independent sources are not echo-cancelled or speaker-diarized. Fixed block boundaries can split words; very quiet speech may be skipped. Tiny prioritizes speed over accuracy; Settings can download Base or load a compatible multilingual GGML model. Review names, numbers, and generated summaries before relying on them. NDI discovery depends on the sender and network/firewall configuration; manual remote discovery IP entry is not included.

## Build and test

Requires the .NET 8 SDK or later:

```powershell
dotnet restore Transcriber/Transcriber.csproj --packages .packages
dotnet build Transcriber/Transcriber.csproj -c Release --no-restore
dotnet restore Transcriber.Tests/Transcriber.Tests.csproj --packages .packages
dotnet run --project Transcriber.Tests/Transcriber.Tests.csproj -c Release --no-restore
```

The test harness exercises scope boundaries, source ordering, audio downmix/resampling, stop flushing, silence handling, session round trips, encrypted settings, all five provider HTTP contracts with fake responses, multi-block summarization, and Open XML schema validation of combined PowerPoint exports. `--hardware` checks device/NDI discovery; `--ndi` tests synthetic audio through the installed NDI 6 sender and receiver; `--speech` uses `models/ggml-tiny.bin` and `TestResults/jfk.wav`; `--render` renders the actual WPF layout offscreen. Live cloud billing/authentication and local LLM generation require user-configured keys/servers and are not covered by mocked contract tests.

Run `build.ps1` to publish a self-contained Windows x64 folder and copy the speech model if present. No installer or code-signing certificate is included.

## Implementation references

- [NAudio](https://github.com/naudio/NAudio): WASAPI input and loopback capture, sample-rate conversion.
- [Whisper.net](https://github.com/sandrohanea/whisper.net): local speech inference and GGML model download.
- [NDI receiver API](https://docs.ndi.video/all/developing-with-ndi/sdk/ndi-recv): native audio-only reception.
- [OpenAI Responses](https://developers.openai.com/api/docs/quickstart), [Gemini generateContent](https://ai.google.dev/api/generate-content), [Claude Messages](https://platform.claude.com/docs/en/api/messages/create), [Ollama chat](https://docs.ollama.com/api/chat), [LM Studio compatibility](https://lmstudio.ai/docs/developer/openai-compat): summary integration contracts.

Third-party dependencies and models retain their own licences. See `THIRD_PARTY.md`.

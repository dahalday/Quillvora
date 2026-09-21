# Quillvora — Technical Handoff

Prepared: 20 September 2026  
Project version: 0.1.0  
Workspace: `F:\GPT programs\Transcriber`

## 1. Purpose and current state

Quillvora is a native Windows desktop application for capturing audio from independently selected Windows playback devices, recording devices, and NDI sources. Whisper runs locally to produce timestamped, source-labelled transcripts. Users can generate prose summaries or slide outlines through a configured AI provider, save sessions, and export combined PowerPoint presentations.

The portable Windows x64 build is at `dist\Quillvora\Quillvora.exe`. It has been rebuilt with the window-closing fix and embedded microphone icon described below. The project-root `Quillvora.lnk` shortcut uses the executable's embedded icon and targets this portable executable. `Start Quillvora.cmd` is an alternative launcher. The installed copy at `C:\Program Files\Quillvora\Quillvora.exe` was also updated on 20 September 2026; see section 11.

## 2. Project map

| File or folder | Responsibility |
| --- | --- |
| `Transcriber/MainWindow.xaml` | Main WPF layout and event wiring |
| `Transcriber/Assets/Quillvora.ico` | Multi-resolution microphone icon embedded in the executable and WPF resources |
| `Transcriber/MainWindow.xaml.cs` | Capture controls, session clock, summaries, exports, recovery, closing |
| `Transcriber/AudioCapture.cs` | Windows endpoint enumeration, WASAPI capture, downmixing, buffering, resampling |
| `Transcriber/NdiCapture.cs` | NDI native interop, discovery, audio reception |
| `Transcriber/TranscriptionEngine.cs` | Whisper model loading, bounded work queue, recognition, model downloads |
| `Transcriber/AiService.cs` | Five provider HTTP integrations, transcript reduction, slide JSON parsing |
| `Transcriber/PowerPointExport.cs` | Local 16:9 PPTX generation through Open XML |
| `Transcriber/Models.cs` | Transcript/session records, time scope filtering, JSON storage |
| `Transcriber/Settings.cs` | Defaults, settings persistence, Windows DPAPI key protection |
| `Transcriber/SettingsWindow.cs` | Model, language, provider, and slide preferences |
| `Transcriber/App.xaml` and `App.xaml.cs` | Shared styles, application startup, unhandled UI exception display |
| `Transcriber.Tests/Program.cs` | Console test harness and optional hardware/render checks |
| `build.ps1` | Self-contained publish, bundled model and licence copying |
| `create-shortcut.ps1` | Creates a project-root Windows shortcut using the embedded executable icon |
| `verify-slides.ps1` | Optional PowerPoint COM rendering of the sample deck |
| `models/` | Source speech model files |
| `dist/Quillvora/` | Portable application and its runtime dependencies |
| `TestResults/` | Generated test profile, sample presentation, reference audio, previews |
| `Errors/` | Reported error screenshot |
| `VALIDATION.md` | Historical first-build verification record |
| `THIRD_PARTY.md`, `Licenses/` | Third-party notices |

## 3. Architecture and behavior

Each selected device has an independent audio buffer. Windows PCM or NDI float audio is downmixed to mono and resampled to 16 kHz before recognition. The default block length is eight seconds. Very short or near-silent buffers are skipped. Idle gaps and stopping capture flush partial buffers.

`TranscriptionEngine` feeds a single Whisper worker through a queue capped at 120 blocks. The capture callback uses `TryWrite`; a full queue drops the new block, increments the dropped counter, and reports overload. Each recognition result is dispatched to the UI with source and time information. UI arrival order can differ across sources; formatted exports and summary context sort by start time.

The capture clock accumulates across stop/start cycles within a session. It includes silence while capture is active. Time filtering selects transcript segments overlapping the requested range; it does not trim words within a segment. Selected-text scope overrides time expressions, and the explicit ten-minute scope overrides the prompt's time expression.

AI summaries use a snapshot of the scoped text. Inputs longer than 18,000 characters are split and reduced through successive requests before synthesis. Provider defaults and request formats are implemented in `Settings.cs` and `AiService.cs`; they are not a guarantee of current external model availability. HTTP is permitted only for loopback URLs; remote provider URLs must use HTTPS.

Slide outlines are parsed and checked for structural and text-length limits. PPTX generation is local for existing outlines. Exporting prose first sends that prose to the selected AI provider to obtain an outline. PowerPoint is not required for generation.

## 4. Recent shutdown fix

Reported screenshot: `Errors/Screenshot 2026-09-20 184629.png`.

The error says that `Close` and certain window operations cannot run while a window is closing. The original `Window_Closing` handler cancelled the initial event, awaited `StopCapture()`, and immediately called `Close()`. With no active capture, `StopCapture()` returned synchronously and the second close happened inside the first closing event.

The updated handler:

1. Cancels the initial event and rejects duplicate shutdown requests using `closeRequested`.
2. Uses `await Dispatcher.Yield(DispatcherPriority.Background)` to return control from the original event before cleanup continues.
3. Cancels an AI request, drains capture, saves recovery, and stops the timer.
4. Sets `closing` so the final `Close()` can proceed.
5. Resets the closing state and restarts the timer if cleanup throws.

When capture startup or shutdown is already busy, the handler asks the user to wait and try closing again. AI cancellation is requested but the handler does not explicitly await the AI operation's completion. Keep that distinction in mind when testing shutdown behavior.

The source fix and portable rebuild are complete. No dedicated automated closing regression check or manual closing interaction was performed during this change.

## 5. Build and packaging

Build on Windows with the .NET 8 SDK or a compatible newer SDK. Run these commands from the project root:

```powershell
dotnet restore Transcriber/Transcriber.csproj --packages .packages
dotnet build Transcriber/Transcriber.csproj -c Release --no-restore
dotnet restore Transcriber.Tests/Transcriber.Tests.csproj --packages .packages
dotnet run --project Transcriber.Tests/Transcriber.Tests.csproj -c Release --no-restore
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\create-shortcut.ps1
```

The process-scoped execution-policy argument allows these scripts to run without changing the machine's persistent PowerShell policy. Close Quillvora before publishing if its files are locked.

`build.ps1` publishes a self-contained `win-x64` application, copies `models/ggml-tiny.bin` if present, and includes the README and third-party notices. It does not download a missing speech model or recreate the shortcut. Run `create-shortcut.ps1` after a clean publish or after moving the project; the generated shortcut stores absolute paths. Its icon comes from the executable. The source icon is `Transcriber/Assets/Quillvora.ico`, embedded as both the application icon and a WPF resource for the title bar.

Distribute the entire `dist/Quillvora` folder, not the executable alone. The build script includes the user manual, technical handoff, and validation record in the portable folder. No installer or code signing is configured.

Runtime requirements documented for this build are Windows x64, the Microsoft Visual C++ 2022 x64 runtime, and an AVX-capable CPU. The portable package includes .NET. NDI additionally needs the separately installed x64 NDI Runtime; it is not redistributed.

## 6. Dependencies

| Component | Project version |
| --- | --- |
| Target framework | `net8.0-windows`, WPF |
| NAudio | 2.2.1 |
| Whisper.net and Whisper.net.Runtime | 1.9.1 |
| DocumentFormat.OpenXml | 3.3.0 |
| System.Security.Cryptography.ProtectedData | 8.0.0 |

The bundled model is multilingual Tiny in GGML format. NDI is loaded dynamically from runtime locations; NDI 6 was recorded as installed on the development computer. See the licence files before redistributing dependencies.

## 7. Verification status

| Check | Evidence and scope |
| --- | --- |
| Release build after shutdown fix | Passed; zero warnings and zero errors |
| Existing core harness after shutdown fix | All 29 checks passed |
| Portable publish after shutdown fix | Completed successfully |
| Latest portable publish after icon change | Passed; NU1900 warning because NuGet vulnerability metadata was unavailable |
| Latest harness with `--render` after icon change | All 29 core checks plus WPF offscreen rendering passed (30 total) |
| Shortcut and executable icon | Shortcut regenerated using the executable icon; Windows extracted a 32×32 icon from the published executable |
| Installed copy | Updated EXE, DLL, PDB, and handoff; SHA-256 hashes matched the portable build |
| Real Whisper speech, endpoint discovery, synthetic NDI | Reported passed in the earlier `VALIDATION.md`; not rerun for the shutdown or icon changes |
| Live cloud authentication/billing and local LLM generation | Not verified against real configured providers |
| Close while idle, recording, draining, or generating AI | Manual acceptance remains outstanding |

The 29 checks cover transcript scopes, ordering, text splitting, slide parsing, JSON persistence, DPAPI protection, audio conversion/resampling, partial-buffer flush, silence filtering, mocked provider contracts, multi-block reduction, and PPTX schema validation. Mocked requests do not prove live service compatibility.

Optional checks:

```powershell
dotnet run --project Transcriber.Tests/Transcriber.Tests.csproj -c Release --no-restore -- --hardware
dotnet run --project Transcriber.Tests/Transcriber.Tests.csproj -c Release --no-restore -- --speech
dotnet run --project Transcriber.Tests/Transcriber.Tests.csproj -c Release --no-restore -- --ndi
dotnet run --project Transcriber.Tests/Transcriber.Tests.csproj -c Release --no-restore -- --render
```

`--speech` needs `models/ggml-tiny.bin` and `TestResults/jfk.wav`. Hardware/NDI checks depend on the machine and runtime; the synthetic NDI test uses the NDI 6 DLL path in the harness. The render check creates `TestResults/app-preview.png`. `verify-slides.ps1` additionally requires installed PowerPoint.

For shutdown acceptance, launch the updated executable and close it while idle, after stopping, during capture, and during an AI request. Try a repeated close click while draining. Confirm no exception dialog, process exit, and recovery of the expected transcript on reopening. Also verify that an attempted close during startup leaves a usable window until startup completes.

## 8. Persistence and operational limits

Application data resides under `%LOCALAPPDATA%\SourceScribe`:

This legacy folder name is deliberately retained after the Quillvora rename so existing settings, encrypted keys, downloaded models, and recovery remain accessible. The JSON session format is unchanged. The internal `Transcriber` namespace and project directory also remain unchanged; the application assembly and executable are named `Quillvora`. The older `dist/SourceScribe` build may remain on disk, but the updated launcher and shortcut target `dist/Quillvora`.

- `settings.json`: preferences and per-provider DPAPI-encrypted keys.
- `recovery.json`: latest session recovery snapshot.
- `Sessions/`: archives written when replacing a nonempty session.
- `Models/`: speech models downloaded through Settings.

Storage writes JSON through a temporary file followed by replacement. Recovery runs every ten seconds when content is dirty and during stop/close. User-saved TXT, JSON, and PPTX files go to chosen locations. Transcripts and summaries are not encrypted; DPAPI keys are tied to the Windows user context. Raw audio is not saved, so lost or dropped audio cannot be retranscribed from a session file.

Known functional limits include block-based latency, possible split words at block boundaries, no speaker diarization or echo cancellation, no per-application playback capture, and no manual NDI discovery-IP entry. There is no audio-file import interface. Multiple selected sources increase recognition workload. Changing settings or replacing sessions is blocked during capture or AI work.

For the next maintainer, prioritize the shutdown acceptance checks above, a live provider smoke test with configured credentials, and an extended capture run on the intended hardware. Do not treat the first-build validation record as evidence that these outstanding scenarios have passed.

## 9. Related documents

- [User manual](USER_MANUAL.md)
- [Project overview](README.md)
- [Historical verification](VALIDATION.md)
- [Third-party components](THIRD_PARTY.md)

## 10. Embedded application icon

The existing microphone icon is now embedded in the executable and displayed beside the main window title. The shortcut script uses the executable icon, so it no longer generates a separate ICO. Portable publish succeeded; all 29 core checks plus the WPF offscreen render passed (30 total). Windows successfully extracted the published executable icon. NuGet vulnerability metadata was unavailable during verification (NU1900 warning); compilation and tests succeeded.

`Transcriber.csproj` sets `ApplicationIcon` to `Assets\Quillvora.ico` and includes that file as a WPF `Resource`. `MainWindow.xaml` sets `Icon="Assets/Quillvora.ico"` for the title bar beside “Quillvora · Live transcription”. The source asset is included in the project rather than generated during publishing. The title-bar appearance has not been manually inspected in a visible running window; the WPF construction/render check passed.

## 11. Installed copy update

On 20 September 2026, compared all portable-build files against `C:\Program Files\Quillvora`. Only `Quillvora.exe`, `Quillvora.dll`, `Quillvora.pdb`, and `HANDOFF.md` differed. Copied those four files with elevated write access and verified each destination against its source using SHA-256. No Quillvora process was running at the time of the check. The installed application was not launched during verification.

The project-root shortcut still targets the workspace portable build. To run the installed copy, use `C:\Program Files\Quillvora\Quillvora.exe`. Future publishes update only `dist/Quillvora`; copying a new build into Program Files is a separate step requiring write permission. Close the installed app before replacing its files. Existing ZIP archives in `dist/` were not rebuilt for the icon change; use the updated folder for the current build or recreate an archive before distribution.

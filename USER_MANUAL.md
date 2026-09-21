# Quillvora — User Manual

Version 0.1.0 · 20 September 2026

## 1. What Quillvora does

Quillvora turns live audio into timestamped text on your Windows computer. It can listen to PC playback, microphones or line-in devices, and available NDI audio sources. It can also turn the transcript into prose summaries and PowerPoint slide outlines using an AI provider you configure.

Speech recognition runs locally. Summaries use the selected cloud provider or local AI server. An AI provider is not required for transcription or saving transcripts.

## 2. Start the application

For a GitHub download, get **Quillvora-0.1.0-win-x64.zip** from [Releases](https://github.com/dahalday/Quillvora/releases/latest), extract the entire ZIP, then open **Quillvora/Quillvora.exe**. GitHub's **Code → Download ZIP** and **Source code** archives contain only the development files and cannot be launched directly.

In `F:\GPT programs\Transcriber`, double-click **Quillvora**, the shortcut with the microphone icon. You can also use **Start Quillvora.cmd** or open `dist\Quillvora\Quillvora.exe` directly.

Keep the whole `dist\Quillvora` folder together. The application needs the libraries and model files beside the executable. This portable build includes .NET; Python is not required. The documented prerequisites are Windows x64, Microsoft Visual C++ 2022 x64 runtime, and an AVX-capable CPU. NDI use additionally requires a separately installed x64 NDI Runtime.

If a recovery prompt appears, choose **Yes** to restore the most recent saved recovery snapshot. Choose **No** to begin without restoring it. Save important work explicitly as a session file rather than relying only on recovery.

## 3. Quick start

1. Select a microphone or PC playback device in **Audio sources**.
2. Confirm the **SPEECH & AI** area shows a speech model. If it says a model is needed, open **Settings** and choose or download one.
3. Click **Start capture** and speak or play the audio you want transcribed.
4. Wait for transcript lines. The default audio block is eight seconds, plus processing time.
5. Click **Save all so far** to save the current transcript as a text file.
6. Click **Stop & finish** and wait for queued audio to finish processing.
7. Use **File → Save session…** to save a reopenable session including the transcript and any summaries.

For a final summary of everything recorded, stop and finish capture before generating it.

## 4. Understand the window

| Area | Use |
| --- | --- |
| Top bar | Start capture, stop and finish, view session capture time |
| Audio sources | Select devices, refresh discovery, monitor input levels |
| Live transcript | Read timestamped text, select passages, save text |
| AI workspace | Choose context, enter instructions, generate prose or slide outlines |
| Summary blocks | Select one or more generated results and export them |
| Bottom status bar | Read capture messages, queued-block count, and dropped-block count |

Source labels name the audio device or NDI sender. They do not identify individual speakers.

## 5. Choose audio sources

**PC audio** captures everything played through the selected Windows playback endpoint. Choose the endpoint your media or meeting application is actually using. It does not select a single application.

**Mic / line in** captures a Windows recording endpoint, such as a microphone, line input, or audio interface. Check Windows input settings if your expected device is missing.

**NDI** captures audio from a discovered NDI sender. Start the sender first, then click **Refresh**. Discovery depends on the runtime, network, and firewall configuration. This version has no field for entering a remote discovery IP manually.

You may select several sources. **Select all** selects every discovered source; pressing it when all are selected clears the selection. Source selection and refresh are disabled while capturing.

Use headphones when recording both PC playback and a nearby microphone to reduce duplicate playback audio in the microphone. The app does not perform echo cancellation. Select only the sources you need to reduce processing load.

## 6. Configure speech recognition

Stop capture and finish or cancel AI work before opening **Settings**.

| Setting | What to enter or choose |
| --- | --- |
| Local Whisper model | Use the bundled Tiny model, browse to a compatible GGML `.bin` model, or download one |
| Download Tiny | Approximately 75 MB; prioritizes speed |
| Download Base | Approximately 142 MB; requires more processing capacity |
| Language | Use the spoken language's ISO code, such as `en`, or choose `auto` |
| Audio block length | Choose 4, 6, 8, 10, 15, or 20 seconds |

Click **Save settings** to apply your choices. Downloads require internet access. Keep Settings open until a download finishes; the app prevents closing that window during a download.

Shorter blocks can show updates sooner, but recognition still takes time and words may cross block boundaries. Quiet speech may be skipped. Review names, numbers, and technical terms in the transcript.

## 7. Capture and save text

Click **Start capture** after selecting at least one source. Transcript lines appear after blocks are recognized. **Follow live** scrolls toward incoming text; clear it when reviewing earlier lines.

**Save all so far** saves the text currently transcribed without stopping capture. **Save selection** saves only the highlighted transcript text. These text files are readable exports; they are not reopenable Quillvora sessions.

**Stop & finish** stops collection and drains queued audio. Wait until the status indicates processing has finished. Saving before the queue drains may omit speech that has not yet been recognized.

The queue counter indicates waiting audio blocks. If it keeps rising, recognition is falling behind. A full queue drops incoming blocks and increases the dropped counter. Those blocks cannot be recovered from the app. Stop capture, use Tiny or fewer sources, and start again when ready.

## 8. Configure AI summaries

Open **Settings**, select a provider, and enter the model name and connection details. The preset names are editable examples; use a model available on your account or server. Click **Save settings**.

| Provider | Default base URL in the app | Required setup |
| --- | --- | --- |
| OpenAI | `https://api.openai.com/v1` | API key and a compatible Responses API model |
| Gemini | `https://generativelanguage.googleapis.com/v1beta` | API key and model name |
| Claude | `https://api.anthropic.com/v1` | API key and model name |
| Ollama | `http://localhost:11434` | Running local server with the named model installed |
| LM Studio | `http://localhost:1234/v1` | Running local server with a loaded model; token if enabled |

Quillvora does not install or start Ollama or LM Studio for you. A chat subscription does not populate the API settings. No cloud key is included. Provider usage may incur charges; long transcripts can require several requests.

Remote addresses must use HTTPS. HTTP is accepted for localhost. The app does not automatically switch to a cloud provider if a local server fails.

## 9. Generate a summary

Choose the transcript scope in the AI workspace:

| Scope | What the AI receives |
| --- | --- |
| All transcript so far | The current transcript, unless a numeric time range in the prompt narrows it |
| Selected transcript text | Only the highlighted passage; select text before generating |
| Last 10 minutes | Transcript segments overlapping the last ten minutes of capture time |

Enter a request, then click **Prose** for a written summary or **Slide outline** for headings and points.

Example requests:

- `Summarize the key ideas and decisions.`
- `Summarize last 10 minutes, including action items.`
- `Summarize the section talking about the budget, including decisions and open questions.`
- `Summarize the past 30 seconds.`

Numeric expressions using last, past, or previous seconds, minutes, or hours are filtered by the app. Topic requests are interpreted by the AI using the supplied context. Selected text takes precedence over time expressions. The explicit **Last 10 minutes** scope always uses ten minutes, even if the prompt mentions a different duration.

The time window uses accumulated capture time, including silence while recording; stopped time does not advance it. A transcript line crossing the boundary may be included in full. Audio still waiting in the queue is not included until transcribed.

Each result becomes a separate summary block. Results are snapshots and do not automatically update as capture continues. **Cancel** stops the AI request without stopping capture. Review generated content against the transcript.

## 10. Export summaries and PowerPoint

1. Select a summary block to display it. Hold **Ctrl** and click to select several blocks.
2. Click **Export selected PPTX** to combine the selected blocks, or **Export all** to combine every block.
3. Choose a filename and location.
4. Wait for the export-complete message.

Existing slide outlines export locally. Prose summaries require an additional AI conversion to slide outlines, so the configured provider must be available. PowerPoint itself is not needed to create the file.

Use **Settings → Target points per slide** to request four or five points. Sparse evidence can produce fewer points. To save the displayed summary as plain text, use **Summaries → Save displayed summary text…**.

## 11. Save, open, and recover sessions

**File → Save session…** writes a JSON file containing transcript lines, timestamps, session duration, and summary blocks. Save periodically and after the final queue has drained.

**File → Open session…** reopens a Quillvora JSON session. **File → New session** starts a fresh session. Stop capture and finish or cancel AI work before either action. The app archives a nonempty current session before replacing it.

Automatic recovery saves changed content approximately every ten seconds and saves again on stop/close. It is a recovery aid, not a substitute for named session files or backups.

To find application data, paste this path into File Explorer's address bar:

```text
%LOCALAPPDATA%\SourceScribe
```

This folder contains settings, the latest recovery snapshot, archived sessions under `Sessions`, and downloaded models under `Models`. User-selected exports are stored wherever you chose to save them.

The folder keeps the former SourceScribe name so existing data remains available after the rename to Quillvora. Previously saved SourceScribe session files can still be opened.

## 12. Close the application

For a complete saved record, click **Stop & finish**, wait for processing, and save the session before closing the window. Closing during capture also requests capture shutdown and recovery saving. Closing during an AI request requests its cancellation.

If the status asks you to wait for capture startup or shutdown, wait for that operation to finish and then close again. The current portable build includes the fix for the reported “Window is closing” exception.

## 13. Troubleshooting

| Symptom | What to check |
| --- | --- |
| Shortcut does not open the app | Open `dist\Quillvora\Quillvora.exe` directly. If the project moved, the shortcut needs recreating. |
| Speech model needed | Open Settings and choose the bundled model or download Tiny/Base, then save. |
| No transcript yet | Confirm a source is selected and carries audio. Allow a complete block plus processing time; inspect the status and queue. |
| PC playback missing | Select the Windows playback endpoint actually used by the audio application. |
| Microphone missing or silent | Check its connection, Windows recording endpoint, input level, and microphone access settings, then refresh while stopped. |
| NDI source missing | Start the sender, confirm the x64 runtime is installed, check network/firewall discovery, and refresh. |
| Repeated words from different sources | The microphone may also hear PC playback. Use headphones or deselect the duplicate source. |
| Queue grows or blocks drop | Stop and finish, select fewer sources or Tiny, and try again. Dropped audio is not recoverable. |
| Summary says there is no transcript in scope | Select a passage or broaden the scope. Recent silence can leave a time window empty. |
| AI returns HTTP error | Check model name, credentials, provider quota, base URL, and local-server availability. |
| Slide outline exceeds limits | Request shorter points and regenerate the outline. |
| Exporting prose fails | Prose needs AI conversion; confirm the provider works or generate a slide outline first. |
| Settings or session switching is blocked | Stop capture and finish or cancel AI work first. |
| Cannot decrypt a provider key | Re-enter that key in Settings for the current Windows account. |
| Autosave fails | Read the status message and check that the application-data folder is writable and the drive has free space. Save a session to a writable location. |
| Old closing error persists | Restart using the rebuilt executable in `dist\Quillvora`; an already running process still uses the old code. |

If reporting a problem, include the action that triggered it, the exact message, selected source type, model, and whether capture or AI work was active. Do not include API keys.

## 14. Data handling and practical limits

Speech recognition stays on this computer. Raw audio is held in memory and is not recorded to disk. Session files contain text and summaries, not audio; this version has no interface for importing an audio recording.

Cloud summary requests send the scoped transcript or derived evidence and your prompt to the selected provider. Local-server requests go to the server you configure. API keys are encrypted for your Windows account, but saved transcripts, summaries, and session files are not encrypted by Quillvora.

The app identifies sources rather than individual speakers. It uses short audio blocks rather than word-by-word streaming and does not remove echo. Keep important session files backed up and review transcription and summary accuracy before using the output.

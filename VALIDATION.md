# Verification for the first Windows build

- Release build and self-contained Windows x64 publish: passed, no compiler warnings or errors.
- 29 core checks: passed. Scope boundaries and time units, concurrent-source export ordering, large-input splitting and summarization, JSON slide parsing, session persistence, DPAPI encryption, PCM stereo conversion, resampling, partial-block flush, silence filtering, all five mocked AI provider contracts, error reporting, and PPTX schema validation.
- Real local speech inference: passed using bundled Tiny model and whisper.cpp JFK reference speech. Output included “ask not what your country can do for you”.
- Windows endpoint enumeration: passed, 7 endpoints detected on this machine.
- NDI runtime discovery: passed. Synthetic audio sender discovered, receiver connected, float audio received and converted to 16 kHz mono. The test required discovery to supply the sender's advertised address.
- WPF window construction and offscreen layout rendering: passed.
- Two-block combined presentation: passed Open XML schema validation.

Live cloud calls were not made because no user API keys were supplied. Ollama/LM Studio generation was not tested against an installed model server. Physical microphone speech, PC playback echo conditions, external NDI senders, and long-running endurance remain environment-specific acceptance checks. This is an unsigned first build, not a claim of production certification.

Bundled ggml-tiny.bin SHA-256:
`BE07E048E1E599AD46341C8D2A135645097A538221678B7ACDD1B1919C6E1B21`

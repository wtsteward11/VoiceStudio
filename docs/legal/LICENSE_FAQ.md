# License FAQ (Item 34)

**Purpose:** Clarify licensing for the VoiceStudio application, models, and redistribution of generated content.

## VoiceStudio application

- **License:** MIT (see repository root `LICENSE`).
- **Copyright:** Copyright (c) 2025 VoiceStudio.
- **Summary:** You may use, copy, modify, merge, publish, distribute, sublicense, and sell copies of the software, subject to including the copyright notice and license in all copies. No warranty; authors not liable.

## Model-specific licenses

- Each engine and its models may have their own licenses (e.g. Coqui TTS MPL-2.0, Piper, Whisper).
- Engine manifests under `engines/*/engine.manifest.json` should reference the model license where applicable.
- **You are responsible** for complying with the license of any model you download or use (e.g. commercial vs non-commercial, attribution).

## Redistribution of generated audio

- **VoiceStudio app:** Output files produced by the app are not automatically licensed under MIT; their use is governed by the licenses of the models and any input (e.g. voice clone) used.
- **Your content:** If you use your own voice and models you have rights to, you typically own the resulting audio; ensure you have consent and rights for any third-party voice or asset used.
- **Provenance:** Use the app's provenance metadata (e.g. sidecar files) to record how content was generated for licensing and attribution.

## Third-party dependencies

- See **`THIRD_PARTY_LICENSES.md`** in the repository root for a list of third-party libraries and their licenses.
- Building or distributing VoiceStudio may require you to comply with those licenses (e.g. including notices, source for GPL components).

## Revision history

| Date       | Change |
|------------|--------|
| 2026-02-28 | Initial FAQ (Item 34, content-creator wedge). |

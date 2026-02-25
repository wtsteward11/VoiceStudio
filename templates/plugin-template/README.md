# Audio Normalizer — Example VoiceStudio Plugin

This is a minimal working plugin that demonstrates the VoiceStudio Plugin SDK.

## Structure

```
normalize/
  manifest.json       # Plugin metadata and capabilities
  normalize_plugin.py # Plugin implementation
  README.md           # This file
```

## Installation

1. Copy this directory to `%LOCALAPPDATA%\VoiceStudio\Plugins\normalize\`
2. Restart VoiceStudio
3. The "Audio Normalizer" effect will appear in the Effects Mixer panel

## Development

1. Subclass `AudioEffectPlugin` from `sdk.plugin`
2. Implement `process(audio, sample_rate, params)` to apply your effect
3. Define `get_parameter_schema()` to expose UI controls
4. Set `plugin_id`, `name`, `version` class attributes
5. Create a `manifest.json` with your plugin metadata

## API Version Compatibility

- **Minor version changes** (1.0 -> 1.1): Backward compatible. New optional methods may be added.
- **Major version changes** (1.x -> 2.0): Breaking. Plugin must be updated for the new API.

# Architecture

## Product rules

1. Never fabricate speech or music.
2. Keep the real-time callback deterministic and allocation-light.
3. Use adaptation to control DSP parameters, not to replace waveform content.
4. Prevent feedback loops before opening devices.
5. Expose many controls, but ship useful presets and safe ranges.

## Signal topology

```text
Voice capture or RVC loopback
  → input trim
  → adaptive gate
  → rumble/plosive cut
  → de-esser
  → 3-band EQ
  → loudness rider
  → compressor
  ┐
  ├→ 32-bit float stream bus → master limiter → selected output
  │
App/endpoint loopback
  → input trim
  → 3-band EQ
  → loudness rider
  → compressor
  → voice sidechain ducker
  ┘
```

## Layers

### UI

WPF on .NET 8. The visual graph is a first-party control with draggable nodes and bypass state. The normal workflow remains preset-first, while the right panel exposes detailed controls.

### Device layer

NAudio 2.3 stable provides WASAPI endpoint capture, endpoint loopback capture, playback, resampling, and mixing. Every source is normalized to 48 kHz stereo floating point.

### DSP layer

`MintDspSampleProvider` performs deterministic sample processing. Automatic mode learns the quiet noise floor and adapts the gate and level rider over time. The controller cannot synthesize samples.

### Recovery

Settings live in `%APPDATA%\Cinder MINT\settings.json`. The watchdog refreshes endpoint IDs and restarts the engine after a device fault when auto-reconnect is enabled.

## Process-specific capture

Windows exposes process-tree loopback through `ActivateAudioInterfaceAsync` and `AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK` on Windows 10 build 20348 and newer.

The hardened implementation will live behind an application-capture abstraction and must handle:

- process and child-process selection;
- browser multi-process trees;
- process exit and restart;
- COM activation cleanup;
- invalid or zero buffer sizes;
- protected/DRM audio;
- endpoint format changes.

Until that is reliable, routing a chosen app to a dedicated Windows/VB endpoint is safer and more predictable.

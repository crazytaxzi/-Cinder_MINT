# MintyFilter architecture direction

Branch scope: this design belongs to `MintyFilter` and branches descended from it.

## Product goal

MintyFilter is a high-fidelity Windows audio cleanup, routing, and mastering application for live voice, RVC, streaming, and podcast workflows. It combines MINT's graph/routing and bounded AI-control architecture with CinderFilter's stronger learned speech-enhancement and speaker-focused tools.

The default experience must be simple, but the engine must not trap advanced users behind one Windows audio API or hard-coded routing assumptions.

## Audio-host freedom

MintyFilter must expose the host API separately from the device endpoint. The same physical interface may appear through multiple host APIs and the user is allowed to choose the one that behaves best on their hardware.

Target Windows host APIs:

- WASAPI Shared
- WASAPI Exclusive
- MME / WinMM
- WDM Kernel Streaming (KS)
- ASIO when the selected hardware supplies an ASIO driver

DirectSound may be considered as an additional compatibility backend, but it is not a substitute for the APIs above.

### Backend abstraction

The realtime graph must consume and produce a common internal format independent of host API:

- 48 kHz preferred internal sample rate
- 32-bit float processing
- mono/stereo normalization at source boundaries
- explicit sample-rate conversion when a host API/device cannot run at the graph rate
- one timing/clock owner per physical route

Every source/output node stores both:

1. logical endpoint identity;
2. selected host API/backend.

The engine must not silently change the selected host API when reopening a device. If recovery requires a different backend, MintyFilter asks or uses an explicitly enabled fallback order.

### Implementation direction

Do not force all backends through NAudio just because the current MINT engine uses WASAPI.

Use a backend interface such as:

```text
IAudioHostBackend
  EnumerateDevices()
  ProbeFormat()
  OpenInput()
  OpenOutput()
  GetLatencyRange()
  GetClockIdentity()
  Close()
```

Probable adapters:

- NAudio/native Windows adapter for WASAPI and MME where it remains reliable.
- ASIO adapter for manufacturer ASIO drivers.
- Native PortAudio bridge or another thin native backend for WDM-KS where .NET support is insufficient.

PortAudio is a reasonable shared native layer because its Windows host implementations include WMME, WDM-KS, WASAPI, and ASIO, but MintyFilter should keep the abstraction above it so one backend library never becomes a product prison.

ASIO has special constraints. Some ASIO implementations allow only one device/driver instance at a time and bypass the normal Windows mixer. MintyFilter must display that fact rather than pretending every API has identical semantics.

## Route safety: freedom first, guardrails second

MintyFilter must distinguish an impossible/recursive graph from a merely risky external route.

### Hard blocks

These remain non-overridable because a preview cannot make them logically valid:

- an actual cycle wholly inside the MintyFilter graph;
- a node connected to itself;
- recursive graph compilation;
- malformed port type connections;
- two graph writers attempting to own the same non-shareable exclusive/ASIO stream when the backend cannot support it.

### Previewable warnings

These should no longer be blanket hard failures:

- selecting an endpoint that resembles the opposite side of a virtual cable already in use;
- output/input combinations that could form a loop through VoiceMeeter, another mixer, Windows monitoring, or third-party software;
- unusual ASIO/KS exclusive combinations;
- routes whose external behavior MintyFilter cannot inspect with certainty.

For those cases the dialog offers:

```text
Potential feedback or routing conflict detected.

MintyFilter cannot prove that this route is safe because external software may
send this output back into an active input.

[ SAFE PREVIEW ]   [ CHANGE ROUTE ]   [ CANCEL ]
```

After a successful preview:

```text
Preview completed without triggering a safety stop.
This does not guarantee that later third-party routing changes cannot create a loop.

[ USE THIS ROUTE ]  [ TEST AGAIN ]  [ CANCEL ]
```

## Safe Preview mode

"50% volume" is only one layer. UI volume percentages do not correspond to a guaranteed acoustic SPL, so Safe Preview uses several protections together.

Preview behavior:

1. Start muted.
2. Apply a hard digital preview ceiling no higher than -12 dBFS.
3. Clamp the controllable Windows/session endpoint volume to no more than 50% where the selected backend exposes such control.
4. Fade in slowly instead of opening at full preview gain.
5. Limit the preview to a short timed window, initially about 3 seconds.
6. Keep the normal runaway-feedback detector active with a lower/faster preview threshold.
7. Add sustained-tone detection so narrow high-frequency oscillation or "dog-whistle" feedback aborts the preview quickly.
8. Abort immediately on near-full-scale repetition, exponential energy growth, severe clipping, or detected recursion symptoms.
9. Fade out before releasing the preview stream.
10. Require explicit confirmation before converting the preview into the normal live route.

For ASIO, KS, or exclusive modes where normal Windows endpoint/session volume is bypassed or unavailable, the digital preview ceiling and internal gain ramp are authoritative.

Safe Preview must never write the risky route into saved startup configuration until the user explicitly confirms it.

## Backend-aware UX

Simple mode should show one device selector with a small host-API badge, for example:

```text
Input
JBL Quantum910 Wireless  [WASAPI Shared]

Output
Minty Filter Output     [WDM-KS]
```

Clicking the badge expands compatible APIs for that endpoint:

```text
WASAPI Shared     Recommended / compatible
WASAPI Exclusive  7 ms / exclusive
WDM-KS            4 ms / exclusive-ish
MME               45 ms / broad compatibility
ASIO               manufacturer driver / low latency
```

MintyFilter may recommend an API but must not silently force it.

Advanced Patchbay exposes backend selection directly on every source/output node.

## Fallback policy

Device recovery must be explicit and configurable.

Example fallback order:

```text
Preferred: ASIO
Fallback 1: WDM-KS
Fallback 2: WASAPI Shared
Fallback 3: MME
```

Default behavior is to retry the selected backend first. Cross-backend fallback occurs only when the user enables it or accepts a recovery prompt.

## High-fidelity processing remains backend-independent

Once audio crosses a source boundary, the rest of MintyFilter sees the same normalized stream. Noise removal, AI control, DeepFilterNet, RVC cleanup, EQ, de-essing, dynamics, mixing, mastering, and virtual output do not need to care whether the source came from MME, KS, ASIO, or WASAPI.

This separation is required so adding host APIs does not duplicate the DSP engine.

## Virtual endpoints

MintyFilter still targets two first-party virtual endpoints:

- `Minty Audio Input` — playback endpoint used to feed application/RVC audio into MintyFilter.
- `Minty Filter Output` — recording endpoint exposing MintyFilter's processed result to RVC, OBS, Discord, podcast software, etc.

Those virtual endpoints should participate in the same backend/route-safety model instead of being special-cased into a single WASAPI path.

## Decision

MintyFilter will not be WASAPI-only.

The product philosophy is:

> Recommend the safest/easiest route, explain the tradeoffs, let the user choose their hardware/API, and use Safe Preview when risk is plausible but not provably invalid.

Hard refusal is reserved for configurations that are structurally impossible or recursively unsafe inside MintyFilter itself.

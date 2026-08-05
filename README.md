# Cinder MINT

**Machine-Intelligent Normalization & Tone**

Cinder MINT is a Windows real-time audio router and mastering tool for streamers. It combines:

- a voice/RVC cleanup lane;
- a music or application-audio lane;
- adaptive level riding;
- de-essing, EQ, compression and sidechain ducking;
- a protected master output for OBS, VoiceMeeter, or a VB-Cable;
- a neon green and electric purple Cinder Stream UI;
- saved device choices, presets, auto-start, and device-recovery behavior.

MINT filters and controls audio. It does **not** synthesize or fabricate replacement speech.

## Current working foundation

The initial commit is a runnable WPF/.NET 8 desktop application with:

- simultaneous microphone/RVC and loopback capture;
- selectable capture, loopback, and render endpoints;
- 48 kHz stereo 32-bit floating-point mix engine;
- adaptive noise-floor gate;
- rumble/plosive high-pass filter;
- dynamic de-esser;
- three-band voice and program EQ;
- slow loudness riding;
- compressor;
- microphone sidechain ducking;
- master limiter;
- draggable visual signal graph with processor bypass;
- JSON settings persistence;
- automatic endpoint recovery;
- presets for natural speech, strong streaming, raw rescue, and RVC cleanup.

## RVC routing

MINT can clean audio after RVC in either of these ways:

1. Select the RVC virtual microphone as the **Voice/RVC Lane** capture source.
2. Send RVC to a dedicated render endpoint or VB-Cable, then select that endpoint as a **LOOPBACK / RVC** source.

Do not select the same endpoint as both a loopback source and MINT output. That creates a feedback loop, and MINT blocks it.

## Application and browser audio

The stable first build captures a selected Windows render endpoint. For one app or browser:

1. Create or choose a dedicated VB-Cable.
2. In Windows **Volume mixer**, route the browser/app to that cable.
3. Select the cable's render endpoint as **Music / App Lane**.
4. Select a different endpoint as MINT's final output.

Native process-tree capture is planned behind the `IApplicationCaptureSource` boundary. Windows supports process loopback on build 20348+, but the native activation path needs a hardened implementation before it belongs in a set-and-forget release.

## Build

Requirements:

- Windows 10 build 20348 or newer;
- .NET 8 SDK;
- Visual Studio 2022 or `dotnet` CLI;
- at least one dedicated virtual endpoint for safe routing.

```powershell
git clone https://github.com/crazytaxzi/-Cinder_MINT.git
cd -Cinder_MINT
dotnet restore
dotnet build -c Release
dotnet run --project .\src\Cinder.MINT.App\Cinder.MINT.App.csproj
```

## Recommended VoiceMeeter setup

```text
Physical mic or RVC output
        ↓
Cinder MINT Voice/RVC lane

Browser/app → dedicated VB-Cable
        ↓
Cinder MINT Music/App lane

Cinder MINT master output → another VB-Cable
        ↓
VoiceMeeter Potato / OBS
```

## Important audio note

The initial limiter is a low-latency sample-peak limiter. A 4× oversampled true-peak limiter and oversampled nonlinear enhancement stage are planned. MINT currently avoids nonlinear “exciter” processing, so it does not create new aliasing that would require a cosmetic anti-aliasing pass afterward.

See [Architecture](docs/ARCHITECTURE.md) and [Roadmap](docs/ROADMAP.md).

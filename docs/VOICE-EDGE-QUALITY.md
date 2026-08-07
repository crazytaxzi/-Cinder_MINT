# MintyFilter Voice Edge Quality

Status: planned DSP milestone after the control-deck and audio-backend work.

## Goal

Speech should never sound as though samples are being switched on and off with a light switch. Noise removal, gating, expansion, RVC cleanup and restoration stages must preserve the temporal shape around phoneme starts, consonants, breaths that belong to speech, and word endings.

## Temporal feathering

Every suppressive stage that can materially change gain should support a smooth gain envelope rather than sample/block hard cuts.

Planned behavior:

- short look-ahead where the selected latency mode allows it;
- soft attack/opening so initial consonants are not clipped;
- hold time so short gaps inside a word are not mistaken for silence;
- program-dependent release so word endings decay naturally;
- hysteresis between speech-open and noise-close decisions;
- crossfaded spectral masks between analysis frames;
- minimum gain-change slew limits to prevent zippering and pumping;
- separate timing presets for RVC Live, voice chat, streaming and studio/podcast modes.

The AI may recommend timing/strength, but the waveform transition remains deterministic DSP.

## Anti-alias treatment

"Anti-alias" in MintyFilter means preventing processing-created aliases; it does not mean inventing missing high-frequency content.

Any nonlinear or harmonic-restoration stage should run oversampled when it can generate energy above Nyquist:

1. band-limited upsample;
2. nonlinear/harmonic processing at the higher rate;
3. low-pass reconstruction filter;
4. band-limited downsample.

Candidate stages include saturation, harmonic presence restoration, nonlinear limiting/soft clipping and any future excitation stage. Linear EQ, ordinary gain and purely subtractive filtering do not need oversampling merely for marketing reasons.

## Edge-aware spectral cleanup

Spectral denoisers should also feather in time/frequency:

- overlap-add windows remain continuous;
- masks are smoothed across adjacent frames and bins;
- speech onsets temporarily increase preservation authority;
- noise-only intervals may suppress far harder than voiced intervals;
- release tails should follow detected speech energy rather than end abruptly at a VAD threshold.

## Acceptance tests

Use real recordings containing:

- quiet speech into hard consonants (T, K, P, B, D);
- sibilants (S, SH, CH);
- words ending in low-energy consonants;
- short pauses inside sentences;
- whispered/soft starts without treating all breath as trash;
- RVC output with metallic high-frequency edges;
- fan/HVAC noise under continuous speech;
- abrupt loud-to-quiet phrases.

Listen specifically for clipped first phonemes, swallowed word endings, zipper noise, metallic tails, pumping, watery spectral modulation and new high-frequency alias products.

## Product rule

MintyFilter should prefer a small amount of harmless room residue over chopping human speech into unnaturally hard temporal edges. Aggressive modes may reduce more noise, but they still must transition smoothly.

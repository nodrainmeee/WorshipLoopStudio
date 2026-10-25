# Audio assets

## Starter library (included)

All 29 starter sounds are **synthesized by code in this repository** (`src/WorshipLoopStudio.Core/Samples/PercussionSynth.cs`). No third-party recordings or sample packs are included, so the sounds are covered by the repository's MIT license and may be used freely in services, streams and recordings.

The files are generated:

- by the CI pipeline into `Samples\Starter` inside the published ZIP (`tools/SampleGen`);
- on first launch into `%LOCALAPPDATA%\WorshipLoopStudio\Samples\Starter` (copied from the install folder, or synthesized if missing);
- again on demand with *Settings > Rebuild starter samples*.

| Category | Sounds | Technique |
|----------|--------|-----------|
| African Percussion | Gangan / talking drum (open, pitch up, pitch down, low, high), djembe (bass, tone, slap), shekere, agogo (high, low), African double bell (high, low) | circular-membrane modal synthesis with pitch glides; noise-grain beads on a resonant gourd; inharmonic bell partials |
| Drum Kit | kick, deep kick, snare, rimshot, clap, toms (high, mid, low) | pitch-swept sine bodies, filtered noise |
| Cymbals & Hi-Hats | closed hat, open hat, crash, ride | six detuned square oscillators (metallic ratios) through high-pass filters |
| Shakers | shaker, long shaker | band-passed noise envelopes |
| Gongs | large gong, small gong | eight inharmonic partials with beating and bloom |

Sounds are mono, 44.1 kHz, 16-bit WAV, normalized to -1 dBFS.

## Your own sounds

Use *Import files...* in the sample browser or record in the Record tab. Only import material you have the right to use.

## Larger libraries

Large third-party libraries are deliberately not stored in the repository. Copy any legally obtained WAV files into `%LOCALAPPDATA%\WorshipLoopStudio\Samples\User` (subfolders are fine) and click *Refresh* in the browser.

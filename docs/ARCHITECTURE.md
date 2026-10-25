# Architecture

```
            WPF UI (MVVM)                      WorshipLoopStudio.App
   Perform | Record | Separate | Settings
        |           |          |
        v           v          v
  ArrangementBuilder  RecordingService   DemucsService ---> python -m demucs (separate process)
        |                  |                                   WorshipLoopStudio.Separation
        v                  v
  LoopEngine  StemPlayer  Monitor input                         WorshipLoopStudio.Audio
        \        |        /
         MixingSampleProvider (44.1 kHz stereo float)
                 |
         WASAPI / MME output (always open)
```

## Real-time rules (audio thread)

`LoopEngine.Read` and `StemPlayer.Read` run on the Windows audio thread. They:

- never allocate, lock, log, touch the disk or call the UI;
- read an **immutable arrangement snapshot** (`EngineArrangement`) swapped with `Volatile.Write` whenever the user edits something;
- receive transport commands (start, stop, preview, pad hit, panic) through a `ConcurrentQueue`;
- publish position, transport state and peak levels through `Volatile` fields that the UI polls at ~30 fps.

All samples are decoded and resampled to 44.1 kHz stereo **before** they reach the engine (`SampleCache`, background threads).

## Timing

One musical clock: the loop position in frames. The start of step *k* is `round(k * framesPerStep)`, computed from the index every time, so there is no accumulated drift. The loop end is the start of step *N*, and wrapping to step 0 happens at the same frame, so loops repeat without a gap. Events are handled at their exact frame even in the middle of a sound-card buffer. Tempo or grid changes while playing keep the musical position (in beats) and never retrigger steps.

Tracks are either:

- **Pattern** tracks: a one-shot sample triggered on active steps (polyphonic, 96 voices, oldest voice stolen);
- **Audio loop** tracks: a clip restarted at step 0 of every cycle, with a short choke fade of the previous cycle.

Mute, solo, volume and pan are smoothed per buffer to avoid zipper noise and clicks.

## Recording

`WasapiCapture` delivers buffers on a capture thread; they are converted to float, gain-adjusted and metered there, then queued. A dedicated writer task writes the WAV file, so disk speed never affects capture. Monitoring routes the input through a `BufferedWaveProvider` into the output mixer.

## Separation

`DemucsService` manages a private Python virtual environment under `Models\demucs-env`, installs pinned versions (PyTorch 2.5.1, Demucs 4.0.1), downloads models into `Models\torch` (`TORCH_HOME`), converts the input to WAV with NAudio, runs `python -m demucs`, parses tqdm progress from stderr and moves the stems to `SeparatedStems`. Failures are mapped to plain-language messages (`DemucsOutputParser`).

## Persistence

Projects are JSON (`.wlsproj`) written through a temporary file and `File.Replace`, so a crash never leaves a half-written project. Built-in sounds are referenced as `starter:<id>`, other samples by path.

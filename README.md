# Worship Loop Studio

A Windows 10 / 11 desktop loop workstation for live Sunday worship: build percussion loops (gangan talking drum, djembe, shekere, agogo, African bells, gongs, kit, cymbals, shakers), play them continuously in sync, record your own beats and voices, and split songs into stems with a real AI separation model.

Built with **C# / .NET 10 / WPF** and **NAudio** (WASAPI). Stem separation uses **Demucs v4** (Meta AI Research) in a private Python environment.

> **Build status:** every push is compiled, tested and packaged by GitHub Actions on a Windows runner (see the *Actions* tab). Only trust a version whose workflow run is green.

---

## Features

| Area | What you get |
|------|--------------|
| **Perform** | Step sequencer (up to 32 tracks, 1/2/4/8 bars), instrument category dropdown, sample browser with preview, pads (mouse or keys 1-9), per-track volume / pan / mute / solo, mixer with master meter |
| **Tempo** | 30-300 BPM, +/-1 and +/-5 buttons, direct numeric entry, tap tempo, time signatures 4/4 3/4 2/4 5/4 6/8 7/8 12/8, grid 1/1 to 1/8 beat incl. triplets |
| **Metronome** | On/off, volume, 0-2 bar audible count-in, beat LEDs, bar/beat position |
| **Playback** | Sample-accurate shared clock, loops play until you stop them, no gap at the loop point, stop now / let ring / at end of bar / at end of loop, "all sound off" |
| **Record** | Choose any Windows input, input gain and level meter with clip warning, optional monitoring, record until stopped / N seconds / N bars, import audio, trim, set loop start/end, snap to whole bars, save as sample or add as loop track |
| **Separate** | Real Demucs separation into vocals, drums, bass, guitar, piano, other (6-stem model) or 4 stems; progress, log and clear errors; synced stem preview with mute / solo / volume; export WAV or MP3 |
| **Projects** | New / open / save / save as / rename; tempo, grid, tracks, mixer and recording references saved as JSON; export mix or a single loop to WAV or MP3 |
| **Storage** | `%LOCALAPPDATA%\WorshipLoopStudio\` with Projects, Loops, Recordings, Samples, SeparatedStems, Models, Logs, Exports. Another location can be chosen in Settings. No administrator rights needed |
| **Sounds** | 29 starter sounds synthesized by the app itself (original, MIT licensed) |

---

## Download and run (Windows 10 / 11, 64-bit)

1. Open the repository on GitHub and click **Actions** > **Build Windows** > the latest green run.
   (Or open **Releases** if a version tag such as `v1.0.0` was published.)
2. Download the artifact **WorshipLoopStudio-x.y.z-win-x64** and unzip it (GitHub wraps artifacts in an extra ZIP: unzip twice).
3. Open the `WorshipLoopStudio` folder and run **WorshipLoopStudio.exe**.
   The build is self-contained: **no .NET installation is required**.
4. Windows SmartScreen may warn because the app is not code-signed. Click **More info** > **Run anyway**.
5. On first launch the app creates its data folder and the starter sounds, then opens a demo groove. Press **Space** to play.

Keyboard: **Space** play/stop, **Esc** stop, **1-9** trigger tracks, **Ctrl+N / Ctrl+O / Ctrl+S** new / open / save.

---

## Using it in a service

1. **Perform** tab: pick a category in the *Instrument category* dropdown, select a sound, **Preview**, then **Add as new track**.
2. Click cells in the sequencer to build the pattern. Set BPM, time signature, grid and loop length at the top.
3. Turn the metronome on and choose a count-in, then **PLAY**. The loop repeats until you press **STOP**.
4. Choose what Stop does: *at end of bar* is the smoothest during worship.
5. Save the project (Ctrl+S). Export a mix or a single loop from the header bar.

**Record** tab: choose your microphone or interface, click *Open input*, set the gain (keep the meter out of the red), pick a length mode, press **RECORD**. Then trim the clip, *Snap length to whole bars*, and **Add to project as loop track**.

If Windows blocks the microphone: *Settings > Privacy & security > Microphone* and enable *Let desktop apps access your microphone* (the app has a button that opens this page).

---

## Separate module (stem separation)

Separation runs Demucs, a neural network. It is **not** simulated: if Demucs is missing, the app says so and produces nothing.

One-time setup:

1. Install **Python 3.11** (3.10 and 3.12 also work) from <https://www.python.org/downloads/windows/>. Tick *Add python.exe to PATH*.
2. In the app, open **Separate** and click **Install Demucs**. Tick the NVIDIA option only if you have an NVIDIA graphics card.
   This creates `%LOCALAPPDATA%\WorshipLoopStudio\Models\demucs-env` and installs PyTorch 2.5.1 + Demucs 4.0.1 (about 1 GB on CPU, about 3 GB with CUDA). Internet required.
3. Click **Download model** (or let the first separation download it, about 80-160 MB, stored in `Models\torch`).

Then choose a song (WAV, MP3, FLAC, M4A...), a model and a device, and click **SEPARATE**.

| Model | Stems | Speed |
|-------|-------|-------|
| `htdemucs_6s` | vocals, drums, bass, guitar, piano, other | default |
| `htdemucs` | vocals, drums, bass, other | fastest |
| `htdemucs_ft` | vocals, drums, bass, other | best quality, ~4x slower |

Hardware: 8 GB RAM minimum (16 GB recommended). On a recent CPU a 4-minute song takes roughly 2-8 minutes; an NVIDIA GPU is usually 5-10x faster. The 6-stem model's piano output is marked experimental by its authors. Stems are written to `SeparatedStems\<song>_<date>` and appear in the sample browser.

Advanced: **Use my own python.exe** lets you point to an existing environment where `pip install demucs` is already done.

Full details: [docs/SEPARATE.md](docs/SEPARATE.md).

---

## Build from source

Requirements: Windows 10/11 x64 and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). Visual Studio 2026 or VS Code is optional.

```powershell
git clone https://github.com/<you>/worship-loop-studio.git
cd worship-loop-studio
dotnet restore WorshipLoopStudio.sln
dotnet build WorshipLoopStudio.sln -c Release
dotnet test tests/WorshipLoopStudio.Tests/WorshipLoopStudio.Tests.csproj -c Release
dotnet run --project src/WorshipLoopStudio.App -c Release

# Self-contained package, like the CI build:
dotnet publish src/WorshipLoopStudio.App/WorshipLoopStudio.App.csproj -c Release -r win-x64 --self-contained true -o publish/WorshipLoopStudio
dotnet run --project tools/SampleGen -c Release -- publish/WorshipLoopStudio/Samples/Starter
```

---

## Put the project on GitHub and trigger the build

If you received these files as a ZIP instead of a repository:

1. Create an empty repository on <https://github.com/new> (no README, no .gitignore).
2. In a terminal inside the project folder:
   ```powershell
   git init
   git add .
   git commit -m "Worship Loop Studio"
   git branch -M main
   git remote add origin https://github.com/<you>/<repo>.git
   git push -u origin main
   ```
   (Without git: on the empty repository page click *uploading an existing file* and drag every file and folder, keeping the `.github` folder.)
3. The push starts **Actions > Build Windows** automatically. You can also run it by hand: *Actions > Build Windows > Run workflow*.
4. When the run is green, download the ZIP from the run's *Artifacts* section.

The basic pipeline needs no paid service and no secrets.

---

## Versioning and releases

- Version numbers follow `MAJOR.MINOR.PATCH`. The default version is in `Directory.Build.props`.
- Untagged builds are numbered `0.1.<run number>`.
- To publish a release: `git tag v1.0.0` then `git push origin v1.0.0`. The workflow builds that version and attaches the ZIP to a GitHub Release.
- See [docs/RELEASING.md](docs/RELEASING.md) and [CHANGELOG.md](CHANGELOG.md).

---

## Repository layout

```
src/WorshipLoopStudio.Core        tempo math, project model + JSON, storage, starter-sound synthesis, editing math
src/WorshipLoopStudio.Audio       real-time loop engine, output devices, recording, stem player, export (NAudio)
src/WorshipLoopStudio.Separation  Demucs installation, model download and separation process
src/WorshipLoopStudio.App         WPF user interface (MVVM)
tests/WorshipLoopStudio.Tests     xUnit tests (timing, loop engine, serialization, samples, editing, parsers)
tools/SampleGen                   writes the starter library (used by CI)
.github/workflows                 Windows build, test, package, release
docs/                             architecture, latency, assets, separation, releasing
```

Architecture notes: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Limitations

Read [docs/LIMITATIONS.md](docs/LIMITATIONS.md) before using the app in a service (latency, ASIO, device changes, Bluetooth, memory use of stems).

## License

MIT, see [LICENSE](LICENSE). Third-party components: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

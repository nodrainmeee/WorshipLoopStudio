# Third-party notices

## Bundled with the application

| Component | Version | License | Use |
|-----------|---------|---------|-----|
| [NAudio](https://github.com/naudio/NAudio) | 2.2.1 | MIT | Windows audio I/O (WASAPI, MME), file decoding, Media Foundation MP3 encoding |
| .NET runtime and WPF | 10 | MIT | Runtime (included in the self-contained build) |

## Installed on demand by the Separate module (not bundled)

These are downloaded from PyPI / pytorch.org into `%LOCALAPPDATA%\WorshipLoopStudio\Models\demucs-env` only when the user clicks **Install Demucs**.

| Component | Version | License |
|-----------|---------|---------|
| [Demucs](https://github.com/facebookresearch/demucs) | 4.0.1 | MIT |
| Demucs pretrained models (`htdemucs`, `htdemucs_6s`, `htdemucs_ft`) | - | MIT (released with Demucs by Meta AI Research) |
| [PyTorch / torchaudio](https://pytorch.org) | 2.5.1 | BSD-3-Clause |
| [soundfile](https://github.com/bastibe/python-soundfile) | latest | BSD-3-Clause |
| Python | 3.10-3.12 | PSF License (installed by the user) |

## Audio assets

The starter instrument library contains **no third-party recordings**. Every sound is synthesized by
`src/WorshipLoopStudio.Core/Samples/PercussionSynth.cs` and is covered by this repository's MIT license.
See [docs/ASSETS.md](docs/ASSETS.md).

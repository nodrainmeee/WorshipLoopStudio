# Separate module

## What it does

Splits a mixed song into stems with **Demucs v4** (Hybrid Transformer Demucs, Meta AI Research, MIT license). This is a real neural source-separation model. The app never fakes a result: if Python, Demucs or the model is unavailable, separation stops with an explanation.

## Installing

1. Install Python 3.11 (or 3.10 / 3.12) from python.org with *Add python.exe to PATH*.
2. Separate tab > **Install Demucs**. The app runs:
   ```
   python -m venv %LOCALAPPDATA%\WorshipLoopStudio\Models\demucs-env
   demucs-env\Scripts\python -m pip install --upgrade pip
   demucs-env\Scripts\python -m pip install torch==2.5.1 torchaudio==2.5.1 --index-url https://download.pytorch.org/whl/cpu   (or /whl/cu121 for NVIDIA)
   demucs-env\Scripts\python -m pip install demucs==4.0.1 soundfile
   ```
3. **Download model** fetches the selected model into `Models\torch` (otherwise it is fetched on first use).
4. **Check setup** confirms Demucs, PyTorch and whether CUDA is available.

Manual alternative: create any environment with the commands above and choose its `python.exe` with **Use my own python.exe**.

## Running

Choose the song, the model (`htdemucs_6s` for vocals / drums / bass / guitar / piano / other) and the device, then **SEPARATE**. The song is converted to a 44.1 kHz WAV, then Demucs runs as a separate process (the audio engine keeps running). Progress comes from Demucs' own progress bar; the full log is shown below.

Results go to `SeparatedStems\<song>_<yyyyMMdd_HHmmss>\<stem>.wav`. All stems start at sample 0 and have the same length, and the stem player plays them from one shared position, so they stay in sync.

In the Stems panel: play / pause / stop / seek, mute, solo and volume per stem, export one stem or all stems as WAV or MP3, or **Make a loop from it** (opens the stem in the Record clip editor).

## Hardware

| | Minimum | Recommended |
|-|---------|-------------|
| RAM | 8 GB | 16 GB |
| Disk | 3 GB (CPU install) | 6 GB (CUDA install) |
| GPU | none (CPU works) | NVIDIA with 4 GB+ VRAM and a recent driver |

## Troubleshooting

| Message | Fix |
|---------|-----|
| Python 3.10, 3.11 or 3.12 was not found | Install Python 3.11 and tick *Add to PATH*, or use the `py` launcher |
| Demucs is not installed | Click *Install Demucs* |
| The separation model could not be downloaded | Connect to the internet once, click *Download model* |
| The graphics card ran out of memory | Choose *CPU* as device |
| Torch not compiled with CUDA | Choose *CPU*, or reinstall with the NVIDIA option |

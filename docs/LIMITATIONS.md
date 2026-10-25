# Limitations and live-use notes

Please test your full setup (computer, audio interface, speakers) before a service.

## Latency

- Output uses **WASAPI** (shared or exclusive) or **MME**. Typical round trip with WASAPI shared and a 30-60 ms buffer is acceptable for loops and pads; it is **not** as tight as a dedicated ASIO driver.
- **ASIO is not supported** in this version. Many audio interfaces also provide a WASAPI driver, which works.
- In shared mode Windows mixes and resamples (for example to 48 kHz); this adds a few milliseconds.
- WASAPI exclusive mode gives the lowest latency but blocks other apps from using the device and may be refused by some drivers (the app then explains the error and falls back).
- **Bluetooth speakers/headphones add 100-300 ms** and are not recommended for live performance.
- Pad hits recorded into the pattern are rounded to the nearest step; output latency is not compensated.

## Recording

- Monitoring through the app adds output latency plus about 20-50 ms of buffering. Use the interface's direct monitoring when available.
- Recordings are not automatically aligned to the loop: use the clip editor (trim + *Snap length to whole bars*) to make a clean loop.
- Supported capture formats: 16-bit, 24-bit and 32-bit float (WASAPI shared mode). Exotic formats are reported with an explanation.

## Devices

- If the output device is unplugged or fails, the app shows a red banner and tries to reopen the configured device, then the Windows default, then MME.
- When the Windows *default* device changes (for example plugging headphones) the app keeps the current device for stability. Choose the new device in Settings > Apply.

## Separation

- Requires Python 3.10-3.12 and a one-time internet download (1-3 GB). CPU separation of a 4-minute song takes several minutes. Do not run a separation during a live service on the performance computer.
- Quality depends on the model and the mix; the 6-stem model's piano stem is experimental.
- Loaded stems are kept in memory (about 10 MB per minute per stem).

## Files

- Supported audio files: WAV, MP3, AIFF, FLAC, M4A, WMA (decoding through Windows Media Foundation; on Windows N editions install the *Media Feature Pack* for MP3/M4A).
- MP3 export uses the Windows Media Foundation MP3 encoder; if it is missing, the app says so and WAV export still works.
- Loop samples are limited to 15 minutes, stems to 20 minutes.

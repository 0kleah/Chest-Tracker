# Third-party replay reading code

Chest Tracker counts chests from Fortnite's own replay files. Reading them uses:

- `FortniteReplayReader/`, `Unreal.Core/`, `OozSharp/`, `Unreal.Encryption/`: Shiqan's
  [FortniteReplayDecompressor](https://github.com/Shiqan/FortniteReplayDecompressor) (MIT, `LICENSE-FortniteReplayDecompressor`),
  as patched for current Fortnite by [LED-N/Fortnite-ChestCounter](https://github.com/LED-N/Fortnite-ChestCounter) (see `PATCHES.md`).
- `ChestCounter.Analysis/`: the chest analysis from LED-N/Fortnite-ChestCounter (MIT, `LICENSE-ChestCounter`).

Copied unchanged from LED-N/Fortnite-ChestCounter commit `1f38798` (2026-11-09).

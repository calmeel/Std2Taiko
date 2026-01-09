# Std2Taiko
<p align="left"><img src="Images/Logo.png"></p>  
<p align="left"><img src="Images/UserInterface.png"></p>

Convert osu!standard beatmaps into osu!taiko format with higher accuracy and optional constant-speed export.

[日本語版のREADMEはこちら](README_JP.md)


## Download
[Download the latest Windows executable](https://github.com/calmeel/Std2Taiko/releases/latest)


## Features

 - Accurate osu!standard → osu!taiko conversion  
 - Constant-speed map export (optional)  


## Usage (GUI)
No .NET runtime installation required.
1. Drag & drop a `.osu` file onto the app
2. Choose output settings
3. Press **Convert**
4. The converted taiko map will appear next to the input file


## Usage (CLI)
```bash
Std2Taiko.exe input.osu output.osu --mode stable
```


## Compatibility
Std2Taiko is distributed as a self-contained 64-bit Windows executable.
No .NET runtime or setup installer is required. Just extract and run.

 - Windows 10 / 11
 - 64-bit (x64)
 - Works from any folder (portable)


## How it works
Internally, Std2Taiko reuses parts of the osu!lazer source code to reproduce the official taiko ruleset behavior for hit object conversion.

Processing pipeline (simplified):

1. `.osu` file → legacy decoder
2. Legacy encoder standardizes `HitObjects` and metadata
3. Taiko conversion logic is applied
   - slider split decision
   - taiko hit placement
   - nested object generation (drumroll / swell)
4. Timing section (`[TimingPoints]`) is sanitized
   - rejection of invalid values (e.g., `NaN`, `-1`, huge scientific values)
   - clamping and recomputing where necessary
   - Aspire-specific timing rescue
5. Final `.osu` is written out

The slider split and placement logic matches the official `TaikoBeatmapConverter` used in lazer, but timing compatibility is closer to osu!stable due to stricter sanitation in `[TimingPoints]`.


## Limitations
- This tool aims to match stable behavior where possible. However, perfect compatibility with all maps cannot be guaranteed.
- Std2Taiko relies on osu!lazer’s beatmap decoding pipeline. As a result, maps that cannot be decoded or converted correctly in lazer may also fail to convert correctly here.
- Note that timing sanitation and Aspire rescue behavior differs from lazer, and in some cases produces more stable-compatible results than lazer.
- Some features and edge cases are not yet implemented. See [TODO.md](TODO.md) for details.


## Reporting Issues

If you encounter a bug or suspicious output, please contact the developer or open an issue on GitHub.

When reporting, please include:

- map link or `.osu` file
- output mode (stable / lazer)
- output options (constant-speed / constant-speed-adjusted)
- expected behavior (if known)

This helps reproduce and diagnose the problem more reliably.


## Additional Notes:
- No administrator privileges required
- No osu! installation required
- Does not interact with osu! client memory or anti-cheat systems
- Outputs standard `.osu` files

## License

MIT License — see `LICENSE`
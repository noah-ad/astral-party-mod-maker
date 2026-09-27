# Video conversion components

The public v2.3.0 standard package includes the conversion integration, `mux.py`,
`inspect_movie.py`, and a first-use setup dialog. It does not redistribute the
third-party video executables. Users click **Download and enable** when importing
their first video, or open the video components action in Tools / Maintenance.
No separate Python installation, pip command, administrator access, or PATH edit
is required. After setup, conversion works offline for that Windows user.

## Pinned upstream downloads

The immutable URLs, expected sizes, and SHA256 digests are stored in
[`VideoRuntime.cs`](../../VideoRuntime.cs). Setup downloads about 117 MiB:

| Component | Upstream | SHA256 |
| --- | --- | --- |
| FFmpeg 8.1.1 essentials ZIP | [Gyan's release](https://github.com/GyanD/codexffmpeg/releases/tag/8.1.1) | `6f58ce889f59c311410f7d2b18895b33c03456463486f3b1ebc93d97a0f54541` |
| Python 3.11.9 x64 embedded ZIP | [python.org](https://www.python.org/downloads/release/python-3119/) | `009d6bf7e3b2ddca3d784fa09f90fe54336d5b60f0e0f305c37f400bf83cfd3b` |
| CriCodecs 1.2.0 cp311-win_amd64 wheel | [PyPI](https://pypi.org/project/cricodecs/1.2.0/) | `ace3269a6d156fe8737b3e06ad3e80cd8c4899c3bc8df208df6314ceee4346de` |

FFmpeg's archive hash was checked against the upstream GitHub release asset
digest. Python's hash was obtained from the official Sigstore bundle. CriCodecs'
hash was checked against the PyPI version JSON. Downloads are size- and
hash-checked before extraction or execution; component metadata and notices
remain in their original archives and extracted directories.

## Installation and repair

Setup writes only to `%LOCALAPPDATA%/JixModMaker/video-runtime`. Completed,
verified archives are cached for retries. A temporary directory is checked by
starting FFmpeg and importing `cricodecs.usm` / `cricodecs.video`, then activated
by a directory swap. Failed activation restores the previous installation.
Cancellation removes partial downloads and staging files. No game resources,
proxy settings, certificates, registry settings, or system Python are changed.

Both `mux.py` and `inspect_movie.py` must remain under `data/Tools/video`.
Existing local full packages with `ffmpeg.exe` and `python/` in that directory
continue to use their bundled components. The public standard package uses the
user cache instead. Moving the application on the same PC preserves access;
another PC or Windows user needs to run setup separately.

## Third-party terms

- Gyan's [builds](https://www.gyan.dev/ffmpeg/builds/) are GPLv3-enabled.
  The extracted FFmpeg directory retains the upstream license and documentation.
  See [FFmpeg's legal page](https://ffmpeg.org/legal.html) before redistributing
  binaries, including corresponding-source requirements.
- Python is covered by the [PSF license](https://docs.python.org/3.11/license.html);
  the original embedded package's `LICENSE.txt` is retained.
- CriCodecs is published by [Youjose](https://github.com/Youjose/CriCodecs).
  The inspected 1.2.0 wheel metadata does not specify redistribution terms.
  It is fetched from the author's PyPI distribution on the user's request;
  this repository does not mirror or include that wheel in release assets.

Do not publish a locally assembled full-runtime archive without first resolving
all third-party redistribution requirements. `Publish-Portable.ps1 -Zip` builds
the public standard package; `-WithVideoRuntime` is an explicit local-only mode.

## Scope of verification

Component installation, conversion, and local preview tests are separate from
game compatibility. USM container roundtrips and native decoder preparation have
been tested, but not all in-game startup/playback cases or Android decoding.
The independent-portrait/card patch remains experimental.

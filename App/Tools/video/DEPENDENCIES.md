# Video conversion components

The public v2.3.0-v2.3.2 standard package includes the conversion integration, `mux.py`,
`inspect_movie.py`, and a first-use setup dialog. It does not redistribute the
third-party video executables. Users click **Download and enable** when importing
their first video, or open the video components action in Tools / Maintenance.
No separate Python installation, pip command, administrator access, or PATH edit
is required. After setup, conversion works offline for that Windows user.

The v2.3.6 **full video edition** includes FFmpeg, embedded Python and
CriCodecs under `data/Tools/video`. It is ready without a per-user component
cache, network download or separate installation. Its maintenance check only
validates the bundled executables. Copy the complete EXE + data folder when
moving it to another PC; copying just the EXE or DLL omits the components.

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
another PC or Windows user needs to run setup separately only for that standard
edition. Full editions carry their components with them.

## Third-party terms

- Gyan's [builds](https://www.gyan.dev/ffmpeg/builds/) are GPLv3-enabled.
  The extracted FFmpeg directory retains the upstream license and documentation.
  See [FFmpeg's legal page](https://ffmpeg.org/legal.html) before redistributing
  binaries, including corresponding-source requirements. Full packages preserve
  the original Gyan `LICENSE` and build `README.txt` under
  `third-party/ffmpeg`; the README identifies the exact upstream source commit.
  [Source and original build directions](third-party/ffmpeg/SOURCES.md) accompany
  the binary; the release also provides the exact FFmpeg source archive.
- Python is covered by the [PSF license](https://docs.python.org/3.11/license.html);
  the original embedded package's `LICENSE.txt` is retained.
- CriCodecs is published by [Youjose](https://github.com/Youjose/CriCodecs).
  The inspected 1.2.0 wheel metadata does not specify redistribution terms.
  The full edition preserves the original package files and metadata without
  modifying the codec. For content uploaded without an Included License,
  [PyPI Terms of Service, User-Generated Content section 4](https://policies.python.org/pypi.org/Terms-of-Service/#4-license-grant-to-us)
  grants users reproduction and distribution rights. This is the basis used for
  redistributing the original PyPI wheel, not an MIT/GPL license for the codec.
  The original, SHA256-verified wheel is provided alongside the application in
  the release. See [distribution details](third-party/cricodecs/DISTRIBUTION.md).

`Publish-Portable.ps1 -Zip` builds the full-runtime edition and fails if required
components or notices are missing. Keep all original notices and source
directions when mirroring it. Changes to third-party code or versions require a
new review of their terms; the project's own license does not relicense these
components. `-WithoutVideoRuntime` explicitly builds the standard edition with
first-use downloads. Never describe that standard archive as containing the
video binaries.

## Scope of verification

Component installation, conversion, and local preview tests are separate from
game compatibility. USM container roundtrips and native decoder preparation have
been tested, but not all in-game startup/playback cases or Android decoding.
The independent-portrait/card patch remains experimental.

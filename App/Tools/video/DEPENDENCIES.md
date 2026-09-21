# Local video conversion runtime

This preview uses locally provisioned binaries, excluded from Git:

- FFmpeg 8.1.1 essentials, Gyan build (GPL-enabled): https://www.gyan.dev/ffmpeg/builds/
- Python 3.11.9 Windows x64 embedded distribution (PSF license, included in python/LICENSE.txt): https://www.python.org/downloads/release/python-3119/
- CriCodecs 1.2.0 cp311-win_amd64: https://pypi.org/project/cricodecs/1.2.0/
  Source: https://github.com/Youjose/CriCodecs

CriCodecs redistribution terms were not present in the inspected package metadata.
Do not publicly redistribute this runtime bundle until those terms are confirmed
and the FFmpeg license/source distribution obligations are satisfied.

Expected local layout: ffmpeg.exe, mux.py, python/python.exe and the cricodecs
package inside python/. The application does not download or install dependencies
at runtime. In preview.9 portable packages these files live under data/Tools/video/;
the top-level launcher loads the application and its runtime from data/.
Keep the launcher and the complete data directory together when copying the tool.

Build the local full package with scripts/Publish-Portable.ps1 -Zip. For source-only
development without these binaries, use -WithoutVideoRuntime; that developer
package cannot convert video and must not be advertised as a full-function release.

USM color/alpha payload roundtrip and Windows/Android asset repacking are tested.
The local Windows game's CRI decoder parsed the converted clip (397 frames,
30 FPS, one alpha stream) and completed preparation to Ready in a separate
test process. Full in-game loading/rendering and Android decoding remain unverified.

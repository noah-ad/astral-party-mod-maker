"""Read native CRI timing with the same decoder used by the mux verifier."""
import json
import pathlib
import sys
from cricodecs import usm, video

source, destination = map(pathlib.Path, sys.argv[1:3])
movie = usm.load(str(source))
streams = {stream.stream_id: i for i, stream in enumerate(movie.streams)}
if usm.UsmChunkType.SFV not in streams:
    raise RuntimeError("Native movie has no color video")
elementary = destination.with_suffix(".m1v")
reader = None
try:
    elementary.write_bytes(movie.stream_bytes(streams[usm.UsmChunkType.SFV]))
    reader = video.MpegVideoReader.load(str(elementary))
    result = {
        "Width": reader.sequence_header.width,
        "Height": reader.sequence_header.height,
        "FramerateN": reader.frame_rate[0],
        "FramerateD": reader.frame_rate[1],
        "TotalFrames": reader.frame_count,
        "HasAlpha": usm.UsmChunkType.ALP in streams,
        "HasAudio": usm.UsmChunkType.SFA in streams,
    }
    destination.write_text(json.dumps(result), encoding="utf-8")
finally:
    reader = None
    elementary.unlink(missing_ok=True)

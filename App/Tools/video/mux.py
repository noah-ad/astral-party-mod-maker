"""Local USM mux adapter. Inputs are produced by the bundled FFmpeg, never executed."""
import hashlib
import pathlib
import sys
import os
import json
from cricodecs import usm, video as video_codec

video, alpha, destination = map(pathlib.Path, sys.argv[1:4])
if not (video.parent == alpha.parent == destination.parent):
    raise RuntimeError("Mux files must share a work directory")
os.chdir(video.parent)
video, alpha, destination = map(lambda p: pathlib.Path(p.name), (video, alpha, destination))
config = usm.UsmMuxConfig(str(video), alpha_path=str(alpha))
usm.mux(config, str(destination))
movie = usm.load(str(destination))
streams = {stream.stream_id: i for i, stream in enumerate(movie.streams)}
for stream_id, source in ((usm.UsmChunkType.SFV, video), (usm.UsmChunkType.ALP, alpha)):
    if stream_id not in streams:
        raise RuntimeError("Required color/alpha stream missing from USM")
    rebuilt = movie.stream_bytes(streams[stream_id])
    if hashlib.sha256(rebuilt).digest() != hashlib.sha256(source.read_bytes()).digest():
        raise RuntimeError("USM payload verification failed")
print("USM color and alpha verified")
reader = video_codec.MpegVideoReader.load(str(video))
alpha_reader = video_codec.MpegVideoReader.load(str(alpha))
if (reader.sequence_header.width, reader.sequence_header.height, reader.frame_count, reader.frame_rate) != (
        alpha_reader.sequence_header.width, alpha_reader.sequence_header.height, alpha_reader.frame_count, alpha_reader.frame_rate):
    raise RuntimeError("Color and alpha geometry or timing differ")
metadata = {
    "Width": reader.sequence_header.width, "Height": reader.sequence_header.height,
    "FramerateN": reader.frame_rate[0], "FramerateD": reader.frame_rate[1], "TotalFrames": reader.frame_count,
    "Sha256": hashlib.sha256(destination.read_bytes()).hexdigest()
}
pathlib.Path(str(destination) + ".json").write_text(json.dumps(metadata), encoding="utf-8")

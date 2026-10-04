"""Builds the About screen easter egg's data from the Bad Apple!! PV.

    python3 tools/BadApple/build.py <video file>

Writes src/PKForge.App/Resources/BadApple/frames.bin and audio.m4a. Needs ffmpeg.

frames.bin: "BAPL", then width, height, fps and frame count as little-endian uint16, then a
gzip stream of every frame as 1-bit rows (MSB first, 1 = white), each XORed with the frame
before it so still parts compress to nothing.
"""
import gzip
import pathlib
import struct
import subprocess
import sys

import numpy as np

WIDTH, HEIGHT, FPS = 128, 96, 30
OUT = pathlib.Path(__file__).resolve().parents[2] / "src/PKForge.App/Resources/BadApple"


def main(video: str) -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    raw = subprocess.run(
        ["ffmpeg", "-v", "error", "-i", video, "-vf", f"fps={FPS},scale={WIDTH}:{HEIGHT}:flags=area,format=gray",
         "-f", "rawvideo", "-"], check=True, capture_output=True).stdout
    frames = np.frombuffer(raw, np.uint8).reshape(-1, HEIGHT, WIDTH) >= 128
    packed = np.packbits(frames.reshape(len(frames), -1), axis=1)
    delta = packed.copy()
    delta[1:] ^= packed[:-1]
    header = b"BAPL" + struct.pack("<4H", WIDTH, HEIGHT, FPS, len(frames))
    (OUT / "frames.bin").write_bytes(header + gzip.compress(delta.tobytes(), 9, mtime=0))
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", video, "-vn", "-ac", "1", "-c:a", "aac", "-b:a", "64k",
                    "-map_metadata", "-1", str(OUT / "audio.m4a")], check=True)
    print(f"{len(frames)} frames, {(OUT / 'frames.bin').stat().st_size} + {(OUT / 'audio.m4a').stat().st_size} bytes")


if __name__ == "__main__":
    main(sys.argv[1])

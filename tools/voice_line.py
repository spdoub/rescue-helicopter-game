#!/usr/bin/env python3
"""Generate a voice line with ElevenLabs and apply the aviation-radio filter.

Usage:
  python3 tools/voice_line.py "Uh, is this thing even rated for grandmas?" out_name [--voice VOICE_ID] [--clean]

Writes RescueGame/Assets/Audio/Voice/<out_name>.wav (radio-filtered) and, with --clean,
<out_name>_clean.wav too. Reads ELEVENLABS_API_KEY from the repo's .env.
"""
import argparse
import json
import os
import subprocess
import sys
import tempfile
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OUT_DIR = ROOT / "RescueGame" / "Assets" / "Audio" / "Voice"
DEFAULT_VOICE = "JBFqnCBsd6RMkjVDRZzb"  # ElevenLabs premade "George"

# Cheap aviation radio: narrow band-pass, 8 kHz telephone sample rate, overdriven and crushed,
# with a bed of hiss/crackle that runs under the voice plus a squelch burst at each end.
RADIO_FILTER = (
    "[0:a]aformat=channel_layouts=mono,apad=pad_dur=0.25,adelay=180,"
    "highpass=f=600,highpass=f=600,lowpass=f=2400,lowpass=f=2400,"
    "aresample=8000,aresample=44100,"
    "acompressor=threshold=-30dB:ratio=12:attack=1:release=40:makeup=10,"
    "volume=2.2,asoftclip=type=hard:threshold=0.6,"
    "acrusher=bits=6:mix=0.45:mode=log,"
    "equalizer=f=1500:t=q:w=1.5:g=6[v];"
    "anoisesrc=color=white:amplitude=0.07:seed=7,highpass=f=900,lowpass=f=3500,"
    "tremolo=f=11:d=0.6[hiss];"
    "anoisesrc=color=white:amplitude=0.5:seed=3:duration=0.18,highpass=f=1200,"
    "afade=t=out:st=0.05:d=0.13[sq1];"
    "anoisesrc=color=white:amplitude=0.5:seed=5:duration=0.18,highpass=f=1200,"
    "afade=t=out:st=0.05:d=0.13[sq2];"
    "[sq1][v][sq2]concat=n=3:v=0:a=1[vs];"
    "[vs][hiss]amix=inputs=2:duration=first:normalize=0,alimiter=limit=0.9[out]"
)


def load_key() -> str:
    key = os.environ.get("ELEVENLABS_API_KEY")
    env = ROOT / ".env"
    if not key and env.exists():
        for line in env.read_text().splitlines():
            if line.startswith("ELEVENLABS_API_KEY="):
                key = line.split("=", 1)[1].strip()
    if not key:
        sys.exit("ELEVENLABS_API_KEY missing: add it to .env")
    return key


def tts(text: str, voice: str, key: str) -> bytes:
    req = urllib.request.Request(
        f"https://api.elevenlabs.io/v1/text-to-speech/{voice}?output_format=mp3_44100_128",
        data=json.dumps({"text": text, "model_id": "eleven_multilingual_v2"}).encode(),
        headers={"xi-api-key": key, "Content-Type": "application/json", "Accept": "audio/mpeg"},
    )
    with urllib.request.urlopen(req) as r:
        return r.read()


def to_wav(src: Path, dst: Path, radio: bool) -> None:
    cmd = ["ffmpeg", "-y", "-loglevel", "error", "-i", str(src)]
    if radio:
        cmd += ["-filter_complex", RADIO_FILTER, "-map", "[out]"]
    subprocess.run(cmd + ["-ac", "1", "-ar", "44100", str(dst)], check=True)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("text")
    ap.add_argument("name")
    ap.add_argument("--voice", default=DEFAULT_VOICE)
    ap.add_argument("--clean", action="store_true", help="also save an unfiltered copy")
    ap.add_argument("--no-radio", action="store_true", help="in-person voice: save only the unfiltered line as <name>.wav")
    ap.add_argument("--out-dir", default=None, help="folder for the .wav (default Assets/Audio/Voice)")
    a = ap.parse_args()

    out = Path(a.out_dir) if a.out_dir else OUT_DIR
    out.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(suffix=".mp3") as mp3:
        mp3.write(tts(a.text, a.voice, load_key()))
        mp3.flush()
        to_wav(Path(mp3.name), out / f"{a.name}.wav", radio=not a.no_radio)
        if a.clean and not a.no_radio:
            to_wav(Path(mp3.name), out / f"{a.name}_clean.wav", radio=False)
    print(out / f"{a.name}.wav")


if __name__ == "__main__":
    main()

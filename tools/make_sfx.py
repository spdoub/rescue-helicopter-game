#!/usr/bin/env python3
"""Game sound effects via the ElevenLabs sound-effects API -> RescueGame/Assets/Audio/SFX/<name>.wav

  python3 tools/make_sfx.py            # make any that are missing
  python3 tools/make_sfx.py --force    # remake all
Loops are requested as seamless loops.
"""
import json, subprocess, sys, tempfile, urllib.request, urllib.error
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "RescueGame" / "Assets" / "Audio" / "SFX"
SFX = {
    "rotor_loop": ("Medium helicopter hovering close by, steady deep rotor blade thump and turbine whine, constant, no fade", 6.0, True),
    "winch_motor": ("Electric rescue winch motor running, steady whirring whine with cable rattling on a drum, constant", 3.0, True),
    "door_slide": ("Heavy metal helicopter sliding door rolling on its track then clunking shut", 1.6, False),
    "hook_clank": ("Heavy steel rescue hook clanking against metal, single hit", 0.8, False),
    "delivered_jingle": ("Short cheerful cartoon success jingle, bouncy brass and xylophone, cash register ding at the end", 2.5, False),
}


def key():
    for line in (ROOT / ".env").read_text().splitlines():
        if line.startswith("ELEVENLABS_API_KEY="): return line.split("=", 1)[1].strip()
    sys.exit("no ELEVENLABS_API_KEY")


def gen(text, seconds, loop):
    body = {"text": text, "duration_seconds": seconds, "prompt_influence": 0.5}
    if loop: body["loop"] = True
    req = urllib.request.Request("https://api.elevenlabs.io/v1/sound-generation", data=json.dumps(body).encode(),
                                 headers={"xi-api-key": key(), "Content-Type": "application/json", "Accept": "audio/mpeg"})
    try:
        return urllib.request.urlopen(req).read()
    except urllib.error.HTTPError as e:
        if loop and e.code in (400, 422):  # older API without looping: try without
            return gen(text, seconds, False)
        raise


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    for name, (text, secs, loop) in SFX.items():
        dst = OUT / f"{name}.wav"
        if dst.exists() and "--force" not in sys.argv: continue
        with tempfile.NamedTemporaryFile(suffix=".mp3") as mp3:
            mp3.write(gen(text, secs, loop)); mp3.flush()
            subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", mp3.name, "-ac", "1", "-ar", "44100", str(dst)], check=True)
        print(dst)


if __name__ == "__main__":
    main()

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
    "dog_bark_1": ("Small scruffy terrier dog, two excited high yappy barks, close up, no background noise", 1.2, False),
    "dog_bark_2": ("Small terrier dog, one happy bark then a little growly play bark, close up", 1.2, False),
    "dog_whine": ("Small dog whining and whimpering impatiently, short, close up", 1.5, False),
    "crash_thud": ("Helicopter hull slamming into a brick wall, heavy metal crunch and thud, short", 1.2, False),
    "rotor_strike": ("Helicopter rotor blades striking concrete, violent clattering metal chopping sparks, short", 1.2, False),
    "damage_alarm": ("Cockpit warning alarm, urgent repeating electronic beep beep beep, constant", 2.0, True),
    "city_ambience": ("Distant city street ambience on a warm evening, light traffic hum, a far-away car horn, a faint siren far off, a few pigeons, steady and calm", 12.0, True),
    "car_horn": ("Single short car horn honk from a small car, city street, close", 0.8, False),
    "bird_splat": ("A bird hitting a helicopter windscreen: wet thump, a squawk cut short, feathers, close and sudden", 0.9, False),
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

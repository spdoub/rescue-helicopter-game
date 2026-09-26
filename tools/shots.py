#!/usr/bin/env python3
"""Real in-game screenshots (windowed build, host mode) from chosen camera spots.

  python3 tools/shots.py                 # all named shots -> Screenshots/<name>.png
  python3 tools/shots.py grandma_roof    # just one
Each shot: -shot <file> -shotat x,y,z,yaw,pitch[,fov] (free camera) or a view of your own crew member.
"""
import subprocess, sys, time
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
APP = ROOT / "Builds/Mac/RescueGame.app/Contents/MacOS/RescueGame"
OUT = ROOT / "Screenshots"

SHOTS = {
    # name: extra args
    "grandma_roof": ["-shotat", "17.5,10.6,62.0,-60,14,55", "-shotdelay", "7"],
    "recliner_roof": ["-shotat", "-56.5,8.8,26.0,-60,15,55", "-shotdelay", "7"],
    "dogperson_roof": ["-shotat", "-66.5,23.4,3.0,-118,18,55", "-shotdelay", "8"],
    "backseat_roof": ["-shotat", "60.5,26.5,3.2,120,15,55", "-shotdelay", "7"],
    "city_overview": ["-shotat", "-10,38,-35,20,18,60", "-shotdelay", "6"],
    "hospital": ["-shotat", "-14,9,-26,235,20,55", "-shotdelay", "6"],
    "helipad_start": ["-shotview", "fp", "-shotpitch", "5", "-shotdelay", "6"],
    "cockpit": ["-takepilot", "-shotview", "fp", "-shotpitch", "8", "-shotdelay", "7"],
    "cabin_winch": ["-shotat", "-0.3,2.1,-0.2,60,12,70", "-shotdelay", "6"],
}


def main():
    OUT.mkdir(exist_ok=True)
    names = sys.argv[1:] or list(SHOTS)
    for n in names:
        f = OUT / f"{n}.png"
        if f.exists(): f.unlink()
        subprocess.run(["pkill", "-f", "RescueGame.app/Contents/MacOS"], capture_output=True)
        time.sleep(0.5)
        p = subprocess.Popen([str(APP), "-screen-fullscreen", "0", "-screen-width", "1600", "-screen-height", "900",
                              "-host", "-shot", str(f)] + SHOTS[n], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        try: p.wait(timeout=60)
        except subprocess.TimeoutExpired: p.kill()
        print(n, "ok" if f.exists() else "MISSING")


if __name__ == "__main__":
    main()

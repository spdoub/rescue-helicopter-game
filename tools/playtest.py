#!/usr/bin/env python3
"""
Automated playtests for Rescue Helicopter.

Launches the built game (Builds/Mac/RescueGame.app) with bot players, collects the bug reports
written by PlaytestDirector, and turns recordings into things you can look at:
  - sheet.png   contact sheet: frames laid out in time order with timestamps
  - onion.png   onion-skin: consecutive frames stacked see-through, so motion shows as trails
  - motion.mp4  the recording as a video

Usage:
  python3 tools/playtest.py run walk --record --view side
  python3 tools/playtest.py run board --client-pilot tour
  python3 tools/playtest.py suite            # everything, no recording
Results land in Playtests/<time>_<name>/ and a summary is printed.
"""
import argparse, glob, os, subprocess, sys, time
from datetime import datetime

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
GAME = os.path.join(ROOT, "Builds/Mac/RescueGame.app/Contents/MacOS/RescueGame")
OUT = os.path.join(ROOT, "Playtests")


def kill_games():
    subprocess.run(["pkill", "-f", "RescueGame.app/Contents/MacOS/RescueGame"], capture_output=True)
    time.sleep(1)


def launch(out, args, window, log):
    os.makedirs(out, exist_ok=True)
    cmd = [GAME]
    if window:
        cmd += ["-screen-fullscreen", "0", "-screen-width", "960", "-screen-height", "540"]
    else:
        cmd += ["-batchmode", "-nographics"]
    cmd += ["-out", out, "-logFile", os.path.join(out, log)] + args
    return subprocess.Popen(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def run(name, scenario=None, pilot=None, client_scenario=None, client_pilot=None, duration=30,
        record=False, view="side", fps=15, seed=1, take_pilot_client=False, window=False):
    kill_games()
    stamp = datetime.now().strftime("%m%d-%H%M%S")
    base = os.path.join(OUT, f"{stamp}_{name}")
    host_args = ["-duration", str(duration), "-seed", str(seed)]
    if scenario: host_args += ["-scenario", scenario]
    if pilot: host_args += ["-pilotbot", pilot]
    if record: host_args += ["-record", "-recfps", str(fps), "-recview", view]
    # A window is needed for recording and for animation measurements (headless mode doesn't animate).
    procs = [launch(os.path.join(base, "host"), host_args, True, "host.log")]

    two = client_scenario or client_pilot or take_pilot_client
    if two:
        time.sleep(4)
        c_args = ["-client", "127.0.0.1", "-duration", str(max(5, duration - 5)), "-seed", str(seed + 1)]
        if client_scenario: c_args += ["-scenario", client_scenario]
        if client_pilot: c_args += ["-pilotbot", client_pilot]
        if take_pilot_client: c_args += ["-takepilot"]
        procs.append(launch(os.path.join(base, "client"), c_args, False, "client.log"))

    deadline = time.time() + duration * (6 if record else 1) + 60
    for p in procs:
        try:
            p.wait(timeout=max(1, deadline - time.time()))
        except subprocess.TimeoutExpired:
            p.kill()
    if record:
        make_media(os.path.join(base, "host"), fps)
    return base


def make_media(d, fps):
    frames = sorted(glob.glob(os.path.join(d, "frames", "f*.png")))
    if not frames:
        return
    from PIL import Image, ImageDraw
    # contact sheet: up to 48 frames, 8 per row
    step = max(1, len(frames) // 48)
    pick = frames[::step][:48]
    tw, th, cols = 320, 180, 8
    rows = (len(pick) + cols - 1) // cols
    sheet = Image.new("RGB", (tw * cols, th * rows), "black")
    for k, f in enumerate(pick):
        im = Image.open(f).convert("RGB").resize((tw, th))
        idx = frames.index(f)
        ImageDraw.Draw(im).text((6, 4), f"{idx / fps:.2f}s", fill="white")
        sheet.paste(im, ((k % cols) * tw, (k // cols) * th))
    sheet.save(os.path.join(d, "sheet.png"))
    # onion skins: 6 panels, each blends 6 consecutive frames (motion trails)
    panels, per = [], 6
    starts = [int(i * (len(frames) - per) / 5) for i in range(6)] if len(frames) > per else [0]
    for s0 in starts:
        acc = None
        group = frames[s0:s0 + per]
        for j, f in enumerate(group):
            im = Image.open(f).convert("RGB").resize((480, 270))
            acc = im if acc is None else Image.blend(acc, im, 1.0 / (j + 1))
        ImageDraw.Draw(acc).text((6, 4), f"{s0 / fps:.2f}-{(s0 + per) / fps:.2f}s", fill="white")
        panels.append(acc)
    onion = Image.new("RGB", (480 * 3, 270 * ((len(panels) + 2) // 3)), "black")
    for k, p in enumerate(panels):
        onion.paste(p, ((k % 3) * 480, (k // 3) * 270))
    onion.save(os.path.join(d, "onion.png"))
    subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-framerate", str(fps), "-i",
                    os.path.join(d, "frames", "f%04d.png"), "-pix_fmt", "yuv420p",
                    "-vf", "scale=960:-2", os.path.join(d, "motion.mp4")])


def summarize(base):
    lines = []
    for role in ("host", "client"):
        rep = os.path.join(base, role, "report.txt")
        if os.path.exists(rep):
            with open(rep) as f:
                text = f.read()
            lines.append(f"[{role}] " + text.split("--- timeline ---")[0].strip().replace("\n", f"\n[{role}] "))
        elif os.path.isdir(os.path.join(base, role)):
            lines.append(f"[{role}] NO REPORT (crashed or hung?) - see {role}.log")
    return "\n".join(lines)


SUITE = [
    dict(name="exitjump", scenario="exitjump", duration=45),
    dict(name="door", scenario="door", duration=35),
    dict(name="winch", scenario="winch", duration=30),
    dict(name="rescue", scenario="rescue_crew", client_pilot="rescue", take_pilot_client=True, duration=200),
    dict(name="walk", scenario="walk", duration=36, window=True),
    dict(name="board_and_fly", scenario="board", pilot="tour", duration=40),
    dict(name="passenger_aggressive", scenario="passenger", client_pilot="aggressive", take_pilot_client=True, duration=40),
    dict(name="jumpoff", scenario="jumpoff", client_pilot="takeoff-later", take_pilot_client=True, duration=35),
    dict(name="chaos_host", scenario="chaos", client_scenario="chaos", duration=60, seed=7),
    dict(name="chaos_2", scenario="chaos", client_scenario="chaos", duration=60, seed=42),
]


def main():
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    r = sub.add_parser("run")
    r.add_argument("scenario")
    r.add_argument("--pilot")
    r.add_argument("--client-scenario")
    r.add_argument("--client-pilot")
    r.add_argument("--client-takes-pilot", action="store_true")
    r.add_argument("--duration", type=int, default=30)
    r.add_argument("--record", action="store_true")
    r.add_argument("--window", action="store_true", help="real window, no screenshots (animation telemetry)")
    r.add_argument("--view", default="side")
    r.add_argument("--fps", type=int, default=15)
    r.add_argument("--seed", type=int, default=1)
    sub.add_parser("suite")
    a = ap.parse_args()

    if a.cmd == "run":
        base = run(a.scenario + ("_rec" if a.record else "_win" if a.window else ""), a.scenario, a.pilot, a.client_scenario,
                   a.client_pilot, a.duration, a.record, a.view, a.fps, a.seed, a.client_takes_pilot, a.window)
        print(base)
        print(summarize(base))
    else:
        for t in SUITE:
            base = run(**t)
            print(f"=== {t['name']}  ({base})")
            print(summarize(base))
    kill_games()


if __name__ == "__main__":
    main()

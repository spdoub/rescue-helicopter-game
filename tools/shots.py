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
    "grandma_fire": ["-shotat", "17.5,10.6,62.0,-60,14,55", "-fire", "1", "-shotdelay", "40"],
    "fire_from_above": ["-shotat", "-8,30,40,41,32,60", "-fire", "1", "-shotdelay", "45"],
    "upgrade_board": ["-shotat", "-4.5,1.7,1.2,-70,4,60", "-money", "3450", "-upgrades", "1,0,0,2", "-shotdelay", "7"],
    "heli_upgraded": ["-shotat", "7.5,3.2,-6.5,-40,10,55", "-upgrades", "2,2,2,2", "-shotdelay", "7"],
    "heli_upgraded_side": ["-shotat", "9,2.2,0.5,-90,4,60", "-upgrades", "2,2,2,2", "-shotdelay", "7"],
    "heli_upgraded_tail": ["-shotat", "5,3.2,-8,-60,12,60", "-upgrades", "2,2,2,2", "-shotdelay", "7"],
    "heli_stock": ["-shotat", "7.5,3.2,-6.5,-40,10,55", "-shotdelay", "7"],
    "recliner_fire": ["-shotat", "-56.5,8.8,26.0,-60,15,55", "-fire", "1", "-shotdelay", "40"],
    "ledge_resident": ["-shotnear", "LedgeResident,7,1.2,2.5", "-shotdelay", "7"],
    "ledge_wide": ["-shotnear", "LedgeResident,22,6,6", "-shotdelay", "7"],
    "heli_front": ["-shotat", "0,2.2,9,180,6,50", "-shotdelay", "6"],
    "heli_front_quarter": ["-shotat", "7,2.6,8,220,10,50", "-shotdelay", "6"],
    "heli_side_r": ["-shotat", "9.5,2.0,0.3,-90,6,50", "-shotdelay", "6"],
    "heli_rear": ["-shotat", "0,2.4,-11,0,8,50", "-shotdelay", "6"],
    "heli_rear_quarter": ["-shotat", "-7,3,-8,40,14,50", "-shotdelay", "6"],
    "heli_top": ["-shotat", "0,14,0.5,0,88,50", "-shotdelay", "6"],
    "heli_door_close": ["-shotat", "3.2,1.8,0.6,-90,5,60", "-shotdelay", "6"],
    "cockpit_seat": ["-shotat", "0.8,1.6,0.2,0,18,70", "-shotdelay", "6"],
    "cockpit_side": ["-shotat", "-0.9,1.7,1.0,90,20,70", "-shotdelay", "6"],
    "cabin_seats": ["-shotat", "-0.9,1.8,-0.4,60,25,75", "-shotdelay", "6"],
    "cabin_floor": ["-shotat", "0,2.2,1.5,180,55,75", "-shotdelay", "6"],
    "heli_door_open": ["-shotat", "3.4,1.9,0.5,-90,4,60", "-shotdelay", "6"],
    "heli_hero": ["-shotat", "6.5,2.4,7.5,222,9,45", "-shotdelay", "6"],
    "heli_tail_close": ["-shotat", "3.2,2.6,-4.5,-70,8,50", "-shotdelay", "6"],
    "heli_low_side": ["-shotat", "-6.5,1.2,2.5,105,2,45", "-shotdelay", "6"],
    "cockpit_from_cabin": ["-shotat", "0.1,1.75,0.6,0,14,68", "-shotdelay", "6"],
    "pilot_seat_close": ["-shotat", "-0.3,1.75,1.15,55,20,60", "-shotdelay", "6"],
    "heli_top_close": ["-shotat", "1.5,8.5,0.6,0,84,45", "-shotdelay", "6"],
    "heli_side_closed": ["-shotat", "9.5,2.0,0.3,-90,6,50", "-doorsclosed", "-shotdelay", "6"],
    "heli_hero_closed": ["-shotat", "6.5,2.4,7.5,222,9,45", "-doorsclosed", "-shotdelay", "6"],
    "cockpit_flying": ["-takepilot", "-pilotbot", "tour", "-shotview", "fp", "-shotpitch", "10", "-shotyaw", "0", "-shotdelay", "9"],
    "cockpit_flying_down": ["-takepilot", "-pilotbot", "tour", "-shotview", "fp", "-shotpitch", "40", "-shotyaw", "0", "-shotdelay", "9"],
    "cockpit_collective": ["-takepilot", "-pilotbot", "tour", "-shotview", "fp", "-shotpitch", "55", "-shotyaw", "-50", "-shotdelay", "8"],
    "dispatch_welcome": ["-shotview", "fp", "-shotpitch", "6", "-shotdelay", "6"],
    "cabin_pizza": ["-shotat", "0.1,1.6,0.6,-110,30,60", "-shotdelay", "7"],
    "cockpit_rain": ["-takepilot", "-weather", "gusty", "-shotview", "fp", "-shotpitch", "6", "-shotyaw", "0", "-shotdelay", "14"],
    "heli_rain": ["-shotat", "7,2.6,8,220,10,50", "-weather", "gusty", "-shotdelay", "10"],
    "cockpit_map": ["-shotat", "-0.35,1.85,1.9,-15,18,60", "-shotdelay", "7"],
    "cabin_map": ["-shotat", "0.2,1.9,0.55,25,2,55", "-shotdelay", "7"],
    "cockpit_left": ["-takepilot", "-shotview", "fp", "-shotpitch", "12", "-shotyaw", "-50", "-shotdelay", "7"],
    "island_overview": ["-shotat", "0,420,-1100,0,22,60", "-shotdelay", "8"],
    "island_north": ["-shotat", "0,380,1150,180,20,60", "-shotdelay", "8"],
    "island_beach": ["-shotat", "-60,12,-300,150,6,60", "-shotdelay", "8"],
    "island_cliffs": ["-shotat", "-700,40,-150,60,8,60", "-shotdelay", "8"],
    "island_mountains": ["-shotat", "-120,120,180,20,5,60", "-shotdelay", "8"],
    "island_lake": ["-shotat", "330,30,60,60,14,60", "-shotdelay", "8"],
    "island_from_pad": ["-shotat", "10,3,-20,0,-4,60", "-shotdelay", "8"],
    "island_road_south": ["-shotat", "70,6,-250,200,8,60", "-shotdelay", "8"],
    "island_gondola": ["-shotat", "40,120,350,30,14,60", "-shotdelay", "8"],
    "island_gondola_close": ["-shotat", "115,155,395,60,18,50", "-shotdelay", "8"],
    "wild_gondola": ["-startwild", "6", "-shotnear", "Grandma,12,4,5", "-shotdelay", "7"],
    "island_pier_boat": ["-shotat", "40,8,-405,120,10,55", "-shotdelay", "8"],
    "island_sheep": ["-shotat", "-10,32,250,45,22,55", "-shotdelay", "8"],
    "cliff_station": ["-shotat", "-405,24,-83,225,18,55", "-shotdelay", "8"],
    "island_lighthouse": ["-shotat", "-470,45,-230,225,12,50", "-shotdelay", "8"],
    "wild_beach": ["-startwild", "1", "-shotnear", "Grandma,9,2,3", "-shotdelay", "7"],
    "wild_mountain": ["-startwild", "2", "-shotnear", "Grandma,12,3,4", "-shotdelay", "7"],
    "wild_cliff": ["-startwild", "3", "-shotnear", "Grandma,12,3,4", "-shotdelay", "7"],
    "wild_lake": ["-startwild", "4", "-shotnear", "Grandma,10,2.5,4", "-shotdelay", "7"],
    "street_parked": ["-shotat", "62,1.7,-58,-125,4,60", "-shotdelay", "8"],
    "street_level": ["-shotat", "14,1.7,-30,-35,4,60", "-shotdelay", "8"],
    "street_level2": ["-shotat", "-40,1.6,52,120,3,60", "-shotdelay", "8"],
    "street_corner": ["-shotat", "36,6,36,225,12,60", "-shotdelay", "8"],
    "city_from_air": ["-shotat", "20,40,-20,-30,35,60", "-shotdelay", "8"],
    "rotor_t4": ["-shotat", "1.5,8.5,0.6,0,84,45", "-shotdelay", "4"],
    "rotor_t9": ["-shotat", "1.5,8.5,0.6,0,84,45", "-shotdelay", "9"],
    "rotor_t14": ["-shotat", "1.5,8.5,0.6,0,84,45", "-shotdelay", "14"],
    "rotor_t20": ["-shotat", "1.5,8.5,0.6,0,84,45", "-shotdelay", "20"],
    "recliner_roof": ["-shotat", "-56.5,8.8,26.0,-60,15,55", "-shotdelay", "7"],
    "dogperson_roof": ["-shotat", "-66.5,23.4,3.0,-118,18,55", "-shotdelay", "8"],
    "backseat_roof": ["-shotat", "60.5,26.5,3.2,120,15,55", "-shotdelay", "7"],
    "city_overview": ["-shotat", "-10,38,-35,20,18,60", "-shotdelay", "6"],
    "hospital": ["-shotat", "-14,9,-26,235,20,55", "-shotdelay", "6"],
    "helipad_start": ["-shotview", "fp", "-shotpitch", "5", "-shotdelay", "6"],
    "cockpit": ["-takepilot", "-shotview", "fp", "-shotpitch", "8", "-shotyaw", "0", "-shotdelay", "7"],
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

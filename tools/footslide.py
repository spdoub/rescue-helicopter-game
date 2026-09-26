#!/usr/bin/env python3
"""
Measure planted-foot slide per movement phase from a playtest telemetry.csv (walk scenario).
Resamples to 30 Hz and measures over 0.1 s windows so physics/frame-rate aliasing doesn't matter.
+ = legs cycle too slowly for the ground speed (feet skate forward), - = too fast.
"""
import bisect, csv, math, sys
rows = list(csv.DictReader(open(sys.argv[1])))
T = [float(r['t']) for r in rows]
def at(t):
    i = min(max(bisect.bisect_left(T, t), 0), len(rows) - 1)
    return rows[i]
F = lambda r, k: float(r[k])
phases = [(2.4, 4.0, 'walk fwd'), (5.5, 7.1, 'walk back'), (8.6, 10.3, 'strafe R'), (12.8, 14.4, 'diag'), (15.0, 17.0, 'sprint'), (23.7, 25.3, 'walk look down')]
W = 0.1
for a, b, name in phases:
    vals = []; spd = []
    t = a
    while t + W <= b:
        p, r = at(t), at(t + W); dt = F(r, 't') - F(p, 't')
        t += 1 / 30
        if dt <= 0: continue
        bv = ((F(r, 'px') - F(p, 'px')) / dt, (F(r, 'pz') - F(p, 'pz')) / dt); bs = math.hypot(*bv)
        if bs < 0.5: continue
        u = (bv[0] / bs, bv[1] / bs)
        for k in ('lf', 'rf'):
            o = 'rf' if k == 'lf' else 'lf'
            planted = abs(F(r, k + 'Y') - F(p, k + 'Y')) < 0.02 and F(r, k + 'Y') <= F(r, o + 'Y') + 0.01
            if planted:
                v = ((F(r, k + 'X') - F(p, k + 'X')) / dt, (F(r, k + 'Z') - F(p, k + 'Z')) / dt)
                vals.append(v[0] * u[0] + v[1] * u[1]); spd.append(bs)
    if vals:
        vals.sort()
        print(f"{name:15s} body {sum(spd)/len(spd):.2f} m/s  planted-foot slide: median {vals[len(vals)//2]:+.2f} m/s  (n={len(vals)})")
    else:
        print(f"{name:15s} no planted-foot samples")

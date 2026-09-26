# Rescue Helicopter Game — PRD

Source of truth: https://claude.ai/code/artifact/863a7678-02a0-4692-8eb4-ba326f405e78 (this file is a local copy, 2026-09-25)

## Overview

A 2–4 player online co-op physics game for Steam where a crew of floppy idiots runs a sketchy search-and-rescue helicopter company. Human Fall Flat and Gang Beasts energy, but the chaos comes from flying a helicopter together and dealing with ridiculous survivors.

**Premise.** The players are the bosses. They started their own rescue company with a taped-together helicopter. People pay them when they get dropped off safely, and the money goes into a better helicopter, a better hangar and better gear.

**Working title options** (not final): 1-800-UH-OH, Probably Rescue, Hang On Rescue, Chopper Dudes Emergency Services.

**Platform:** PC (Steam), online co-op. Engine: Unity.

**Design pillars**
- Fun in the moment. One round or ten; no story to follow.
- Mutual dependence. The pilot flies, the crew sees and does everything else. Nobody succeeds alone.
- Survivors and conditions are the comedy. Weird people plus bad weather plus a bad helicopter.
- Readable chaos. Simple shapes and clear silhouettes so players can describe what they see fast.
- Streamable. Every mission should produce at least one clip-worthy moment.

## Core gameplay loop

Take a call, fly out, rescue as many people as you can, drop them off, get paid, upgrade, repeat.

1. **Hangar.** Pick a helicopter sized to your crew, spend money on upgrades, pick the next call from the job board.
2. **Fly out.** The pilot flies; the crew calls out directions over the radio.
3. **Rescue.** Hover, winch, grab, drag and wrangle survivors into the cabin while conditions get worse.
4. **Triage.** More people than seats or time. Decide who goes first.
5. **Drop off and get paid.** Payout depends on who you saved, how fast, and what state they arrived in.
6. **Back to the hangar.** Upgrade and take the next call.

A mission should run roughly 3–8 minutes so a session can be one mission or a whole evening.

## Roles and player scaling

There is always exactly one pilot. Everyone else is crew, and the crew's jobs split across however many people show up.

| Crew size | Who does what |
| --- | --- |
| 2 players | Pilot + one crew member who does everything: spotting, winch, survivors. Frantic by design. |
| 3 players | Pilot + spotter at the window + winch/cabin operator. |
| 4 players | Pilot + spotter + winch operator + cabin wrangler for the survivors. |

More players never makes it easier, only differently chaotic. Missions scale with crew size: more survivors, needier survivors, harder conditions.

**Helicopter choice (Sea of Thieves ship-pick model).** Before a mission the crew picks an aircraft. Bigger aircraft carry more survivors and pay more but have more stations to cover. Taking a big one with two people is a legit, hilarious challenge.

| Aircraft | Best for | Notes |
| --- | --- | --- |
| Bubble chopper | 2 players | Tiny, nimble, 1–2 survivors, cramped with more crew |
| Standard medevac | 3 players | Balanced all-rounder |
| Twin-rotor heavy lifter (Chinook-style) | 4 players | Huge cabin, slow, many stations |
| Tilt-rotor (Osprey-style) | Any, late unlock | Fast cruise, awkward hover |

## Player mechanics

**Ragdoll bodies.** Every player is a floppy physics character. Anyone can grab anything: survivors, gear, the helicopter frame, and each other. A hard bank throws unbraced crew across the cabin.

**The pilot's view can't be fully trusted.** The pilot sees the cockpit, controls and flashing buttons. Their view of the world is sometimes wrong, so they need the crew's eyes. Causes include weather, glare, fog, damage and the energy-drink jitters below.

**Consumables (background texture, not the main event).**
- Pilot: an absurd, deadly-looking gas-station energy drink. Without it he gets sluggish and everything he does slows down. Too much gives screen shake and doubled vision.
- Crew: pizza. Too hungry and they get weak, then start biting the nearest equipment, which permanently breaks it for the mission. Eating fixes it, but they later throw up, which can cover the windscreen or the pilot.
- Each player holds the other's consumable, so it has to be handed over. Taking your own teammate's item backfires: the crew member who drinks the energy drink gets super speed; the pilot who eats pizza gets fat and bounces like a ball.

These meters drift slowly. They should add a few funny moments per mission, never dominate it.

**Voice.** Everyone wears headsets. Voice chat is always on and always reaches everyone regardless of distance, with a crunchy aviation-radio filter on every voice. Occasional rare disruption: a crew member's headset gets knocked off in a hard bounce and they can't hear until they pick it up.

## Survivors

Survivors are the star of the game. Each one is a physics character with its own personality, voice lines and behaviour, and they are funny and sometimes just weird.

They act, not just talk. They grab things in the cabin, wander toward the open door, steal consumables (a survivor who drinks the energy drink goes supersonic), argue with the crew and critique the flying.

| Survivor | Behaviour |
| --- | --- |
| Recliner Guy | Won't get in unless you also winch up his very heavy recliner |
| Strong Grandma | Grabs the winch cable and pulls you down toward her |
| Ledge Resident | Lives on the ledge by choice; offended you showed up |
| Mascot | Full mascot costume, never explains himself |
| Backseat Pilot | Critiques every manoeuvre all the way to the drop-off |
| Dog Person | Refuses to leave without the dog, who runs away |

After a drop-off, the survivor gives a short reaction to how the trip went, which feeds the payout and the trophy wall.

## Missions and conditions

Missions stay interesting through three pressure dials. Each mission turns one dial high and keeps the others low.

| Dial | What it does | Examples |
| --- | --- | --- |
| Triage | More survivors than seats or time; choosing who goes first costs someone | Flooded town, crowded rooftop |
| Changing environment | The plan at minute one is wrong by minute three | Spreading fire, rising tide, collapsing roof, avalanche |
| Aircraft damage | The pilot loses control authority and improvises | Tail rotor hit, stuck gauges, broken winch |

Mission types: cliff and ledge winches, rooftop pickups, water rescues, fire drops, night rescues with a spotlight, storm flights, heavy cargo lifts.

Constant pressures on every mission: fuel as a clock, weather, and survivors who don't cooperate.

Failure should be as entertaining as success. A crash ends the mission with a funny wreck and a reduced or zero payout, then the crew is back at the hangar fast.

## Campaigns, runs and saving

| Campaign | Base | Signature conditions | Finale |
| --- | --- | --- | --- |
| Mountains | Rundown ski lodge | Avalanches, wind, cliffs | Gondola full of people stuck mid-cable |
| Tropical coast | Beach shack | Rising tides, hurricanes | Cruise ship stranded on the rocks |
| Big city | Sketchy rooftop hangar | Skyscraper fires, downdrafts, tight streets | Office party trapped on a burning top floor |
| Desert canyon | Gas station | Heat, dust storms, flash floods | TBD |

**Runs.** Start the company broke with a crappy helicopter, take calls, earn money, upgrade, push toward the finale. The run ends at the finale, on bankruptcy, or when the crew quits.

**Saving.** The host can save a run and resume it later with any crew. Only the host's save exists.

**Endless mode.** Unlocks after beating a campaign's finale. Random calls, weirder survivors, nastier conditions, no end.

**Persistent layer.** Outside of runs, players keep cosmetics, trophy-wall entries and unlocked helicopter types.

## Economy and upgrades

Survivors pay on drop-off. Payout scales with who was saved, speed, and how roughed-up they arrived. Some survivors (and their recliners) pay more, which makes triage a money decision too.

| Category | Type | Examples |
| --- | --- | --- |
| Helicopter | Functional | Stronger winch, bigger cabin, better wipers, spotlight, fuel tank, armour, stabiliser |
| Crew gear | Functional | Better harnesses, grip gloves, longer rope, better snacks |
| Hangar / base | Mostly cosmetic | Nicer base, sad vending machine, decor per campaign |
| Outfits | Cosmetic | Matching jumpsuits that get fancier as the business grows |
| Trophy wall | Cosmetic, persistent | Mementos of the weirdest survivors and worst missions |

## Art, audio and UI

**Look.** Chunky, soft, toy-like diorama style. Lumpy ragdoll characters with big hands and simple faces. Bright colours, clean silhouettes, low visual clutter.

**Helicopter.** Stubby, overloaded, taped together at the start; visibly nicer as it's upgraded. Cluttered cabin with physics props: rolling cans, pizza boxes, dangling wires.

**World.** Blocky cliffs, flat water, big readable fires and storms. Each campaign has its own palette.

**Audio.** Always-on voice with an aviation-radio filter (compression, crackle, band-pass). Loud rotor and engine layers, survivor voice lines, a cheesy company jingle on the menu.

**UI.** Minimal HUD. Pilot sees cockpit instruments and warning lights in-world. Consumable meters as small diegetic indicators. Job board and upgrade shop as physical objects in the hangar.

## Tech stack and AI pipeline

| Layer | Tool | Role |
| --- | --- | --- |
| Engine | Unity 6 (URP) | Game engine |
| Engine control | Unity MCP (AnkleBreaker Studio) | Claude drives the Unity Editor |
| 3D generation | Meshy MCP (`@meshy-ai/meshy-mcp-server`) | Models, textures, auto-rigging from prompts |
| 3D cleanup | Blender + Blender MCP | Cleanup, rigging, export |
| Networking | Netcode for GameObjects + Steam transport | Online co-op, host-based lobbies |
| Voice | Steam voice or Vivox + radio DSP filter | Always-on crew chat |
| Voices / SFX | ElevenLabs (API) | Survivor lines, announcer, jingle |
| Version control | Git + GitHub (Git LFS) | History and backup |

**Physics networking is the hardest technical risk.** Host-authoritative physics model. Prototype this first.

**Art consistency.** Every Meshy prompt uses one shared style prompt (see STYLE_PROMPT.md).

## Build plan

1. **Prototype.** Grey-box helicopter, one pilot, one crew member, online. Flying, grabbing, winch, one survivor.
2. **Vertical slice.** Mountains campaign, one helicopter, 3–4 mission types, 3 survivors, radio filter, first art pass.
3. **Core systems.** Job board, payouts, upgrades, hangar, host save, 2–4 player scaling, helicopter choice.
4. **Content.** Remaining campaigns, finales, full survivor cast, endless mode, cosmetics, trophy wall.
5. **Steam launch prep.** Store page, trailer, playtest, Early Access or full release.

## Open questions
- [ ] Final game name
- [ ] Desert campaign finale
- [ ] Solo play: AI crew or co-op only?
- [ ] Payout formula and upgrade prices
- [ ] Price point, Early Access vs full launch
- [ ] Cross-play or Steam only

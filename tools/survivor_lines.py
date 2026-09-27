#!/usr/bin/env python3
"""Every line a survivor can say, voiced with ElevenLabs (in person, no radio filter).

  python3 tools/survivor_lines.py grandma          # (re)generate all of Strong Grandma's lines

Files land in RescueGame/Assets/Audio/Survivors/<survivor>/<key>.wav; the game matches a
line to its clip by the exact text (SurvivorVoice), so keep these in sync with SurvivorBrain.cs.
"""
import subprocess, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SURVIVORS = {
    "grandma": {
        "voice": "aAsWcN5jdLdiYG7Hq0YL",  # "Harriet" - refined, bossy British (shared library)
        "lines": [
            "Yoo-hoo! Down here!",
            "Pull harder, sonny!", "I've carried heavier shopping!", "Is that all you've got?", "Put your back into it!",
            "HNNNGH!", "Come down here!", "My turn to pull!",
            "OOF! Again, but better.", "HUP!",
            "Right. Seatbelt. Where's the tea?",
            "Ooh, it's like the rollercoaster at Blackpool.", "My Harold flew smoother than this. He was a bus driver.",
            "Are we nearly there?", "I brought biscuits, don't tell the others.",
            "WATCH IT!", "I'll have your licence for this!", "My hip! My good hip!",
            "Well! That was an experience.",
            "Lovely boys. Five stars. Would get winched again.", "Very nice. Bit bumpy. Four stars.",
            "I've had worse. I've had better. Three stars.", "My hip will never forgive you. Two stars.",
            "One star. I'm telling the newspaper.",
            "Is something burning? It's not my scones!", "Hot! Hot! Hurry up, sonny!", "I'm too old to be barbecued!", "OW! My cardigan's on fire!",
            "Sand in my slippers. Marvellous.", "I only came up here for the view!", "Don't look down, Harold always said.", "The fish weren't biting anyway.", "I was picking mushrooms! Leave the basket!", "Eleven pounds for that cable car. Eleven!",
        ],
    },
    "recliner": {
        "voice": "75DchiXtNUXnu3lra8pV",  # laid-back middle-aged man
        "lines": [
            "Hey! Keep it down, I'm watching the game!", "I'm not going anywhere without my chair.", "You're blocking the signal!",
            "Careful with the chair! It's vintage.", "Is this covered by insurance?", "Don't mind me. Just reclining.",
            "Got any snacks back here?", "Nice view. Still prefer the TV.", "Can you turn it up? No, the radio.",
            "This chair's been in three floods. Never left it.",
            "MY CHAIR!", "Hey! Watch the upholstery!", "I spilled my drink!",
            "Whoa whoa whoa!", "Yeah, this'll do. Wake me when we land.", "Right here's fine. Don't touch the chair.", "Aw, come on!",
            "Five stars. Didn't spill a drop.", "Four stars. Chair's got a new squeak.", "Three stars. Missed the second half.",
            "Two stars. My remote fell out.", "One star. I'm suing. From this chair.",
            "Uh, is the roof supposed to be on fire?", "This chair is VERY flammable!", "Can't move! Reclined! Hurry!", "AAH! The footrest!",
        ],
    },
    "backseat": {
        "voice": "pNInz6obpgDQGcFmaJgB",  # "Adam" - dominant, firm: a know-it-all
        "lines": [
            "Finally! I've been timing you. Terrible.", "You're approaching too fast!", "Wind's from the west, genius!",
            "Winch speed is way too slow!", "I'd have done this in half the time.", "Don't jerk the cable! Smooth!",
            "In my day we flew with our knees.", "I have eleven hours in a simulator, you know.", "Check your mirrors. Helicopters have mirrors, right?",
            "Easy! EASY!", "Who taught you to fly? A washing machine?", "I'm writing this down.",
            "Too fast! TOO FAST!", "This isn't a race, hotshot!", "Easy on the bank angle, rookie.", "Level the wings! Or... blades!",
            "Pull up! PULL UP!", "You're scraping the rooftops!", "You call that a hover?", "Steady! Steady! Steady!",
            "Move over, let me show you how it's done!", "My aircraft!", "I've got it, I've got it!",
            "Fine! FINE. Your funeral.", "Unhand me! I'm a professional!", "See? That's how it's done.", "You're welcome.",
            "Five stars. I taught you well.", "Four stars. Almost as good as me.", "Three stars. Needed more of my input.",
            "Two stars. I'm reporting this to someone important.", "One star. I'd have crashed less.",
            "Hup! Textbook!", "Right. I'll be supervising.", "Adequate landing. Adequate.", "That was the winch's fault!",
            "Fire! Classic. You took too long.", "I'd have been here before the fire started!", "Downwind of the smoke, genius!", "My eyebrows! I'm billing you!",
            "Beach landing. Rookie stuff. I'd have done it on the water.", "Mountain rescue! Density altitude! Do you even know what that is?", "Cliff winch. Textbook. Mine would've been faster.", "A lake. You know we could have just landed, right?", "Trees! Watch the rotor! WATCH THE ROTOR!", "I was fixing the cable car. I was nearly done.",
        ],
    },
    "dogperson": {
        "voice": "FGY2WhTYpPnrIDTdsKH5",  # "Laura" - quirky, enthusiastic
        "lines": [
            "Is he okay? Is he scared? He looks scared.", "Biscuit's safe! Now me, please!",
            "Is Biscuit alright up there?", "Don't let go of him!", "Faster, it's past his dinner time!",
            "Who's a good boy? You are!", "He's never flown before. Neither have I.", "Biscuit, stop licking the pilot.",
            "BISCUIT!", "Careful! He gets sick!", "Oh no, oh no, oh no!",
            "Five stars! Biscuit loved it!", "Four stars. He only threw up once.", "Three stars. Biscuit is unsettled.",
            "Two stars. Biscuit bit someone. I don't blame him.", "One star. Biscuit will remember this.",
            "Coming, Biscuit!", "Biscuit! Oh, you brave boy!", "Come on Biscuit, walkies!", "Whoops! I'm okay!",
            "Biscuit! BISCUIT! Come here, boy!", "I'm not leaving without Biscuit!", "He likes things that hold still!", "Biscuit, no! Not the edge!",
            "Fire! Biscuit, stay away from the fire!", "Hurry! Biscuit hates fire!", "Stop, drop and roll, Biscuit!", "Ow ow ow! Hot hot hot!",
            "Biscuit chased a crab all the way here!", "He saw a goat. He HAD to.", "Biscuit's scared of heights. So am I!", "He went in after a duck. Of course he did.", "Squirrels! It's always squirrels!", "Biscuit got in the gondola on his own. I just followed.",
        ],
    },
    "ledge": {
        "voice": "2EiwWnXFnvU5JabPnv8n",  # "Clyde" - gravelly, grumpy older man
        "lines": [
            "I didn't call you!", "This is a private ledge!", "Go away, you're scaring the pigeons!",
            "Get that thing away from me!", "No! Shoo! Shoo!", "I live here, you know!",
            "Fine. FINE. But I'm not enjoying this.",
            "Mind the pigeons!", "I was perfectly happy up there!", "This is kidnapping!",
            "Eleven years on that ledge. Eleven!", "The pigeons will be wondering where I am.", "Nobody asked for this.", "My ledge had a better view.",
            "Oh, marvellous.", "Typical!", "I'm writing to the council!",
            "Five stars. Don't tell anyone I said that.", "Four stars. Still didn't ask for it.", "Three stars. The pigeons will be worried.",
            "Two stars. I want my ledge back.", "One star. I'm climbing back up tonight.",
            "Hmph!", "Don't talk to me.", "Right. How do I get back up there?", "Ow! See? This is why I stay put!",
            "Oh, that's just perfect!", "The ledge was fine until you lot showed up!", "My robe! My good robe!",
        ],
    },
}


def key_for(text: str) -> str:
    k = "".join(c if c.isalnum() else "_" for c in text.lower()).strip("_")
    while "__" in k: k = k.replace("__", "_")
    return k[:48]


def main():
    who = sys.argv[1] if len(sys.argv) > 1 else "grandma"
    s = SURVIVORS[who]
    out = ROOT / "RescueGame" / "Assets" / "Audio" / "Survivors" / who
    for line in s["lines"]:
        name = key_for(line)
        if (out / f"{name}.wav").exists() and "--force" not in sys.argv:
            continue
        subprocess.run([sys.executable, str(ROOT / "tools" / "voice_line.py"), line, name,
                        "--voice", s["voice"], "--no-radio", "--out-dir", str(out)], check=True)


if __name__ == "__main__":
    main()

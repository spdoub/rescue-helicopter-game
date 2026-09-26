# Art style: PS1 extreme-sports low-poly

**Official target image: `ArtSource/Concept/concept_3_rooftop.png`** (chosen by Sam, 2026-09-25). Match it: warm golden-hour light, soft amber haze, crisp faceted low-poly shapes with dark edge lines, painterly flat colour regions (not noisy textures), taped-up red helicopter, floppy orange-jumpsuit crew. High quality first; the PS1 flavour is a light seasoning (slight pixelation, light dither, subtle wobble), not heavy crunch.

The look: late-90s PlayStation extreme-sports games (Tony Hawk's Pro Skater 2, Crash Team Racing) crossed with low-poly flat-shaded games (TABS) and the goofy physics-party games (Human Fall Flat, Gang Beasts, Totally Reliable Delivery Service). Chunky readable shapes, bright colours, clean painterly textures, visible polygons.

## How the look is built in Unity (automatic, no per-asset work)

| Effect | Where | Setting |
| --- | --- | --- |
| Flat shading (one shade per triangle) | `RescueGame/PS1 Lit` shader | Flat Shading on |
| Vertex wobble (corners snap to a coarse grid) | `RescueGame/PS1 Lit` shader | Vertex Snap Grid 480 (lower = more wobble) |
| Soft cartoon light bands | `RescueGame/PS1 Lit` shader | Light Bands 3 |
| Affine texture warp | `RescueGame/PS1 Lit` shader | Texture Warp 0.3 on props/vehicles, 0 on big world pieces |
| Low-res pixelated render | `Assets/Settings/PC_RPAsset` | Render Scale 0.75, Upscaling Filter Point, MSAA off |
| Colour depth + ordered dither | `PS1 Dither` renderer feature on `PC_Renderer` | 64 shades per channel, dither 0.35 |
| Golden-hour light + haze | Scene lighting settings | Low warm sun (18° elevation), trilight ambient, amber linear fog 60–380 m |

**Every material must use `RescueGame/PS1 Lit`.** Textures: point filter, no mipmaps, max 512 px for heroes (helicopters, characters), 256 for props, 64 for world tiles. Grey-box world pieces use the shader's world-space texture projection so textures don't stretch.

## Meshy prompt (prepend to every generation)

> Stylised low-poly 3D game asset, modern high-quality take on late-1990s PlayStation extreme sports games. Chunky exaggerated shapes with a strong readable silhouette and crisp visible flat polygon facets. Bold saturated colours in large clean painterly regions with soft hand-painted shading, slightly goofy and toy-like. No realistic detail, no surface noise, no text or logos. Single object, centred, neutral pose.

**Characters:** add "big hands, stubby limbs, simple painted face, blocky low-poly body, T-pose for rigging".
**Helicopter parts:** add "stubby and overloaded, duct tape patches, oversized rotor hub, chunky sticker-like decals".

**Meshy settings:** use `should_remesh: true` with a low `target_polycount`, `topology: triangle`, textures at 2k (we downscale to 256–512 on import). After download, always weld (`remove_doubles`) and delete stray islands in Blender.

**Triangle budget (PS1-ish, keeps the faceted look):** props under 1.5k, characters under 3k, helicopters under 6k, set pieces under 8k.

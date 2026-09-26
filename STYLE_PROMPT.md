# Art style: PS1 extreme-sports low-poly

The look: late-90s PlayStation extreme-sports games (Tony Hawk's Pro Skater 2, Crash Team Racing) crossed with low-poly flat-shaded games (TABS) and the goofy physics-party games (Human Fall Flat, Gang Beasts, Totally Reliable Delivery Service). Chunky readable shapes, bright colours, crunchy low-res textures, visible polygons.

## How the look is built in Unity (automatic, no per-asset work)

| Effect | Where | Setting |
| --- | --- | --- |
| Flat shading (one shade per triangle) | `RescueGame/PS1 Lit` shader | Flat Shading on |
| Vertex wobble (corners snap to a coarse grid) | `RescueGame/PS1 Lit` shader | Vertex Snap Grid 220 (lower = more wobble) |
| Affine texture warp | `RescueGame/PS1 Lit` shader | Texture Warp 0.5 on props/vehicles, 0 on big world pieces |
| Low-res pixelated render | `Assets/Settings/PC_RPAsset` | Render Scale 0.4, Upscaling Filter Point, MSAA off |
| 15-bit colour + ordered dither | `PS1 Dither` renderer feature on `PC_Renderer` | 32 shades per channel |
| Distance fog | Scene lighting settings | Linear, 90–420 m, haze blue |

**Every material must use `RescueGame/PS1 Lit`.** Textures: point filter, no mipmaps, max 128 px (64 or 32 for world tiles). Grey-box world pieces use the shader's world-space texture projection so textures don't stretch.

## Meshy prompt (prepend to every generation)

> Low-poly 3D game asset in the style of a late-1990s PlayStation 1 extreme sports game. Chunky, simple, exaggerated shapes with a strong readable silhouette and visible flat polygon facets. Bright saturated colours in large simple regions, simple hand-painted low-resolution texture detail, slightly goofy and toy-like. No realistic detail, no fine surface noise, no text or logos. Single object, centred, neutral pose.

**Characters:** add "big hands, stubby limbs, simple painted face, blocky low-poly body, T-pose for rigging".
**Helicopter parts:** add "stubby and overloaded, duct tape patches, oversized rotor hub, chunky sticker-like decals".

**Meshy settings:** use `should_remesh: true` with a low `target_polycount`, `topology: triangle`, textures at 2k (we downscale to 128 on import). After download, always weld (`remove_doubles`) and delete stray islands in Blender.

**Triangle budget (PS1-ish, keeps the faceted look):** props under 1.5k, characters under 3k, helicopters under 6k, set pieces under 8k.

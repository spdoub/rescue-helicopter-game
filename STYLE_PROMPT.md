# Art style

**The one visual reference for the whole game: `ArtSource/Concept/concept_3_rooftop.png`** (golden-hour rooftop pickup, chosen by Sam 2026-09-25). Everything should look like it belongs in that image. Earlier PS1/pixelated/vinyl-toy directions are dropped.

What that image is:
- Stylised low-poly 3D: chunky, slightly exaggerated shapes with crisp visible facets.
- Clean painterly colour: big flat colour regions with soft hand-painted shading, no noise or grime textures.
- Warm golden-hour light: low sun, long soft shadows, amber haze in the distance, shadows tinted warm purple rather than grey.
- Dark edge lines around shapes (comic/illustrated feel).
- Goofy and readable: taped-up red helicopter, floppy crew in orange jumpsuits, ridiculous survivors (Recliner Guy).

## How it's built in Unity

| Part | Where | Setting |
| --- | --- | --- |
| Surface shader | `RescueGame/Stylized Lit` (`Assets/Shaders/StylizedLit.shader`) | Flat shading on, light wrap 0.35, warm purple shadow tint |
| Lighting | Scene | Low warm sun (~18°), soft shadows, trilight ambient, amber linear fog 60–380 m |
| Rendering | `Assets/Settings/PC_RPAsset` | Full resolution, 4x MSAA |
| Edge lines | not built yet | planned: outline pass |

**Every material uses `RescueGame/Stylized Lit`.** Textures: bilinear, mipmaps, up to 2048 for heroes (helicopters, characters), 1024 for props.

## Meshy workflow

Best results so far: make a clean reference picture first (image-to-image from the concept, or text-to-image with the prompt below), then image-to-3D (Meshy 7).

Prompt to prepend:
> Stylised low-poly 3D game asset matching a warm golden-hour cartoon co-op game. Chunky slightly exaggerated shapes with crisp visible flat facets, clean painterly colour regions with soft hand-painted shading, dark outline edges, goofy and readable. No realistic detail, no grime or noise, no text or logos. Single object, centred, plain white background.

**Characters:** add "floppy ragdoll proportions, big hands, simple painted face, T-pose for rigging".
**Helicopter parts:** add "taped-together, grey duct tape patches, chunky shapes".

After download: check orientation with ortho views in Blender, weld (`remove_doubles`), delete stray islands, split spinning parts (rotors) with their pivot at the hub.

**Triangle budget:** props under 3k, characters under 8k, helicopters under 15k, set pieces under 20k.

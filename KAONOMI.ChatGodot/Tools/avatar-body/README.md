[www.kaonomi.net](https://www.kaonomi.net)

# The bodies of the 3D animations

The 3D animation of the chat can leave its frame and walk over the desktop (drag it to the left). It then needs a
body that matches its face. `generate_bodies.py` draws one for each animation, on this machine, with open source
parts only, and writes it in `Assets/`:

| File | What it is |
| --- | --- |
| `Assets/<name>-body.png` | The whole body, standing, seen from the front, transparent around it; its top is the picture of the face (`<name>-face.png`), unchanged |
| `Assets/<name>-body.json` | Its size, where the picture of the face is in it, its joints (shoulders, elbows, wrists, hips, knees, ankles, in pixels) and whether its arms can be cut out |

## How

1. The picture of the face (a bust) is put at the top of a tall empty picture, and **Stable Diffusion XL inpainting**
   (`diffusers/stable-diffusion-xl-1.0-inpainting-0.1`, OpenRAIL++-M, about 7 GB, downloaded the first time) draws
   the rest from a short description of the character (`CHARACTERS` in the script): the body continues the bust.
2. The background is taken away (**rembg**, U²-Net, MIT).
3. The joints are read on the silhouette (where the legs part, where the arms hang apart from the trunk), else
   found by **YOLOv8 pose** (Ultralytics), else placed by the usual proportions of a body.

## Run

```bash
python3 -m venv ~/kaonomi/tools/avatar-body
~/kaonomi/tools/avatar-body/bin/pip install torch diffusers transformers accelerate safetensors pillow numpy opencv-python certifi "rembg[cpu]" ultralytics
~/kaonomi/tools/avatar-body/bin/python generate_bodies.py                 # every animation (about a minute each on Apple Silicon)
~/kaonomi/tools/avatar-body/bin/python generate_bodies.py musuko --seed 7 # one of them, another drawing
~/kaonomi/tools/avatar-body/bin/python generate_bodies.py --joints-only   # the joints again, from the bodies already drawn
```

Look at each drawing: a body sitting, cut at the feet or with its hands in its pockets is drawn again with another
`--seed` (or a clearer description). The names whose arms hang clear of the trunk, with joints found right, go in
`ARMS` in the script: only these swing their arms; the arms of the others stay part of the trunk.

A new animation: add its face (`Assets/<name>-face.png`) and a line in `CHARACTERS`, then run the script for it.
The walker (`Scripts/Eve/AvatarWalker.cs`, `BodyPuppet.cs`) reads the body beside the face; without one, it gives
the animation a small robot body instead.

"""
The whole body of each 3D animation of the chat, drawn from its face.

The walker of the chat (the animation out of its frame, Scripts/Eve/AvatarWalker.cs) needs a body that matches the
face of each animation. This script makes it, on this machine, with open source parts only:

  1. the picture of the face (Assets/<name>-face.png, a bust) is put at the top of a tall empty picture, and Stable
     Diffusion XL inpainting (diffusers/stable-diffusion-xl-1.0-inpainting-0.1, OpenRAIL++-M) draws the rest: the body
     that continues the bust, standing, seen from the front, arms away from the body and legs apart so that they can
     be cut out;
  2. the background is taken away (rembg, U²-Net, MIT);
  3. the joints (shoulders, elbows, wrists, hips, knees, ankles) are read on the silhouette (where the legs part,
     where the arms hang apart from the trunk), else found by YOLOv8 pose (Ultralytics) when it sees a person in the
     picture, else placed by the usual proportions of a body.

It writes Assets/<name>-body.png (the body, transparent around it) and Assets/<name>-body.json (its size, where the
face of the animation is in it, its joints, in pixels, and whether its arms can be cut out), which the walker reads.
Look at each drawing: a body sitting, cut at the feet or with its hands in its pockets is drawn again with another
--seed; the names whose arms hang clear and are found right go in ARMS.

    python generate_bodies.py                 every face
    python generate_bodies.py kaonomi-robot   one of them
    python generate_bodies.py --seed 7 musuko another try (the seed changes the drawing)
    python generate_bodies.py --joints-only   the joints again, from the bodies already drawn
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import cv2
import numpy as np
from PIL import Image

ASSETS = Path(__file__).resolve().parents[2] / "Assets"
MODEL = "diffusers/stable-diffusion-xl-1.0-inpainting-0.1"
WIDTH, HEIGHT = 832, 1216

POSE = ("full body character from head to feet, standing on the floor on its own two legs, standing straight, front view, symmetrical pose, arms hanging slightly away from the body, "
        "legs slightly apart, both feet visible on the ground, centered, plain dark grey studio background")
NEGATIVE = ("cropped, cut off, close-up, portrait, missing legs, missing feet, sitting, squatting, crouching, bent knees, crossed arms, crossed legs, hands in pockets, "
            "extra limbs, extra arms, four arms, extra heads, two characters, pedestal, display stand, pole, base, toy figurine, mannequin stand, "
            "floating, text, watermark, frame, blurry, deformed")

# What each animation is, for the drawing of its body: (description, width of the bust in the tall picture, in pixels).
CHARACTERS = {
    "kaonomi-robot": ("a heavy dark weathered metal robot with a hooded head and one glowing orange eye, bulky armoured body, thick mechanical arms and legs, big metal boots, 3D render, cinematic lighting", 430),
    "kaonami": ("a worn white and red retro humanoid robot with exposed cables, slender mechanical body, articulated metal arms and legs, 3D render, cinematic lighting", 400),
    "kaonomi": ("a cute kawaii anime robot girl with pink and blonde hair, pastel pink and white outfit with hearts, short skirt over leggings, boots, anime illustration, clean line art, flat colors", 400),
    "mariekilleuse": ("a kawaii anime robot girl with pink and blonde hair holding a knife, pastel pink and white outfit with hearts, boots, anime illustration, clean line art, flat colors", 400),
    "musuko": ("a 3D cartoon robot boy with dark blue hair, blue armoured body with glowing cyan lights, robot arms and legs, blue boots, pixar style 3D render", 390),
    "green": ("a dark grey alien android child with big glowing green eyes, standing upright with straight legs, slim dark humanoid body with glowing green circuit lines, short arms with hands, 3D render", 360),
    "circuit": ("a pale android woman with circuit patterns, white and silver futuristic bodysuit with circuit lines, photorealistic, studio lighting", 340),
    "motuko": ("a woman with short black hair wearing white futuristic armour, white armoured suit, armoured gloves and boots, photorealistic, studio lighting", 360),
}

# The bodies whose arms hang clear of the trunk, with joints found right (checked by eye on the drawings): only these
# swing their arms. The arms of the others stay part of the trunk: cut at a wrong joint, they would tear the picture.
ARMS = {"circuit", "kaonami", "kaonomi-robot"}

# The joints of the body, in the order they are written; the ones of COCO that YOLOv8 pose gives.
JOINTS = {"shoulder_l": 5, "shoulder_r": 6, "elbow_l": 7, "elbow_r": 8, "wrist_l": 9, "wrist_r": 10,
          "hip_l": 11, "hip_r": 12, "knee_l": 13, "knee_r": 14, "ankle_l": 15, "ankle_r": 16}


def compose(face: Image.Image, bust_width: int):
    """The tall picture with the bust at its top, the mask of what is to be drawn, and where the bust is."""
    bust = face.resize((bust_width, round(face.height * bust_width / face.width)), Image.LANCZOS)
    x, y = (WIDTH - bust.width) // 2, 16
    rng = np.random.default_rng(0)
    canvas = Image.fromarray((np.full((HEIGHT, WIDTH, 3), 58, np.float32) + rng.normal(0, 6, (HEIGHT, WIDTH, 3))).clip(0, 255).astype(np.uint8))
    canvas.paste(bust, (x, y), bust)
    alpha = np.zeros((HEIGHT, WIDTH), np.uint8)
    alpha[y:y + bust.height, x:x + bust.width] = np.array(bust.split()[3])
    # Kept as it is: the bust, a little less than its edge so that the body joins it without a seam.
    keep = cv2.erode((alpha > 200).astype(np.uint8), np.ones((9, 9), np.uint8))
    mask = Image.fromarray(((1 - keep) * 255).astype(np.uint8))
    return canvas, mask, bust, (x, y, bust.width, bust.height)


def runs_of(row: np.ndarray, least: int = 9) -> list[tuple[int, int]]:
    """The runs of the silhouette on a row, as (first, last) columns; the thin ones (a cable, a strap) are left out."""
    filled = np.flatnonzero(row > 40)
    if filled.size == 0:
        return []
    breaks = np.flatnonzero(np.diff(filled) > 1)
    starts = np.concatenate(([filled[0]], filled[breaks + 1]))
    ends = np.concatenate((filled[breaks], [filled[-1]]))
    return [(int(a), int(b)) for a, b in zip(starts, ends) if b - a + 1 >= least]


def silhouette_joints(alpha: np.ndarray) -> dict | None:
    """
    The joints read on the silhouette of a body standing, seen from the front: the legs are the two runs under the
    row where the trunk ends (the crotch); an arm is a run that hangs apart, outside the legs and the trunk. None
    when the legs cannot be told apart.
    """
    ys, xs = np.where(alpha > 40)
    top, bottom = int(ys.min()), int(ys.max())
    height = bottom - top
    rows = {y: runs_of(alpha[y]) for y in range(top, bottom + 1)}
    # The middle of the body: the middle of its feet and of its head would do; the mass of the lower half is steadier.
    lower = np.where(alpha[top + height // 2:] > 40)[1]
    middle = float(np.median(lower)) if lower.size else (xs.min() + xs.max()) / 2

    thick = max(12, int((xs.max() - xs.min()) * 0.14))

    def legs_at(y):
        """The two runs each side of the middle that are the legs on this row, or None (the trunk covers the middle)."""
        runs = rows.get(y, [])
        # A thin run on the middle (a cable hanging between the legs) is not the trunk.
        if any(a <= middle <= b and b - a + 1 >= thick for a, b in runs):
            return None
        runs = [r for r in runs if not r[0] <= middle <= r[1]]
        lefts = [r for r in runs if r[1] < middle]
        rights = [r for r in runs if r[0] > middle]
        return (lefts[-1], rights[0]) if lefts and rights else None

    # The crotch: going up from above the feet, the first row where the legs are no longer apart for good.
    crotch = None
    y = bottom - max(4, height // 20)
    while y > top + height * 0.3:
        if legs_at(y) is None and all(legs_at(y - k) is None for k in range(1, 6)):
            crotch = y + 1
            break
        y -= 1
    if crotch is None or bottom - crotch < height * 0.15 or legs_at(crotch + 3) is None:
        return None
    leg = bottom - crotch

    def leg_point(side, share):
        y = int(crotch + share * leg)
        for dy in range(0, 12):
            for yy in (y + dy, y - dy):
                found = legs_at(yy)
                if found:
                    a, b = found[side]
                    return [round((a + b) / 2, 1), float(y)]
        return None

    joints = {}
    for side, name in ((0, "l"), (1, "r")):
        hip, knee, ankle = leg_point(side, 0.04), leg_point(side, 0.5), leg_point(side, 0.9)
        if not (hip and knee and ankle):
            return None
        # The hip is a little above where the legs part, inside the trunk.
        joints[f"hip_{name}"] = [hip[0], round(crotch - 0.06 * leg, 1)]
        joints[f"knee_{name}"], joints[f"ankle_{name}"] = knee, ankle

    # The legs and the trunk are between these columns; what hangs outside is an arm.
    wide = legs_at(crotch + 3)
    inner_left, inner_right = wide[0][0], wide[1][1]
    for side, name in ((0, "l"), (1, "r")):
        apart = []  # (row, middle of the run of the arm)
        for y in range(top + int(height * 0.12), bottom - int(leg * 0.15)):
            runs = rows[y]
            if len(runs) < 2:
                continue
            a, b = runs[0] if side == 0 else runs[-1]
            outside = b < inner_left - 2 if side == 0 else a > inner_right + 2
            # Above the crotch the trunk may be wider than the legs: the arm is then the outer run of at least three, or of two when the other holds the middle.
            if y < crotch:
                others = runs[1:] if side == 0 else runs[:-1]
                outside = any(o[0] <= middle <= o[1] for o in others)
            if outside:
                apart.append((y, (a + b) / 2))
        # The longest stretch of rows where it hangs apart.
        best, current = [], []
        for item in apart:
            if current and item[0] - current[-1][0] > 6:
                best, current = (current if len(current) > len(best) else best), []
            current.append(item)
        best = current if len(current) > len(best) else best
        if len(best) < height * 0.08:
            # The arm is against the body: its joints by the proportions, close to the trunk.
            half = (inner_right - inner_left) / 2
            sign = -1 if side == 0 else 1
            shoulder_y = top + (crotch - top) * 0.42
            joints[f"shoulder_{name}"] = [round(middle + sign * half * 1.25, 1), round(shoulder_y, 1)]
            joints[f"elbow_{name}"] = [round(middle + sign * half * 1.4, 1), round(shoulder_y + (crotch - shoulder_y) * 0.5, 1)]
            joints[f"wrist_{name}"] = [round(middle + sign * half * 1.45, 1), round(shoulder_y + (crotch - shoulder_y) * 0.92, 1)]
            continue
        first, last = best[0][0], best[-1][0]
        # The shoulder is above where the arm comes apart from the trunk, towards the body.
        length = last - first
        at = lambda share: best[min(len(best) - 1, int(share * (len(best) - 1)))]
        towards = 1 if side == 0 else -1
        joints[f"shoulder_{name}"] = [round(at(0)[1] + towards * 6, 1), round(first - 0.1 * length, 1)]
        joints[f"elbow_{name}"] = [round(at(0.42)[1], 1), float(at(0.42)[0])]
        joints[f"wrist_{name}"] = [round(at(0.8)[1], 1), float(at(0.8)[0])]
    return joints


def find_joints(picture: np.ndarray, alpha: np.ndarray) -> tuple[dict, str]:
    """The joints by YOLOv8 pose when it sees a body, else by the proportions of the silhouette."""
    ys, xs = np.where(alpha > 40)
    left, right, top, bottom = int(xs.min()), int(xs.max()), int(ys.min()), int(ys.max())
    # The silhouette first: it is right for the robots and the drawings, where the pose model sees a body in the head.
    found = silhouette_joints(alpha)
    if found is not None:
        return found, "silhouette"
    try:
        from ultralytics import YOLO

        models = Path.home() / "kaonomi" / "models" / "yolo"
        models.mkdir(parents=True, exist_ok=True)
        result = YOLO(str(models / "yolov8m-pose.pt"))(cv2.cvtColor(picture, cv2.COLOR_RGB2BGR), verbose=False, conf=0.25)[0]
        if result.keypoints is not None and len(result.keypoints) > 0:
            best = int(result.boxes.conf.argmax())
            points = result.keypoints.xy[best].cpu().numpy()
            scores = result.keypoints.conf[best].cpu().numpy()
            if all(scores[i] > 0.35 for i in JOINTS.values()):
                joints = {name: [round(float(points[i][0]), 1), round(float(points[i][1]), 1)] for name, i in JOINTS.items()}
                # The left of the body is on the right of the picture (it faces us): the names are the ones of the picture.
                if joints["shoulder_l"][0] > joints["shoulder_r"][0]:
                    joints = {name: joints[name[:-1] + ("r" if name.endswith("l") else "l")] for name in joints}
                return joints, "pose"
    except Exception as error:  # noqa: BLE001 - no model, no network: the proportions
        print(f"   pose not found ({error}): the proportions of the silhouette are used", file=sys.stderr)
    height, middle, half = bottom - top, (left + right) / 2, (right - left) / 2
    at = lambda dx, dy: [round(middle + dx * half, 1), round(top + dy * height, 1)]
    return {"shoulder_l": at(-0.62, 0.24), "shoulder_r": at(0.62, 0.24), "elbow_l": at(-0.8, 0.4), "elbow_r": at(0.8, 0.4),
            "wrist_l": at(-0.88, 0.54), "wrist_r": at(0.88, 0.54), "hip_l": at(-0.3, 0.55), "hip_r": at(0.3, 0.55),
            "knee_l": at(-0.32, 0.76), "knee_r": at(0.32, 0.76), "ankle_l": at(-0.34, 0.95), "ankle_r": at(0.34, 0.95)}, "proportions"


def main() -> int:
    parser = argparse.ArgumentParser(description="Draws the whole body of the 3D animations of the chat from their faces")
    parser.add_argument("names", nargs="*", help="the animations (kaonomi-robot, marvin…); all of them when none")
    parser.add_argument("--seed", type=int, default=3, help="the seed of the drawing: another one gives another body")
    parser.add_argument("--steps", type=int, default=30)
    parser.add_argument("--guidance", type=float, default=8.0)
    parser.add_argument("--out", default=str(ASSETS), help="where the bodies are written (Assets by default)")
    parser.add_argument("--keep-steps", action="store_true", help="also write the picture before the background is taken away")
    parser.add_argument("--joints-only", action="store_true", help="draw nothing: find the joints again in the bodies already there")
    args = parser.parse_args()
    names = args.names or list(CHARACTERS)
    unknown = [name for name in names if name not in CHARACTERS]
    if unknown:
        print(f"Unknown: {', '.join(unknown)} (known: {', '.join(CHARACTERS)})", file=sys.stderr)
        return 1

    if args.joints_only:
        out = Path(args.out)
        for name in names:
            path = out / f"{name}-body.json"
            if not path.exists():
                continue
            data = json.loads(path.read_text(encoding="utf-8"))
            pixels = np.array(Image.open(out / f"{name}-body.png").convert("RGBA"))
            joints, how = find_joints(pixels[:, :, :3], pixels[:, :, 3])
            data["joints"], data["joints_from"] = {key: [round(x, 1), round(y, 1)] for key, (x, y) in joints.items()}, how
            data["arms"] = name in ARMS
            path.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")
            print(f"{name}: joints by {how}", flush=True)
        return 0

    import torch
    from diffusers import AutoPipelineForInpainting
    from rembg import new_session, remove

    device = "mps" if torch.backends.mps.is_available() else "cuda" if torch.cuda.is_available() else "cpu"
    pipe = AutoPipelineForInpainting.from_pretrained(MODEL, torch_dtype=torch.float16 if device != "cpu" else torch.float32, variant="fp16").to(device)
    pipe.set_progress_bar_config(disable=True)
    cutter = new_session("u2net")
    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)

    for name in names:
        description, bust_width = CHARACTERS[name]
        face = Image.open(ASSETS / f"{name}-face.png").convert("RGBA")
        canvas, mask, bust, (bx, by, bw, bh) = compose(face, bust_width)
        print(f"{name}: drawing…", flush=True)
        drawn = pipe(prompt=f"{description}, {POSE}", negative_prompt=NEGATIVE, image=canvas, mask_image=mask, width=WIDTH, height=HEIGHT,
                     strength=0.99, guidance_scale=args.guidance, num_inference_steps=args.steps,
                     generator=torch.Generator("cpu").manual_seed(args.seed)).images[0].convert("RGB")
        # The bust itself, exactly as the animation shows it, over what was drawn.
        drawn.paste(bust, (bx, by), bust)
        if args.keep_steps:
            drawn.save(out / f"{name}-body-drawn.png")
        cut = remove(drawn, session=cutter).convert("RGBA")
        pixels = np.array(cut)
        # The bust is always kept, whatever the cutter thinks of it.
        bust_alpha = np.zeros((HEIGHT, WIDTH), np.uint8)
        bust_alpha[by:by + bh, bx:bx + bw] = np.array(bust.split()[3])
        pixels[:, :, 3] = np.maximum(pixels[:, :, 3], bust_alpha)
        # The body alone: the largest piece of what is left (the bits of background the cutter kept are dropped).
        count, labels, stats, _ = cv2.connectedComponentsWithStats((pixels[:, :, 3] > 40).astype(np.uint8))
        if count > 2:
            largest = 1 + int(np.argmax(stats[1:, cv2.CC_STAT_AREA]))
            pixels[:, :, 3] = np.where(labels == largest, pixels[:, :, 3], 0)
        joints, how = find_joints(pixels[:, :, :3], pixels[:, :, 3])
        # Cropped to the body, a margin around it.
        ys, xs = np.where(pixels[:, :, 3] > 40)
        left, top = max(0, int(xs.min()) - 8), max(0, int(ys.min()) - 8)
        right, bottom = min(WIDTH, int(xs.max()) + 9), min(HEIGHT, int(ys.max()) + 9)
        body = Image.fromarray(pixels[top:bottom, left:right])
        body.save(out / f"{name}-body.png")
        data = {
            "name": name, "width": body.width, "height": body.height,
            # Where the picture of the face of the animation is in the body: the animation is drawn there, over it.
            "face": [bx - left, by - top, bw, bh],
            "joints": {key: [round(x - left, 1), round(y - top, 1)] for key, (x, y) in joints.items()},
            # Whether the arms are cut out and swing (see ARMS).
            "arms": name in ARMS,
            "joints_from": how, "seed": args.seed, "model": MODEL,
        }
        (out / f"{name}-body.json").write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")
        print(f"{name}: {body.width} x {body.height}, joints by {how} -> {out / (name + '-body.png')}", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())

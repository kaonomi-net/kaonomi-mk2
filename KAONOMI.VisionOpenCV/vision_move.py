"""
/vision:move — motion detection with OpenCV, no neural network.

Based on moving_contours.py and motion_detection.py of LeLabIA-VisionOpenCV
(a tutorial of Le Lab IA): the background is learnt (MOG2), what differs from it is the
motion; the mask is cleaned (opening, dilation), its contours are the moving zones, and the moments of the mask give
the gravity centre of everything that moves.

In the chat it runs with --stream, without a window of its own: the frames go to the chat (the window /vision:move
of the Kaonomi desktop shows them) and the chat sends what its buttons and sliders ask. Alone, in a terminal, it
opens its own window:

One window, four views of the same frame:
    camera + zones        | foreground mask
    contours only + trail | motion highlight (the moving pixels alone)

Keys:  q / Esc  quit          space  pause             v  mosaic / one view
       r  learn the background again                    t  trail of the gravity centre on / off
       s  save a picture (~/Pictures/kaonomi/vision)    h  help on / off
"""

from __future__ import annotations

import argparse
import json
import queue
import struct
import sys
import threading
import time
from collections import deque
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path

import cv2
import numpy as np

WINDOW = "KAONOMI - /vision:move"
GREEN, YELLOW, RED, CYAN, WHITE = (0, 255, 0), (0, 255, 255), (0, 0, 255), (255, 200, 0), (255, 255, 255)
VIEWS = ("Mosaic", "Camera + zones", "Foreground mask", "Contours + trail", "Motion highlight")


@dataclass
class Zone:
    """A moving zone: its contour, its box and its own gravity centre."""

    contour: np.ndarray
    box: tuple[int, int, int, int]
    area: float
    center: tuple[int, int]


@dataclass
class Motion:
    """What moves in one frame."""

    mask: np.ndarray
    zones: list[Zone] = field(default_factory=list)
    center: tuple[int, int] | None = None  # the gravity centre of all the moving pixels
    ratio: float = 0.0  # the part of the frame that moves, 0 to 1


class MotionDetector:
    """Background subtraction (MOG2), a cleaned mask, the contours of what moves and its gravity centre."""

    WARM_UP = 3  # frames given to the background before anything counts as motion

    def __init__(self, sensitivity: int = 25, min_area: int = 800, history: int = 500):
        self.history = history
        self.min_area = min_area
        self._sensitivity = sensitivity
        self._kernel = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (5, 5))
        self._subtractor = self._new_subtractor()
        self._seen = 0  # frames since the background was (re)learnt

    def _new_subtractor(self):
        return cv2.createBackgroundSubtractorMOG2(history=self.history, varThreshold=max(1, self._sensitivity), detectShadows=False)

    @property
    def sensitivity(self) -> int:
        return self._sensitivity

    @sensitivity.setter
    def sensitivity(self, value: int) -> None:
        # The threshold of MOG2: the lower, the more sensitive. Changed live, the background learnt is kept.
        self._sensitivity = max(1, value)
        self._subtractor.setVarThreshold(self._sensitivity)

    def reset(self) -> None:
        """Forgets the background: it is learnt again from the next frames."""
        self._subtractor = self._new_subtractor()
        self._seen = 0

    def process(self, frame: np.ndarray) -> Motion:
        mask = self._subtractor.apply(frame)
        self._seen += 1
        if self._seen <= self.WARM_UP:
            # MOG2 knows no background yet: its first masks are the whole frame, not motion.
            return Motion(np.zeros_like(mask))
        mask = cv2.morphologyEx(mask, cv2.MORPH_OPEN, self._kernel, iterations=1)
        mask = cv2.morphologyEx(mask, cv2.MORPH_DILATE, self._kernel, iterations=2)
        contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)

        # Only the zones large enough are motion: the mask keeps them alone, so the noise weighs nothing in the
        # gravity centre.
        kept = np.zeros_like(mask)
        zones: list[Zone] = []
        for contour in contours:
            area = cv2.contourArea(contour)
            if area < max(self.min_area, 1):
                continue
            cv2.drawContours(kept, [contour], -1, 255, cv2.FILLED)
            m = cv2.moments(contour)
            x, y, w, h = cv2.boundingRect(contour)
            center = (int(m["m10"] / m["m00"]), int(m["m01"] / m["m00"])) if m["m00"] > 0 else (x + w // 2, y + h // 2)
            zones.append(Zone(contour, (x, y, w, h), area, center))
        zones.sort(key=lambda z: z.area, reverse=True)
        mask = cv2.bitwise_and(mask, kept)

        moments = cv2.moments(mask, binaryImage=True)
        center = (int(moments["m10"] / moments["m00"]), int(moments["m01"] / moments["m00"])) if moments["m00"] > 0 else None
        return Motion(mask, zones, center, float(cv2.countNonZero(mask)) / mask.size)


def label(image: np.ndarray, text: str, origin: tuple[int, int], color=GREEN, scale: float = 0.6, thickness: int = 2) -> None:
    """A text readable on any background: a dark outline under it."""
    for dx, dy in ((-1, -1), (1, -1), (-1, 1), (1, 1)):
        cv2.putText(image, text, (origin[0] + dx, origin[1] + dy), cv2.FONT_HERSHEY_SIMPLEX, scale, (0, 0, 0), thickness, cv2.LINE_AA)
    cv2.putText(image, text, origin, cv2.FONT_HERSHEY_SIMPLEX, scale, color, thickness, cv2.LINE_AA)


def draw_zones(canvas: np.ndarray, motion: Motion, names: bool) -> None:
    for index, zone in enumerate(motion.zones, start=1):
        x, y, w, h = zone.box
        cv2.drawContours(canvas, [zone.contour], -1, GREEN, 2)
        cv2.rectangle(canvas, (x, y), (x + w, y + h), YELLOW, 2)
        cv2.circle(canvas, zone.center, 3, YELLOW, cv2.FILLED)
        if names:
            label(canvas, f"Motion {index}  {int(zone.area)} px", (x, max(14, y - 6)), YELLOW, 0.45, 1)
    if motion.center is not None:
        cv2.drawMarker(canvas, motion.center, RED, markerType=cv2.MARKER_CROSS, markerSize=26, thickness=2)


def draw_trail(canvas: np.ndarray, trail: deque) -> None:
    """The path of the gravity centre: the older, the thinner."""
    points = list(trail)
    for i in range(1, len(points)):
        if points[i - 1] is None or points[i] is None:
            continue
        cv2.line(canvas, points[i - 1], points[i], CYAN, max(1, int(4 * i / len(points))), cv2.LINE_AA)


def render(frame: np.ndarray, motion: Motion, trail: deque, show_trail: bool) -> list[np.ndarray]:
    """The four views of the frame, in the order of VIEWS[1:]."""
    camera = frame.copy()
    draw_zones(camera, motion, names=True)
    if show_trail:
        draw_trail(camera, trail)

    mask = cv2.cvtColor(motion.mask, cv2.COLOR_GRAY2BGR)

    contours = np.zeros_like(frame)
    draw_zones(contours, motion, names=False)
    if show_trail:
        draw_trail(contours, trail)

    highlight = cv2.bitwise_and(frame, frame, mask=motion.mask)
    return [camera, mask, contours, highlight]


def compose(views: list[np.ndarray], view: int) -> np.ndarray:
    """The mosaic of the four views (2 x 2), or one of them alone."""
    if view > 0:
        image = views[view - 1].copy()
        label(image, VIEWS[view], (10, 28))
        return image
    for image, name in zip(views, VIEWS[1:]):
        label(image, name, (10, 28))
    return np.vstack((np.hstack(views[:2]), np.hstack(views[2:])))


def draw_status(image: np.ndarray, motion: Motion, fps: float, paused: bool, learning: bool) -> None:
    height, width = image.shape[:2]
    moving = len(motion.zones) > 0
    state = "PAUSED" if paused else "LEARNING THE BACKGROUND" if learning else "MOTION" if moving else "still"
    center = f"  centre {motion.center[0]},{motion.center[1]}" if motion.center else ""
    text = f"{state}  |  {len(motion.zones)} zone(s)  {motion.ratio * 100:4.1f} % of the frame{center}  |  {fps:4.1f} fps"
    cv2.rectangle(image, (0, height - 30), (width, height), (0, 0, 0), cv2.FILLED)
    label(image, text, (10, height - 9), RED if moving and not paused else WHITE, 0.55, 1)
    label(image, "h: help", (width - 90, height - 9), WHITE, 0.5, 1)


def draw_help(image: np.ndarray) -> None:
    lines = [
        "q / Esc   quit",
        "space     pause",
        "v         mosaic / one view",
        "r         learn the background again",
        "t         trail of the gravity centre",
        "s         save a picture",
        "h         this help",
    ]
    x, y = 20, 50
    overlay = image.copy()
    cv2.rectangle(overlay, (x - 10, y - 24), (x + 380, y + 24 * len(lines)), (0, 0, 0), cv2.FILLED)
    cv2.addWeighted(overlay, 0.7, image, 0.3, 0, image)
    for i, line in enumerate(lines):
        label(image, line, (x, y + 24 * i), WHITE, 0.55, 1)


def open_source(source: str, width: int, height: int) -> cv2.VideoCapture:
    """The camera of this index, or a video file."""
    if source.isdigit():
        index = int(source)
        backend = cv2.CAP_AVFOUNDATION if sys.platform == "darwin" else cv2.CAP_DSHOW if sys.platform == "win32" else cv2.CAP_ANY
        capture = cv2.VideoCapture(index, backend)
        if not capture.isOpened():
            capture = cv2.VideoCapture(index)
        capture.set(cv2.CAP_PROP_FRAME_WIDTH, width)
        capture.set(cv2.CAP_PROP_FRAME_HEIGHT, height)
        return capture
    return cv2.VideoCapture(source)


def open_camera_when_allowed(source: str, width: int, height: int, wait: float) -> cv2.VideoCapture:
    """
    Opens the source. On macOS the first use of the camera asks the user (a dialog of the system) and OpenCV fails at
    once without waiting for the answer: the camera is tried again every second, up to `wait` seconds, so that the
    window opens as soon as the access is allowed.
    """
    capture = open_source(source, width, height)
    if capture.isOpened() or sys.platform != "darwin" or not source.isdigit():
        return capture
    print("Waiting for the access to the camera: allow it in the dialog of macOS "
          "(or in System Settings > Privacy & Security > Camera)", flush=True)
    deadline = time.monotonic() + wait
    while time.monotonic() < deadline:
        capture.release()
        time.sleep(1)
        capture = open_source(source, width, height)
        if capture.isOpened():
            print("Camera allowed", flush=True)
            break
    return capture


def save_picture(image: np.ndarray, folder: Path) -> Path | None:
    try:
        folder.mkdir(parents=True, exist_ok=True)
        path = folder / f"vision-move-{datetime.now():%Y%m%d-%H%M%S}.png"
        return path if cv2.imwrite(str(path), image) else None
    except OSError:
        return None


# ── The stream to the chat (--stream) ──
#
# On the standard output, one message after the other: the length of a JSON text (4 bytes, big endian), the JSON, the
# length of a JPEG picture (4 bytes, 0 when there is none), the picture. The JSON of a frame holds what was found
# (state, zones, ratio, center, fps, width, height); the one of a message holds "message" (and "error": true).
# On the standard input, one command per line: "min_area 800", "threshold 25", "view 0" (0: mosaic, 1 to 4: one
# view), "trail 1", "pause 1", "reset", "snapshot", "quit". The input closed (the chat is gone) quits too.

def send(output, meta: dict, picture: bytes = b"") -> None:
    text = json.dumps(meta).encode("utf-8")
    output.write(struct.pack(">I", len(text)) + text + struct.pack(">I", len(picture)) + picture)
    output.flush()


def read_commands(commands: queue.Queue) -> None:
    for line in sys.stdin:
        commands.put(line.strip())
    commands.put("quit")


def run_stream(args) -> int:
    output = sys.stdout.buffer
    sys.stdout = sys.stderr  # nothing but the messages on the standard output
    commands: queue.Queue = queue.Queue()
    threading.Thread(target=read_commands, args=(commands,), daemon=True).start()

    capture = open_source(args.source, args.width, args.height)
    if not capture.isOpened() and sys.platform == "darwin" and args.source.isdigit():
        send(output, {"message": "Waiting for the access to the camera: allow it in the dialog of macOS (or in System Settings > Privacy & Security > Camera)"})
        deadline = time.monotonic() + args.wait
        while not capture.isOpened() and time.monotonic() < deadline:
            capture.release()
            try:
                if commands.get(timeout=1) == "quit":
                    return 0
            except queue.Empty:
                pass
            capture = open_source(args.source, args.width, args.height)
    if not capture.isOpened():
        what = f"the camera {args.source}" if args.source.isdigit() else f"the video '{args.source}'"
        send(output, {"message": f"{what} could not be opened" + (" (macOS did not allow it: System Settings > Privacy & Security > Camera, for the chat)" if sys.platform == "darwin" and args.source.isdigit() else ""), "error": True})
        return 2

    detector = MotionDetector(args.sensitivity, args.min_area)
    trail: deque = deque(maxlen=64)
    view, paused, show_trail = 0, False, True
    frames = learnt = 0
    fps, last = 0.0, time.perf_counter()
    period = 1.0 / max(1, args.fps)
    composed = None
    try:
        while True:
            started = time.perf_counter()
            while True:
                try:
                    command = commands.get_nowait()
                except queue.Empty:
                    break
                name, _, value = command.partition(" ")
                number = int(value) if value.strip().lstrip("-").isdigit() else 0
                if name == "quit":
                    return 0
                if name == "min_area":
                    detector.min_area = max(1, number)
                elif name == "threshold":
                    detector.sensitivity = max(1, number)
                elif name == "view":
                    view = min(max(number, 0), len(VIEWS) - 1)
                elif name == "trail":
                    show_trail = number != 0
                    trail.clear()
                elif name == "pause":
                    paused = number != 0
                elif name == "reset":
                    detector.reset()
                    trail.clear()
                    learnt = 0
                    send(output, {"message": "Background forgotten: learning it again"})
                elif name == "snapshot" and composed is not None:
                    path = save_picture(composed, Path(args.pictures))
                    send(output, {"message": f"Saved {path}"} if path else {"message": f"The picture could not be saved in {args.pictures}", "error": True})
            if paused:
                time.sleep(0.03)
                continue
            ok, frame = capture.read()
            if not ok:
                if frames == 0:
                    send(output, {"message": "no frame could be read from the camera", "error": True})
                    return 3
                send(output, {"message": "The video is over" if not args.source.isdigit() else "The camera stopped", "error": args.source.isdigit()})
                return 0
            if args.source.isdigit():
                frame = cv2.flip(frame, 1)
            motion = detector.process(frame)
            frames += 1
            learnt += 1
            trail.append(motion.center)
            now = time.perf_counter()
            fps = 0.9 * fps + 0.1 / max(now - last, 1e-6) if fps else 1 / max(now - last, 1e-6)
            last = now
            composed = compose(render(frame, motion, trail, show_trail), view)
            ok, jpeg = cv2.imencode(".jpg", composed, [cv2.IMWRITE_JPEG_QUALITY, 80])
            if ok:
                send(output, {
                    "state": "learning" if learnt < 30 else "motion" if motion.zones else "still",
                    "zones": len(motion.zones),
                    "ratio": round(motion.ratio, 4),
                    "center": list(motion.center) if motion.center else None,
                    "fps": round(fps, 1),
                    "width": int(composed.shape[1]),
                    "height": int(composed.shape[0]),
                }, jpeg.tobytes())
            if args.frames and frames >= args.frames:
                return 0
            time.sleep(max(0.0, period - (time.perf_counter() - started)))
    except (BrokenPipeError, OSError):
        return 0  # the chat closed the window
    finally:
        capture.release()


def main() -> int:
    parser = argparse.ArgumentParser(description="Motion detection with OpenCV (/vision:move of the Kaonomi chat)")
    parser.add_argument("--source", default="0", help="index of the camera (0 by default), or a video file")
    parser.add_argument("--width", type=int, default=640, help="width asked to the camera")
    parser.add_argument("--height", type=int, default=480, help="height asked to the camera")
    parser.add_argument("--min-area", type=int, default=800, help="smallest moving zone kept, in pixels")
    parser.add_argument("--sensitivity", type=int, default=25, help="threshold of the background subtraction: the lower, the more sensitive")
    parser.add_argument("--pictures", default=str(Path.home() / "Pictures" / "kaonomi" / "vision"), help="folder of the pictures saved with s")
    parser.add_argument("--wait", type=float, default=45, help="macOS: seconds given to allow the camera in the dialog of the system")
    parser.add_argument("--stream", action="store_true", help="no window: the frames go to the standard output, the commands come on the standard input (the chat)")
    parser.add_argument("--fps", type=int, default=25, help="with --stream: the most frames sent per second")
    parser.add_argument("--frames", type=int, default=0, help="stop after this many frames (0: never; for the tests)")
    parser.add_argument("--no-window", action="store_true", help="no window: the counts are printed (for the tests)")
    args = parser.parse_args()
    if args.stream:
        return run_stream(args)

    capture = open_camera_when_allowed(args.source, args.width, args.height, args.wait)
    if not capture.isOpened():
        if not args.source.isdigit():
            print(f"Error: the video '{args.source}' could not be opened", file=sys.stderr)
        else:
            print(f"Error: the camera {args.source} could not be opened"
                  + (" (macOS did not allow it: System Settings > Privacy & Security > Camera, for the application that started the chat)" if sys.platform == "darwin" else ""), file=sys.stderr)
        return 2

    detector = MotionDetector(args.sensitivity, args.min_area)
    trail: deque = deque(maxlen=64)
    view, paused, show_trail, show_help = 0, False, True, False
    frames = moving_frames = 0
    fps, last = 0.0, time.perf_counter()
    learnt = 0  # frames since the background was (re)learnt
    message, message_until = "", 0.0
    composed = None
    motion = None

    if not args.no_window:
        cv2.namedWindow(WINDOW, cv2.WINDOW_NORMAL)
        cv2.createTrackbar("Min area", WINDOW, args.min_area, 20_000, lambda _v: None)
        cv2.createTrackbar("Threshold", WINDOW, args.sensitivity, 100, lambda _v: None)
        print("Camera opened. q or Esc to quit, h for the keys.", flush=True)

    while True:
        if not paused or motion is None:
            ok, frame = capture.read()
            if not ok:
                if frames == 0:
                    print("Error: no frame could be read", file=sys.stderr)
                    return 3
                break
            if args.source.isdigit():
                frame = cv2.flip(frame, 1)  # a camera is shown as a mirror
            if not args.no_window:
                detector.min_area = cv2.getTrackbarPos("Min area", WINDOW)
                threshold = cv2.getTrackbarPos("Threshold", WINDOW)
                if threshold != detector.sensitivity and threshold > 0:
                    detector.sensitivity = threshold
            motion = detector.process(frame)
            frames += 1
            learnt += 1
            moving_frames += 1 if motion.zones else 0
            trail.append(motion.center)
            now = time.perf_counter()
            fps = 0.9 * fps + 0.1 / max(now - last, 1e-6) if fps else 1 / max(now - last, 1e-6)
            last = now
            views = render(frame, motion, trail, show_trail)

        if args.no_window:
            if args.frames and frames >= args.frames:
                break
            continue

        composed = compose([v.copy() for v in views], view)
        draw_status(composed, motion, fps, paused, learnt < 30)
        if time.perf_counter() < message_until:
            label(composed, message, (10, 60), CYAN, 0.6, 2)
        if show_help:
            draw_help(composed)
        cv2.imshow(WINDOW, composed)

        key = cv2.waitKey(1 if not paused else 30) & 0xFF
        if key in (ord("q"), 27):
            break
        if key == ord(" "):
            paused = not paused
        elif key == ord("v"):
            view = (view + 1) % len(VIEWS)
        elif key == ord("t"):
            show_trail = not show_trail
            trail.clear()
        elif key == ord("h"):
            show_help = not show_help
        elif key == ord("r"):
            detector.reset()
            trail.clear()
            learnt = 0
            message, message_until = "Background forgotten: learning it again", time.perf_counter() + 2
        elif key == ord("s"):
            path = save_picture(composed, Path(args.pictures))
            message = f"Saved {path}" if path else f"The picture could not be saved in {args.pictures}"
            message_until = time.perf_counter() + 3
            print(message, flush=True)
        # The window closed with its button.
        try:
            if cv2.getWindowProperty(WINDOW, cv2.WND_PROP_VISIBLE) < 1:
                break
        except cv2.error:
            break
        if args.frames and frames >= args.frames:
            break

    capture.release()
    cv2.destroyAllWindows()
    print(f"{frames} frame(s), motion in {moving_frames}", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())

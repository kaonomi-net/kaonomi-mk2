"""
Object detection with YOLOv8 and OpenCV — the objects of /vision.

Based on the scripts of LeLabIA-VisionYolo (a tutorial of Le Lab IA):
    01-GravityCenter           the centre of the box of each person, with its coordinates
    02-Vecteurdedirection      each person followed from frame to frame: the trail of its last positions and the
                               arrow of its average direction
    03-PersonOverlapDetection  an alert when the boxes of two persons overlap (IoU > 0)
    04-WineGlassDetector       an alert when an object of a given class is seen ("wine glass" by default)
    05-SaveJsonFiles           the detections written as JSON (here: one line per detection in a .jsonl file per
                               session, at most once a second, in ~/kaonomi/vision/yolo)
All of them in one application, each as an option. The models are the ones of these scripts: yolov8n.pt (nano, fast)
and yolov8m.pt (medium, more accurate), and two larger ones of the same family: yolov8l.pt (large) and yolov8x.pt
(extra large: the most accurate, the slowest). They are kept in ~/kaonomi/models/yolo and downloaded by Ultralytics
when missing.

In the chat it runs with --stream, without a window of its own (see vision_move.py for the protocol): the frames go
to a window of the Kaonomi desktop (/vision runs its detector in vision_face.py --objects instead). Alone, in a terminal, it opens its own window.

Keys (own window):  q / Esc  quit    space  pause    m  the next model (n, m, l, x)    p  persons only / every object
                    c  centres    d  directions    o  overlap alert    j  JSON log    s  save a picture
"""

from __future__ import annotations

import argparse
import json
import queue
import sys
import threading
import time
from collections import deque
from datetime import datetime
from pathlib import Path

import cv2
import numpy as np

from objects_record import ObjectsRecorder
from vision_move import CYAN, GREEN, RED, WHITE, YELLOW, label, open_camera_when_allowed, open_source, read_commands, send

WINDOW = "KAONOMI - /vision objects"
MODELS = {"n": "yolov8n.pt", "m": "yolov8m.pt", "l": "yolov8l.pt", "x": "yolov8x.pt"}
MODEL_NAMES = {"n": "YOLOv8 nano", "m": "YOLOv8 medium", "l": "YOLOv8 large", "x": "YOLOv8 extra large"}
PERSON = 0  # the class of the persons in COCO
BLUE = (255, 0, 0)


def default_models_directory() -> Path:
    return Path.home() / "kaonomi" / "models" / "yolo"


def best_device() -> str:
    """The GPU of the machine when PyTorch can use it (Metal on a Mac, CUDA elsewhere), else the processor."""
    try:
        import torch

        if torch.cuda.is_available():
            return "cuda"
        if torch.backends.mps.is_available():
            return "mps"
    except Exception:  # noqa: BLE001 - whatever PyTorch says, the processor works
        pass
    return "cpu"


def load_model(size: str, directory: Path):
    """The model of this size, from the folder of the models; Ultralytics downloads it there when it is missing."""
    from ultralytics import YOLO

    directory.mkdir(parents=True, exist_ok=True)
    return YOLO(str(directory / MODELS[size]))


def iou(a, b) -> float:
    """Intersection over union of two boxes (x1, y1, x2, y2)."""
    x1, y1, x2, y2 = max(a[0], b[0]), max(a[1], b[1]), min(a[2], b[2]), min(a[3], b[3])
    if x2 < x1 or y2 < y1:
        return 0.0
    intersection = (x2 - x1) * (y2 - y1)
    union = (a[2] - a[0]) * (a[3] - a[1]) + (b[2] - b[0]) * (b[3] - b[1]) - intersection
    return intersection / union if union > 0 else 0.0


def overlapping(boxes: list) -> set[int]:
    """The indexes of the boxes that overlap another one."""
    found: set[int] = set()
    for i in range(len(boxes)):
        for j in range(i + 1, len(boxes)):
            if iou(boxes[i], boxes[j]) > 0:
                found.update((i, j))
    return found


def average_direction(positions: list) -> tuple[float, float] | None:
    """The average move between the last positions."""
    if len(positions) < 2:
        return None
    moves = [(positions[i][0] - positions[i - 1][0], positions[i][1] - positions[i - 1][1]) for i in range(1, len(positions))]
    return sum(m[0] for m in moves) / len(moves), sum(m[1] for m in moves) / len(moves)


class Tracker:
    """Each person followed from frame to frame by the nearest centre: its last positions."""

    def __init__(self, history: int = 4, max_distance: float = 100):
        self.history, self.max_distance = history, max_distance
        self.tracks: dict[int, deque] = {}
        self._next = 0

    def update(self, centers: list[tuple[int, int]]) -> None:
        matched: set[int] = set()
        for center in centers:
            best, best_distance = None, self.max_distance
            for identifier, positions in self.tracks.items():
                if identifier in matched or not positions:
                    continue
                distance = float(np.hypot(center[0] - positions[-1][0], center[1] - positions[-1][1]))
                if distance < best_distance:
                    best, best_distance = identifier, distance
            if best is None:
                best, self._next = self._next, self._next + 1
                self.tracks[best] = deque(maxlen=self.history)
            self.tracks[best].append(center)
            matched.add(best)
        # A person no longer seen is forgotten.
        self.tracks = {identifier: positions for identifier, positions in self.tracks.items() if identifier in matched}


class Options:
    """What the window asks, changed while it runs."""

    def __init__(self, args):
        self.size = args.model
        self.confidence = args.conf
        self.persons_only = args.persons_only
        self.centers = True
        self.vectors = True
        self.overlap = True
        self.alert_class = args.alert_class.strip().lower()
        self.log = args.log
        self.paused = False
        # Everything detected is summed up every minute and told to the chat for its search engine.
        self.record = not getattr(args, "no_record", False)


class Detector:
    """YOLO on a frame, and what the scripts of the tutorial draw over its boxes."""

    def __init__(self, options: Options, models: Path, device: str):
        self.options, self.models, self.device = options, models, device
        self.model = None
        self.loaded_size = None
        self.tracker = Tracker()

    def ensure_model(self) -> None:
        if self.model is None or self.loaded_size != self.options.size:
            self.model = load_model(self.options.size, self.models)
            self.loaded_size = self.options.size
            self.tracker = Tracker()

    def process(self, frame: np.ndarray) -> tuple[np.ndarray, dict, list[dict]]:
        """The frame drawn, what was found (for the status), and the detections (for the JSON log)."""
        self.ensure_model()
        options = self.options
        arguments = dict(imgsz=640, conf=options.confidence, verbose=False, device=self.device)
        if options.persons_only:
            arguments["classes"] = [PERSON]
        try:
            result = self.model(frame, **arguments)[0]
        except Exception:  # noqa: BLE001 - a GPU that fails (Metal with an operator it lacks…): the processor
            if self.device == "cpu":
                raise
            self.device = arguments["device"] = "cpu"
            result = self.model(frame, **arguments)[0]

        annotated = result.plot()
        names = result.names
        boxes = result.boxes
        detections: list[dict] = []
        counts: dict[str, int] = {}
        persons: list[tuple[float, float, float, float]] = []
        alert = False
        for box in boxes:
            x1, y1, x2, y2 = (float(v) for v in box.xyxy[0].cpu().numpy())
            class_id, confidence = int(box.cls[0]), float(box.conf[0])
            name = names[class_id]
            counts[name] = counts.get(name, 0) + 1
            if class_id == PERSON:
                persons.append((x1, y1, x2, y2))
            if options.alert_class and options.alert_class in name.lower():
                alert = True
            detections.append({
                "object": name, "class_id": class_id, "confidence": round(confidence, 3),
                "box": [round(x1), round(y1), round(x2), round(y2)],
                "center": [round((x1 + x2) / 2), round((y1 + y2) / 2)],
            })

        centers = [(int((b[0] + b[2]) / 2), int((b[1] + b[3]) / 2)) for b in persons]
        self.tracker.update(centers)

        # 03: the persons whose boxes overlap.
        touching = overlapping(persons) if options.overlap else set()
        for index in touching:
            x1, y1, x2, y2 = (int(v) for v in persons[index])
            cv2.rectangle(annotated, (x1, y1), (x2, y2), RED, 4)

        # 01: the centre of each person and its coordinates.
        if options.centers:
            for x, y in centers:
                cv2.circle(annotated, (x, y), 5, GREEN, cv2.FILLED)
                label(annotated, f"({x}, {y})", (x + 10, y - 10), GREEN, 0.5, 1)

        # 02: the trail of the last positions and the arrow of the average direction.
        if options.vectors:
            for positions in self.tracker.tracks.values():
                points = list(positions)
                for i in range(1, len(points)):
                    cv2.line(annotated, points[i - 1], points[i], YELLOW, 2, cv2.LINE_AA)
                direction = average_direction(points)
                if direction is not None and (abs(direction[0]) + abs(direction[1])) >= 1:
                    x, y = points[-1]
                    end = (int(x + direction[0] * 10), int(y + direction[1] * 10))
                    cv2.arrowedLine(annotated, (x, y), end, BLUE, 3, cv2.LINE_AA, tipLength=0.3)

        # 03 and 04: the alerts, written across the picture.
        height, width = annotated.shape[:2]
        banners = []
        if touching:
            banners.append("OVERLAP")
        if alert:
            banners.append(options.alert_class.upper())
        for row, text in enumerate(banners):
            scale, thickness = 1.6, 4
            (text_width, text_height), _ = cv2.getTextSize(text, cv2.FONT_HERSHEY_SIMPLEX, scale, thickness)
            origin = ((width - text_width) // 2, height // 2 + row * (text_height + 24))
            label(annotated, text, origin, RED, scale, thickness)

        found = {
            "objects": len(detections), "persons": len(persons), "counts": counts,
            "overlap": bool(touching), "alert": alert, "alert_class": options.alert_class,
            "model": MODEL_NAMES[self.loaded_size], "device": self.device,
        }
        return annotated, found, detections


class JsonLog:
    """The detections of a session, one JSON line each, at most once a second (05-SaveJsonFiles)."""

    def __init__(self, folder: Path):
        self.folder = folder
        self.path: Path | None = None
        self._last = 0.0
        self.lines = 0

    def write(self, detections: list[dict], model: str) -> None:
        now = time.monotonic()
        if not detections or now - self._last < 1.0:
            return
        self._last = now
        try:
            if self.path is None:
                self.folder.mkdir(parents=True, exist_ok=True)
                self.path = self.folder / f"vision-yolo-{datetime.now():%Y%m%d-%H%M%S}.jsonl"
            stamp = datetime.now().astimezone().isoformat(timespec="milliseconds")
            with self.path.open("a", encoding="utf-8") as file:
                for detection in detections:
                    file.write(json.dumps({"timestamp": stamp, "model": model, **detection}) + "\n")
                    self.lines += 1
        except OSError:
            pass


def save_picture(image: np.ndarray, folder: Path) -> Path | None:
    try:
        folder.mkdir(parents=True, exist_ok=True)
        path = folder / f"vision-yolo-{datetime.now():%Y%m%d-%H%M%S}.png"
        return path if cv2.imwrite(str(path), image) else None
    except OSError:
        return None


def describe(found: dict) -> str:
    counts = ", ".join(f"{count} {name}" for name, count in sorted(found["counts"].items(), key=lambda item: -item[1])[:6])
    return counts or "nothing"


# ── The stream to the chat (--stream) ──
#
# The messages are the ones of vision_move.py. The JSON of a frame holds: state ("detecting"), objects, persons,
# counts ({"person": 2, "cup": 1}), overlap, alert, alert_class, model, device, fps, width, height.
# The commands: "model n", "model m", "model l" or "model x", "conf 50" (per cent), "persons 1", "centers 1", "vectors 1", "overlap 1",
# "alert wine glass" ("alert" alone: none), "log 1", "record 1", "pause 1", "snapshot", "quit".
# Every minute (--record-seconds), a message {"detected": …} holds everything detected during it (objects_record.py):
# the chat writes it in its search engine.

def apply(command: str, options: Options) -> str | None:
    """Applies a command of the chat to the options; "quit" and "snapshot" are given back to the loop."""
    name, _, value = command.partition(" ")
    value = value.strip()
    flag = value not in ("", "0")
    if name in ("quit", "snapshot"):
        return name
    if name == "model" and value in MODELS:
        options.size = value
    elif name == "conf" and value.isdigit():
        options.confidence = min(max(int(value), 1), 99) / 100
    elif name == "persons":
        options.persons_only = flag
    elif name == "centers":
        options.centers = flag
    elif name == "vectors":
        options.vectors = flag
    elif name == "overlap":
        options.overlap = flag
    elif name == "alert":
        options.alert_class = value.lower()
    elif name == "log":
        options.log = flag
    elif name == "record":
        options.record = flag
    elif name == "pause":
        options.paused = flag
    return None


def run_stream(args) -> int:
    output = sys.stdout.buffer
    sys.stdout = sys.stderr  # nothing but the messages on the standard output
    commands: queue.Queue = queue.Queue()
    threading.Thread(target=read_commands, args=(commands,), daemon=True).start()
    options = Options(args)
    models = Path(args.models)

    def announce_model() -> None:
        missing = not (models / MODELS[options.size]).exists()
        send(output, {"message": f"Downloading {MODELS[options.size]} into {models} (once)…" if missing else f"Loading {MODEL_NAMES[options.size]}…"})

    try:
        send(output, {"message": "Loading YOLO (PyTorch)…"})
        detector = Detector(options, models, best_device())
        announce_model()
        detector.ensure_model()
    except Exception as error:  # noqa: BLE001 - no network for the download, a broken install…
        send(output, {"message": f"YOLO could not be loaded: {error}", "error": True})
        return 4

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

    log = JsonLog(Path(args.log_directory))
    frames = 0
    fps, last = 0.0, time.perf_counter()
    period = 1.0 / max(1, args.fps)
    annotated = None
    was_logging = options.log
    recorder = ObjectsRecorder(args.record_seconds)
    was_recording = options.record
    try:
        while True:
            started = time.perf_counter()
            while True:
                try:
                    asked = apply(commands.get_nowait(), options)
                except queue.Empty:
                    break
                if asked == "quit":
                    return 0
                if asked == "snapshot" and annotated is not None:
                    path = save_picture(annotated, Path(args.pictures))
                    send(output, {"message": f"Saved {path}"} if path else {"message": f"The picture could not be saved in {args.pictures}", "error": True})
            if options.record != was_recording:
                was_recording = options.record
                # Switched off: the period so far is sent.
                if not options.record and (done := recorder.close()) is not None:
                    send(output, {"detected": done})
            if options.log != was_logging:
                was_logging = options.log
                send(output, {"message": f"The detections are written in {log.folder} (one line each, once a second)" if options.log
                              else f"JSON log stopped: {log.lines} line(s) in {log.path}" if log.path else "JSON log stopped"})
            if options.paused:
                time.sleep(0.03)
                continue
            if detector.loaded_size != options.size:
                announce_model()
                try:
                    detector.ensure_model()
                    send(output, {"message": f"{MODEL_NAMES[options.size]} loaded"})
                except Exception as error:  # noqa: BLE001
                    send(output, {"message": f"{MODELS[options.size]} could not be loaded: {error}", "error": True})
                    options.size = detector.loaded_size
            ok, frame = capture.read()
            if not ok:
                if frames == 0:
                    send(output, {"message": "no frame could be read from the camera", "error": True})
                    return 3
                send(output, {"message": "The video is over" if not args.source.isdigit() else "The camera stopped", "error": args.source.isdigit()})
                return 0
            if args.source.isdigit():
                frame = cv2.flip(frame, 1)
            annotated, found, detections = detector.process(frame)
            frames += 1
            if options.log:
                log.write(detections, found["model"])
            if options.record and (done := recorder.frame(time.time(), detections, found, options.confidence)) is not None:
                send(output, {"detected": done})
            now = time.perf_counter()
            fps = 0.9 * fps + 0.1 / max(now - last, 1e-6) if fps else 1 / max(now - last, 1e-6)
            last = now
            ok, jpeg = cv2.imencode(".jpg", annotated, [cv2.IMWRITE_JPEG_QUALITY, 80])
            if ok:
                send(output, {"state": "detecting", **found, "fps": round(fps, 1), "width": int(annotated.shape[1]), "height": int(annotated.shape[0])}, jpeg.tobytes())
            if args.frames and frames >= args.frames:
                return 0
            time.sleep(max(0.0, period - (time.perf_counter() - started)))
    except (BrokenPipeError, OSError):
        return 0  # the chat closed the window
    finally:
        capture.release()
        # The last period, shorter, when the chat still listens.
        try:
            if options.record and (done := recorder.close()) is not None:
                send(output, {"detected": done})
        except (BrokenPipeError, OSError, ValueError):
            pass


def run_window(args) -> int:
    options = Options(args)
    print("Loading YOLO…", flush=True)
    detector = Detector(options, Path(args.models), best_device())
    detector.ensure_model()
    capture = open_camera_when_allowed(args.source, args.width, args.height, args.wait)
    if not capture.isOpened():
        print(f"Error: the camera or the video '{args.source}' could not be opened", file=sys.stderr)
        return 2
    log = JsonLog(Path(args.log_directory))
    frames = 0
    fps, last = 0.0, time.perf_counter()
    annotated = None
    if not args.no_window:
        cv2.namedWindow(WINDOW, cv2.WINDOW_NORMAL)
        cv2.createTrackbar("Confidence %", WINDOW, int(options.confidence * 100), 99, lambda _v: None)
        print("q or Esc to quit; m: model, p: persons only, c: centres, d: directions, o: overlap, j: JSON log, s: picture", flush=True)
    seen: dict[str, int] = {}
    while True:
        if not options.paused or annotated is None:
            ok, frame = capture.read()
            if not ok:
                break
            if args.source.isdigit():
                frame = cv2.flip(frame, 1)
            if not args.no_window:
                options.confidence = max(1, cv2.getTrackbarPos("Confidence %", WINDOW)) / 100
            annotated, found, detections = detector.process(frame)
            frames += 1
            for name, count in found["counts"].items():
                seen[name] = max(seen.get(name, 0), count)
            if options.log:
                log.write(detections, found["model"])
            now = time.perf_counter()
            fps = 0.9 * fps + 0.1 / max(now - last, 1e-6) if fps else 1 / max(now - last, 1e-6)
            last = now
            height, width = annotated.shape[:2]
            cv2.rectangle(annotated, (0, height - 30), (width, height), (0, 0, 0), cv2.FILLED)
            label(annotated, f"{found['model']} ({found['device']})  |  {describe(found)}  |  {fps:4.1f} fps", (10, height - 9), WHITE, 0.5, 1)
        if args.frames and frames >= args.frames:
            break
        if args.no_window:
            continue
        cv2.imshow(WINDOW, annotated)
        key = cv2.waitKey(1 if not options.paused else 30) & 0xFF
        if key in (ord("q"), 27):
            break
        if key == ord(" "):
            options.paused = not options.paused
        elif key == ord("m"):
            sizes = list(MODELS)
            options.size = sizes[(sizes.index(options.size) + 1) % len(sizes)]   # nano, medium, large, extra large
        elif key == ord("p"):
            options.persons_only = not options.persons_only
        elif key == ord("c"):
            options.centers = not options.centers
        elif key == ord("d"):
            options.vectors = not options.vectors
        elif key == ord("o"):
            options.overlap = not options.overlap
        elif key == ord("j"):
            options.log = not options.log
            print(f"JSON log {'on: ' + str(log.folder) if options.log else 'off'}", flush=True)
        elif key == ord("s"):
            print(f"Saved {save_picture(annotated, Path(args.pictures))}", flush=True)
        try:
            if cv2.getWindowProperty(WINDOW, cv2.WND_PROP_VISIBLE) < 1:
                break
        except cv2.error:
            break
    capture.release()
    cv2.destroyAllWindows()
    print(f"{frames} frame(s); seen at most: {', '.join(f'{count} {name}' for name, count in seen.items()) or 'nothing'}", flush=True)
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description="Object detection with YOLOv8 (the objects of /vision of the Kaonomi chat)")
    parser.add_argument("--source", default="0", help="index of the camera (0 by default), a video file or a picture")
    parser.add_argument("--width", type=int, default=640, help="width asked to the camera")
    parser.add_argument("--height", type=int, default=480, help="height asked to the camera")
    parser.add_argument("--model", choices=sorted(MODELS), default="n",
                        help="n: yolov8n.pt (nano, fast); m: yolov8m.pt (medium); l: yolov8l.pt (large); x: yolov8x.pt (extra large, the most accurate, the slowest)")
    parser.add_argument("--models", default=str(default_models_directory()), help="folder of the models (downloaded there when missing)")
    parser.add_argument("--conf", type=float, default=0.5, help="smallest confidence of a detection kept (0 to 1)")
    parser.add_argument("--persons-only", action="store_true", help="detect the persons only")
    parser.add_argument("--alert-class", default="wine glass", help="alert when an object of this class is seen (empty: none)")
    parser.add_argument("--log", action="store_true", help="write the detections as JSON lines")
    parser.add_argument("--no-record", action="store_true", help="with --stream: do not tell the chat what was detected for its search engine")
    parser.add_argument("--record-seconds", type=float, default=60, help="with --stream: everything detected is summed up and told to the chat every this many seconds (60)")
    parser.add_argument("--log-directory", default=str(Path.home() / "kaonomi" / "vision" / "yolo"), help="folder of the JSON lines")
    parser.add_argument("--pictures", default=str(Path.home() / "Pictures" / "kaonomi" / "vision"), help="folder of the pictures saved")
    parser.add_argument("--wait", type=float, default=45, help="macOS: seconds given to allow the camera in the dialog of the system")
    parser.add_argument("--stream", action="store_true", help="no window: the frames go to the standard output, the commands come on the standard input (the chat)")
    parser.add_argument("--fps", type=int, default=25, help="with --stream: the most frames sent per second")
    parser.add_argument("--download", action="store_true", help="download every model into --models, then stop")
    parser.add_argument("--frames", type=int, default=0, help="stop after this many frames (0: never; for the tests)")
    parser.add_argument("--no-window", action="store_true", help="no window: what was seen is printed (for the tests)")
    args = parser.parse_args()
    if args.download:
        for size in MODELS:
            load_model(size, Path(args.models))
            print(f"{MODELS[size]}: {Path(args.models) / MODELS[size]}", flush=True)
        return 0
    return run_stream(args) if args.stream else run_window(args)


if __name__ == "__main__":
    sys.exit(main())

"""
/vision — face recognition and facial expression recognition, with OpenCV alone.

Everything is open source and runs on this machine; no picture leaves it. OpenCV (Apache 2.0) runs three small
models of the OpenCV Zoo (https://github.com/opencv/opencv_zoo):
    YuNet     face_detection_yunet_2026may.onnx                          MIT         the faces and 5 landmarks of each
    SFace     face_recognition_sface_2021dec.onnx                        Apache 2.0  a face as 128 numbers, to compare
    MobileFaceNet  facial_expression_recognition_mobilefacenet_2022july.onnx  Apache 2.0  angry, disgust, fearful,
                                                                                      happy, neutral, sad, surprised
They are kept in ~/kaonomi/models/face and downloaded from the OpenCV Zoo when missing.

Who is who: a face is recognised when it is close enough (cosine similarity of SFace) to a face enrolled under a
name. Enrolling keeps the face seen now, aligned (112 x 112), in ~/kaonomi/vision/faces/<name>/: several pictures per
name make the recognition steadier; forgetting a name deletes its folder. In the chat, the face enrolled is the one
clicked in the window (its box): the chat says which ("hold"), then the name typed ("enrolheld").

The movement of each face (face_motion.py): the centre of gravity of its box, an arrow from it in the direction it
moves, and a yellow trail of where it went, fading out in 3 seconds.

The objects (vision_yolo.py, with --objects or the command "objects 1"): YOLOv8 runs on the same frame, its boxes,
the centres, trails and directions of the persons and its alerts drawn under the faces; what it finds is in the same
message as the faces, its JSON log and its record every minute too. One camera, one process: the faces and
the objects are one window of the chat, /vision.

In the chat it runs with --stream, without a window of its own (see vision_move.py for the protocol): the frames go
to the window /vision of the Kaonomi desktop. Alone, in a terminal, it opens its own window.

Speed: the faces are found at each frame (on the frame made DETECT_WIDTH wide when it is wider, and on the whole
frame every WHOLE_SECONDS for the small faces of someone far), but who a face followed is and what it expresses are
read again every IDENTITY_SECONDS and EXPRESSION_SECONDS only (kept with its track meanwhile); with --stream the objects
are detected in their own thread while the faces are read, and what moves is looked for on a smaller frame. See "What
a frame costs" in README.md.

Keys (own window):  q / Esc  quit    space  pause    e  enrol the largest face (the name is asked in the terminal)
                    x  expressions on / off    l  landmarks on / off    m  movement on / off    s  save a picture
"""

from __future__ import annotations

import argparse
import json
import queue
import re
import shutil
import ssl
import sys
import threading
import time
import urllib.request
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime
from pathlib import Path

import cv2
import numpy as np

import vision_yolo as yolo
from face_motion import FaceMotion
from face_record import FaceRecorder
from objects_record import ObjectsRecorder
from vision_move import CYAN, GREEN, RED, WHITE, YELLOW, MotionDetector, label, open_camera_when_allowed, open_source, read_commands, send

WINDOW = "KAONOMI - /vision"
ZOO = "https://github.com/opencv/opencv_zoo/raw/main/models"
MODELS = {
    "detection": ("face_detection_yunet/face_detection_yunet_2026may.onnx", "YuNet (face detection)"),
    "recognition": ("face_recognition_sface/face_recognition_sface_2021dec.onnx", "SFace (face recognition)"),
    "expression": ("facial_expression_recognition/facial_expression_recognition_mobilefacenet_2022july.onnx", "MobileFaceNet (expressions)"),
}
EXPRESSIONS = ("angry", "disgust", "fearful", "happy", "neutral", "sad", "surprised")
EXPRESSION_COLORS = {"happy": GREEN, "neutral": WHITE, "surprised": CYAN, "sad": (255, 140, 60), "angry": RED, "disgust": (60, 160, 60), "fearful": (200, 90, 200)}
# Where the 5 landmarks of a face go in the 112 x 112 picture the expression model reads (from the OpenCV Zoo).
ALIGNED = np.array([[38.2946, 51.6963], [73.5318, 51.5014], [56.0252, 71.7366], [41.5493, 92.3655], [70.7299, 92.2041]], np.float32)
UNKNOWN = "unknown"


def model_path(directory: Path, key: str) -> Path:
    return directory / MODELS[key][0].split("/")[-1]


def missing_models(directory: Path) -> list[str]:
    return [key for key in MODELS if not model_path(directory, key).exists()]


def https_context() -> ssl.SSLContext:
    """The certificates of certifi when it is there: the Python of python.org on macOS comes without any."""
    try:
        import certifi

        return ssl.create_default_context(cafile=certifi.where())
    except ImportError:
        return ssl.create_default_context()


def download_models(directory: Path, say=print) -> None:
    """The models missing, from the OpenCV Zoo."""
    directory.mkdir(parents=True, exist_ok=True)
    context = https_context()
    for key in missing_models(directory):
        target = model_path(directory, key)
        say(f"Downloading {MODELS[key][1]} into {directory} (once)…")
        partial = target.with_suffix(".part")
        with urllib.request.urlopen(f"{ZOO}/{MODELS[key][0]}", context=context, timeout=60) as response, partial.open("wb") as file:  # noqa: S310 - a fixed https address
            shutil.copyfileobj(response, file)
        partial.replace(target)


def safe_name(name: str) -> str:
    """A name as the name of a folder: letters, digits, spaces, - and _."""
    return re.sub(r"\s+", " ", re.sub(r"[^\w \-]", "", name, flags=re.UNICODE)).strip()[:40]


class Faces:
    """The faces enrolled: for each name, the pictures kept and what SFace says of each (128 numbers)."""

    def __init__(self, folder: Path, recognizer):
        self.folder, self.recognizer = folder, recognizer
        self.features: dict[str, list[np.ndarray]] = {}
        self.files: dict[str, list[str]] = {}  # the pictures of each name, in the order of its features
        self.version = 0  # changes with the faces enrolled: what was recognised before is read again
        self.load()

    def load(self) -> None:
        self.features, self.files = {}, {}
        self.version += 1
        if not self.folder.is_dir():
            return
        for person in sorted(p for p in self.folder.iterdir() if p.is_dir()):
            found, files = [], []
            for picture in sorted(person.glob("*.jpg")):
                image = cv2.imread(str(picture))
                if image is not None and image.shape[:2] == (112, 112):
                    found.append(self.recognizer.feature(image).copy())
                    files.append(picture.name)
            if found:
                self.features[person.name], self.files[person.name] = found, files

    @staticmethod
    def numbers(feature: np.ndarray) -> list[float]:
        return [round(float(v), 5) for v in feature.flatten()]

    def inventory(self) -> list[dict]:
        """Every picture kept: its name, its file and what SFace says of it (for the search engine of the chat)."""
        return [{"name": name, "file": file, "feature": self.numbers(feature)}
                for name in self.features for file, feature in zip(self.files[name], self.features[name])]

    def counts(self) -> dict[str, int]:
        return {name: len(features) for name, features in self.features.items()}

    def enrol(self, name: str, aligned: np.ndarray) -> dict:
        """Keeps this aligned face under this name: its name, file, feature, and the number of pictures of the name."""
        person = self.folder / name
        person.mkdir(parents=True, exist_ok=True)
        file = f"{datetime.now():%Y%m%d-%H%M%S-%f}.jpg"
        cv2.imwrite(str(person / file), aligned)
        feature = self.recognizer.feature(aligned).copy()
        self.features.setdefault(name, []).append(feature)
        self.files.setdefault(name, []).append(file)
        self.version += 1
        return {"name": name, "file": file, "feature": self.numbers(feature), "count": len(self.features[name])}

    def forget(self, name: str) -> bool:
        person = self.folder / name
        known = name in self.features or person.is_dir()
        self.features.pop(name, None)
        self.files.pop(name, None)
        self.version += 1
        # Only a folder of the faces is ever deleted.
        if person.is_dir() and person.parent.resolve() == self.folder.resolve():
            shutil.rmtree(person, ignore_errors=True)
        return known

    def recognise(self, feature: np.ndarray, threshold: float) -> tuple[str, float]:
        """The name whose pictures are the closest (cosine similarity), or unknown under the threshold."""
        best_name, best_score = UNKNOWN, 0.0
        for name, features in self.features.items():
            score = max(float(self.recognizer.match(feature, known, cv2.FaceRecognizerSF_FR_COSINE)) for known in features)
            if score > best_score:
                best_name, best_score = name, score
        return (best_name, best_score) if best_score >= threshold else (UNKNOWN, best_score)


class Options:
    """What the window asks, changed while it runs."""

    def __init__(self, args):
        self.threshold = args.threshold
        self.expressions = True
        self.landmarks = False
        # The centre of each face, the arrow of its movement, its trail.
        self.motion = True
        # The rectangle of what moves in the picture (the motion detection of vision_move.py: just its rectangle).
        self.movement = True
        # What is seen of the faces (who, the emotions, the area, the movement) is told to the chat for its search engine.
        self.record = not getattr(args, "no_record", False)
        self.paused = False


# How long what was read of a face followed is kept before it is read again, in seconds: who it is (SFace: 13 ms a
# face), what it expresses (5 ms a face). A face just seen is read at once; so is every face after an enrolment, a name
# forgotten or another threshold.
IDENTITY_SECONDS = 0.2
EXPRESSION_SECONDS = 0.12

# The widest frame the faces are looked for on, in pixels: a wider one (1280 x 720 from the camera) is made this wide
# for the detection. Faces under about 40 pixels wide are not found on it: the frame is read whole every WHOLE_SECONDS
# for them (someone far is still found, down to 16 pixels as before, and followed 4 times a second), and its faces
# under SMALL_FACE pixels wide are kept meanwhile.
DETECT_WIDTH = 640
WHOLE_SECONDS = 0.25
SMALL_FACE = 64


class FaceAnalyser:
    """The faces of a frame: where, who, and what they express."""

    def __init__(self, models: Path, faces_folder: Path, options: Options):
        self.options = options
        self.detector = cv2.FaceDetectorYN.create(str(model_path(models, "detection")), "", (320, 320), 0.7, 0.3, 5000)
        # The same, for the frames read whole (see _detect).
        self.detector_whole = cv2.FaceDetectorYN.create(str(model_path(models, "detection")), "", (320, 320), 0.7, 0.3, 5000)
        self._whole_size = (0, 0)
        self._whole_at = float("-inf")
        self._small: list[np.ndarray] = []
        self.recognizer = cv2.FaceRecognizerSF.create(str(model_path(models, "recognition")), "")
        self.expression = cv2.dnn.readNet(str(model_path(models, "expression")))
        self.faces = Faces(faces_folder, self.recognizer)
        self._size = (0, 0)
        self.largest_aligned: np.ndarray | None = None  # the largest face of the last frame, aligned: the one enrolled
        self.seen: list[tuple[list[int], np.ndarray]] = []  # every face of the last frame: its box and the face aligned
        self.held: np.ndarray | None = None  # the face clicked in the chat (aligned), kept until it is named
        self.motion = FaceMotion()
        self._born = 0

    def _expression(self, frame: np.ndarray, landmarks: np.ndarray) -> tuple[str, float, list[float] | None]:
        """The expression that leads, how sure it is, and the share of each of the seven (they add up to 1)."""
        matrix, _ = cv2.estimateAffinePartial2D(landmarks, ALIGNED, method=cv2.LMEDS)
        if matrix is None:
            return "", 0.0, None
        aligned = cv2.warpAffine(frame, matrix, (112, 112))
        data = (cv2.cvtColor(aligned, cv2.COLOR_BGR2RGB).astype(np.float32) / 255.0 - 0.5) / 0.5
        self.expression.setInput(cv2.dnn.blobFromImage(data))
        scores = self.expression.forward()[0]
        scores = np.exp(scores - scores.max())
        scores /= scores.sum()
        index = int(scores.argmax())
        return EXPRESSIONS[index], float(scores[index]), [float(v) for v in scores]

    def _identity(self, track, aligned: np.ndarray, moment: float) -> tuple[str, float]:
        """Who this face is: read again every IDENTITY_SECONDS, and at once after an enrolment or another threshold."""
        if not self.faces.features:
            return UNKNOWN, 0.0
        key = (self.faces.version, self.options.threshold)
        kept = track.identity
        if kept is not None and kept[3] == key and moment - kept[2] < IDENTITY_SECONDS:
            return kept[0], kept[1]
        name, score = self.faces.recognise(self.recognizer.feature(aligned), self.options.threshold)
        # The faces that appeared together are not all read again in the same frame.
        track.identity = (name, score, moment if kept is not None else moment - self._stagger(IDENTITY_SECONDS), key)
        return name, score

    def _expressed(self, track, frame: np.ndarray, landmarks: np.ndarray, moment: float) -> tuple[str, float, list[float] | None]:
        """What this face expresses: read again every EXPRESSION_SECONDS."""
        kept = track.expression
        if kept is not None and moment - kept[3] < EXPRESSION_SECONDS:
            return kept[0], kept[1], kept[2]
        expression, confidence, probabilities = self._expression(frame, landmarks)
        track.expression = (expression, confidence, probabilities, moment if kept is not None else moment - self._stagger(EXPRESSION_SECONDS))
        return expression, confidence, probabilities

    def _stagger(self, period: float) -> float:
        """A part of <period>, another one at each call: the first reading of a face is aged by it."""
        self._born = (self._born + 1) % 4
        return period * self._born / 4

    def _detect(self, frame: np.ndarray, moment: float) -> list[np.ndarray]:
        """
        The faces of the frame, as YuNet gives them (box, 5 landmarks, score), in the pixels of the frame. A frame
        wider than DETECT_WIDTH is made that wide first (YuNet costs as many times more as there are pixels), and the
        boxes and landmarks are brought back to its pixels; who the faces are is read on the frame itself. A small
        face (someone far) is not found on the smaller frame: every WHOLE_SECONDS the frame is also read whole, for
        its faces under SMALL_FACE pixels only, kept until the next time where the smaller frame does not show them
        (the other faces always come from the smaller frame: their boxes do not jump from one reading to the other).
        """
        height, width = frame.shape[:2]
        scale = min(1.0, DETECT_WIDTH / max(1, width))
        if scale == 1.0:
            return self._faces(self.detector, "_size", frame)
        if moment - self._whole_at >= WHOLE_SECONDS:
            self._whole_at = moment
            self._small = [row.copy() for row in self._faces(self.detector_whole, "_whole_size", frame) if row[2] < SMALL_FACE]
        rows = self._faces(self.detector, "_size", cv2.resize(frame, (round(width * scale), round(height * scale)), interpolation=cv2.INTER_AREA))
        for row in rows:
            row[:14] /= scale

        def shown(small: np.ndarray) -> bool:
            cx, cy = small[0] + small[2] / 2, small[1] + small[3] / 2
            return any(abs(cx - (row[0] + row[2] / 2)) < 0.75 * max(row[2], small[2]) and abs(cy - (row[1] + row[3] / 2)) < 0.75 * max(row[3], small[3])
                       for row in rows)

        return rows + [small for small in self._small if not shown(small)]

    def _faces(self, detector, known: str, image: np.ndarray) -> list[np.ndarray]:
        """What <detector> finds in <image> (one detector for each size: the size of its input is set once, not at each change)."""
        size = (image.shape[1], image.shape[0])
        if getattr(self, known) != size:
            detector.setInputSize(size)
            setattr(self, known, size)
        _, found = detector.detect(image)
        return [] if found is None else list(found)

    def analyse(self, frame: np.ndarray) -> dict:
        """
        What is found in the frame, nothing drawn (so that it runs while the objects are detected): each face (its
        box, its landmarks, who it is, what it expresses) and its track. The faces are found at each frame; who a face
        followed is and what it expresses are read again every IDENTITY_SECONDS and EXPRESSION_SECONDS only.
        """
        height, width = frame.shape[:2]
        moment = time.monotonic()
        rows = sorted(self._detect(frame, moment), key=lambda row: -(row[2] * row[3]))
        self.largest_aligned = None
        self.seen = []
        # The movement: the faces followed (always, so that it goes on when it is shown again).
        tracks = self.motion.update(moment, [[int(v) for v in row[:4]] for row in rows])
        # The expressions are also what is recorded: computed for the record even when they are not shown.
        wanted = self.options.expressions or self.options.record
        faces = []
        for index, (row, track) in enumerate(zip(rows, tracks)):
            x, y, w, h = (int(v) for v in row[:4])
            landmarks = row[4:14].reshape(5, 2).astype(np.float32)
            aligned = self.recognizer.alignCrop(frame, row)
            if index == 0:
                self.largest_aligned = aligned
            self.seen.append(([x, y, w, h], aligned))
            name, score = self._identity(track, aligned, moment)
            expression, confidence, probabilities = self._expressed(track, frame, landmarks, moment) if wanted else ("", 0.0, None)
            if not self.options.expressions:
                expression, confidence = "", 0.0
            faces.append({"box": (x, y, w, h), "landmarks": landmarks, "name": name, "score": score,
                          "expression": expression, "confidence": confidence, "probabilities": probabilities})
        return {"faces": faces, "tracks": tracks, "moment": moment, "height": height}

    def draw(self, annotated: np.ndarray, analysis: dict) -> tuple[np.ndarray, dict]:
        """What <analyse> found, drawn over <annotated> (the frame, or the objects already drawn), and told."""
        height, moment, tracks = analysis["height"], analysis["moment"], analysis["tracks"]
        people = []
        # The trails under the boxes, the centres and the arrows over them.
        if self.options.motion:
            self.motion.draw_trails(annotated, moment)
        for face in analysis["faces"]:
            x, y, w, h = face["box"]
            name, score, expression, confidence = face["name"], face["score"], face["expression"], face["confidence"]
            known = name != UNKNOWN
            color = GREEN if known else YELLOW
            cv2.rectangle(annotated, (x, y), (x + w, y + h), color, 2)
            label(annotated, f"{name}  {score * 100:.0f} %" if known else name, (x, max(16, y - 8)), color, 0.6, 2)
            if expression:
                label(annotated, f"{expression}  {confidence * 100:.0f} %", (x, min(height - 6, y + h + 20)), EXPRESSION_COLORS.get(expression, WHITE), 0.6, 2)
            if self.options.landmarks:
                for px, py in face["landmarks"]:
                    cv2.circle(annotated, (int(px), int(py)), 3, CYAN, cv2.FILLED)
            people.append({"name": name, "known": known, "score": round(score, 3), "expression": expression, "confidence": round(confidence, 3),
                           "box": [x, y, w, h], "probabilities": face["probabilities"]})
        if self.options.motion:
            self.motion.draw_marks(annotated, tracks)
        return annotated, {"faces": len(people), "people": people, "known": self.faces.counts()}

    def process(self, frame: np.ndarray, canvas: np.ndarray | None = None) -> tuple[np.ndarray, dict]:
        """The frame drawn (over <canvas> when given: the objects already drawn) and what was found."""
        return self.draw(frame.copy() if canvas is None else canvas, self.analyse(frame))


# The threads OpenCV gives the models of the faces: one. With more, macOS (GCD) spreads each model over every core: a
# frame is done 2.5 times sooner, but for half more processor time, and at 25 frames a second nothing is gained by
# being done sooner. The faces being looked for on a frame of DETECT_WIDTH at most, one thread is enough.
OPENCV_THREADS = int(__import__("os").environ.get("KAONOMI_OPENCV_THREADS", "1"))


class Objects:
    """
    The detection of the objects (vision_yolo.py) on the frames of the faces: YOLOv8, its options, its JSON log and its
    record of every minute. Loaded when it is switched on; off, nothing of it runs.
    """

    COMMANDS = ("model", "conf", "persons", "centers", "vectors", "overlap", "alert", "log")

    def __init__(self, args, say):
        asked = argparse.Namespace(model=args.model, conf=args.conf, persons_only=args.persons_only, alert_class=args.alert_class,
                                   log=args.log, no_record=args.no_record_objects)
        self.options = yolo.Options(asked)
        self.on = args.objects
        self.models = Path(args.yolo_models)
        self.detector: yolo.Detector | None = None
        self.log = yolo.JsonLog(Path(args.log_directory))
        self.recorder = ObjectsRecorder(args.record_seconds)
        self.say = say
        self._logging = self.options.log

    def ensure(self) -> bool:
        """The model asked, loaded (downloaded once when missing); False, said, when it cannot be."""
        options = self.options
        if self.detector is not None and self.detector.loaded_size == options.size:
            return True
        missing = not (self.models / yolo.MODELS[options.size]).exists()
        self.say({"message": f"Downloading {yolo.MODELS[options.size]} into {self.models} (once)…" if missing else f"Loading {yolo.MODEL_NAMES[options.size]}…"})
        try:
            if self.detector is None:
                self.detector = yolo.Detector(options, self.models, yolo.best_device())
            self.detector.ensure_model()
            # Ultralytics, when it is loaded, sets the threads of OpenCV: they are set back as chosen here.
            cv2.setNumThreads(OPENCV_THREADS)
            self.say({"message": f"{yolo.MODEL_NAMES[options.size]} loaded: faces and objects"})
            return True
        except Exception as error:  # noqa: BLE001 - no network for the download, a broken install…
            self.say({"message": f"YOLO could not be loaded: {error}", "error": True})
            if self.detector is not None and self.detector.loaded_size:
                options.size = self.detector.loaded_size
                return True
            self.on = False
            return False

    def command(self, name: str, value: str, line: str) -> bool:
        """A command of the objects: applied; False when it is not one."""
        flag = value not in ("", "0")
        if name == "objects":
            if flag and not self.on:
                self.on = True
                self.ensure()
            elif not flag and self.on:
                self.on = False
                self.close()
            return True
        if name == "recordobjects":
            if self.options.record and not flag:
                self.close()
            self.options.record = flag
            return True
        if name in self.COMMANDS:
            yolo.apply(line, self.options)
            if name == "log" and self.options.log != self._logging:
                self._logging = self.options.log
                log = self.log
                self.say({"message": f"The detections are written in {log.folder} (one line each, once a second)" if log and self.options.log
                          else f"JSON log stopped: {log.lines} line(s) in {log.path}" if log.path else "JSON log stopped"})
            return True
        return False

    def process(self, frame: np.ndarray) -> tuple[np.ndarray | None, dict]:
        """The frame with the objects drawn, and what was found (nothing when it is off)."""
        if not self.on or not self.ensure():
            return None, {}
        annotated, found, detections = self.detector.process(frame)
        if self.options.log:
            self.log.write(detections, found["model"])
        if self.options.record and (done := self.recorder.frame(time.time(), detections, found, self.options.confidence)) is not None:
            self.say({"detected": done})
        return annotated, found

    def close(self) -> None:
        """The minute being recorded, sent as it is (switched off, or the window closed)."""
        if (done := self.recorder.close()) is not None:
            self.say({"detected": done})


def save_picture(image: np.ndarray, folder: Path) -> Path | None:
    try:
        folder.mkdir(parents=True, exist_ok=True)
        path = folder / f"vision-face-{datetime.now():%Y%m%d-%H%M%S}.png"
        return path if cv2.imwrite(str(path), image) else None
    except OSError:
        return None


def describe(found: dict) -> str:
    people = ", ".join(person["name"] + (f" ({person['expression']})" if person["expression"] else "") for person in found["people"])
    return people or "no face"


# ── The stream to the chat (--stream) ──
#
# The messages are the ones of vision_move.py. The JSON of a frame holds: faces, people ([{"name", "known", "score",
# "expression", "confidence", "box"}], the largest face first), known ({"Anna": 3}: the pictures kept per name), fps,
# width, height. Three messages hold more than their text, for the chat to keep its search engine like the folder of
# the faces: "inventory" at the start ([{"name", "file", "feature"}]: every picture kept), "enrolled" ({"name", "file",
# "feature", "count"}: the face just kept), "forgotten" (the name just forgotten). The commands: "enrol <name>" (the
# largest face seen now), "hold <x> <y>" (the face of the last frame at this point, in the pixels of the frame, is kept
# until it is named: the one clicked in the chat), "enrolheld <name>" (the face kept by "hold"), "enrolfile {"name": …,
# "path": …}" (the largest face of a photo), "forget <name>", "threshold 36" (per cent of the cosine similarity),
# "expressions 1", "landmarks 1", "motion 1" (the centres, arrows and trails), "movement 1" (the rectangle of what
# moves in the picture, sent as "movement": [x, y, w, h] or null), "capture" (the next frame sent as it is, nothing
# drawn on it, with {"capture": true}: the picture SET LOCATION has analysed), "pause 1", "snapshot", "quit".
# The objects (class Objects): "objects 1" (YOLO on or off), "recordobjects 1" (the record of every minute, sent as
# {"detected": …}), and the commands of vision_yolo.py: "model n|m|l|x", "conf 50", "persons 1", "centers 1",
# "vectors 1", "overlap 1", "alert wine glass", "log 1". A frame then holds what vision_yolo.py sends too (objects,
# persons, counts, overlap, alert, alert_class, model, device) and "objects_on".

def enrol(analyser: FaceAnalyser, name: str) -> dict:
    """Enrols the largest face of the last frame under this name: the message to send ("enrolled": the face kept)."""
    name = safe_name(name)
    if not name or name.lower() == UNKNOWN:
        return {"message": "Type a name first", "error": True}
    if analyser.largest_aligned is None:
        return {"message": "No face to enrol: look at the camera", "error": True}
    kept = analyser.faces.enrol(name, analyser.largest_aligned)
    return {"message": f"{name} enrolled: {kept['count']} picture(s) kept in {analyser.faces.folder / name}", "enrolled": kept}


def hold(analyser: FaceAnalyser, x: float, y: float) -> dict | None:
    """
    Keeps the face of the last frame at this point (its box holds it; the smallest box when several do, else the
    closest one): the face clicked in the chat, enrolled once it is named. A message only when there is none.
    """
    def distance(box: list[int]) -> float:
        bx, by, bw, bh = box
        return float(np.hypot(max(bx - x, 0, x - bx - bw), max(by - y, 0, y - by - bh)))

    analyser.held = None
    if not analyser.seen:
        return {"message": "No face seen now: look at the camera, then click the face again", "error": True}
    box, aligned = min(analyser.seen, key=lambda face: (distance(face[0]), face[0][2] * face[0][3]))
    # Moved too far since the click (more than its own size): another face would be named.
    if distance(box) > max(box[2], box[3]):
        return {"message": "That face is no longer seen: click it again", "error": True}
    analyser.held = aligned.copy()
    return None


def enrol_held(analyser: FaceAnalyser, name: str) -> dict:
    """Enrols the face kept by "hold" (the one clicked in the chat) under this name: the message to send."""
    name = safe_name(name)
    if not name or name.lower() == UNKNOWN:
        return {"message": "Type a name first", "error": True}
    if analyser.held is None:
        return {"message": "No face chosen: click the box of a face", "error": True}
    kept = analyser.faces.enrol(name, analyser.held)
    analyser.held = None
    return {"message": f"{name} enrolled: {kept['count']} picture(s) kept in {analyser.faces.folder / name}", "enrolled": kept}


def enrol_file(analyser: FaceAnalyser, name: str, path: str) -> dict:
    """Enrols the largest face of a photo (a file) under this name: the message to send ("enrolled": the face kept)."""
    name = safe_name(name)
    if not name or name.lower() == UNKNOWN:
        return {"message": "Type a name first", "error": True}
    try:
        # np.fromfile + imdecode: cv2.imread does not open a path with accents on every system.
        image = cv2.imdecode(np.fromfile(path, dtype=np.uint8), cv2.IMREAD_COLOR)
    except (OSError, ValueError):
        image = None
    if image is None:
        return {"message": f"The photo could not be read: {path}", "error": True}
    # A large photo is made smaller: the detector looks for faces of the size of a frame of the camera.
    height, width = image.shape[:2]
    scale = min(1.0, 1600 / max(height, width))
    if scale < 1.0:
        image = cv2.resize(image, (int(width * scale), int(height * scale)), interpolation=cv2.INTER_AREA)
        height, width = image.shape[:2]
    analyser.detector.setInputSize((width, height))
    analyser._size = (0, 0)  # the next frame of the camera sets the size of the detector again
    _, found = analyser.detector.detect(image)
    if found is None or len(found) == 0:
        return {"message": f"No face found in {Path(path).name}: take a photo where the face is seen from the front", "error": True}
    row = max(found, key=lambda r: r[2] * r[3])
    kept = analyser.faces.enrol(name, analyser.recognizer.alignCrop(image, row))
    others = f" ({len(found) - 1} other face(s) left out: the largest is kept)" if len(found) > 1 else ""
    return {"message": f"{name} enrolled from {Path(path).name}{others}: {kept['count']} picture(s) kept in {analyser.faces.folder / name}", "enrolled": kept}


def forget(analyser: FaceAnalyser, name: str) -> dict:
    """Forgets a name: the message to send ("forgotten": the name)."""
    name = safe_name(name)
    if not name:
        return {"message": "Type the name to forget first", "error": True}
    if not analyser.faces.forget(name):
        return {"message": f"{name} is not enrolled", "error": True}
    return {"message": f"{name} forgotten: its pictures are deleted", "forgotten": name}


MAGENTA = (255, 0, 255)


def movement_box(motion) -> tuple[int, int, int, int] | None:
    """The rectangle around every zone that moves (None when nothing moves)."""
    if not motion.zones:
        return None
    left = min(z.box[0] for z in motion.zones)
    top = min(z.box[1] for z in motion.zones)
    right = max(z.box[0] + z.box[2] for z in motion.zones)
    bottom = max(z.box[1] + z.box[3] for z in motion.zones)
    return left, top, right - left, bottom - top


class Movement:
    """
    What moves in the picture: one rectangle around all of it (the MotionDetector of vision_move.py: the background
    learnt by MOG2). The frame is made WIDTH pixels wide first: four times fewer pixels at 640 x 480, and the rectangle
    is as good (to 2 pixels).
    """

    WIDTH = 320
    MIN_AREA = 1500  # the smallest zone that counts, in the pixels of the frame

    def __init__(self):
        self.detector: MotionDetector | None = None
        self.scale = 1.0
        self._size = (0, 0)

    def box(self, frame: np.ndarray) -> tuple[int, int, int, int] | None:
        height, width = frame.shape[:2]
        if self._size != (width, height) or self.detector is None:
            self._size = (width, height)
            self.scale = min(1.0, self.WIDTH / max(1, width))
            self.detector = MotionDetector(min_area=max(1, int(self.MIN_AREA * self.scale * self.scale)))
        small = frame if self.scale == 1.0 else cv2.resize(frame, None, fx=self.scale, fy=self.scale, interpolation=cv2.INTER_AREA)
        box = movement_box(self.detector.process(small))
        return None if box is None else tuple(int(round(v / self.scale)) for v in box)


def run_stream(args) -> int:
    output = sys.stdout.buffer
    sys.stdout = sys.stderr  # nothing but the messages on the standard output
    writing = threading.Lock()

    def tell(meta: dict, picture: bytes = b"") -> None:
        """A message to the chat: one at a time (the objects tell theirs from their thread)."""
        with writing:
            send(output, meta, picture)

    commands: queue.Queue = queue.Queue()
    threading.Thread(target=read_commands, args=(commands,), daemon=True).start()
    options = Options(args)
    models = Path(args.models)
    try:
        download_models(models, lambda text: tell({"message": text}))
        tell({"message": "Loading the face models…"})
        analyser = FaceAnalyser(models, Path(args.faces), options)
        # What is kept here, for the chat to write in its search engine what is missing there.
        known = analyser.faces.counts()
        tell({"message": f"{len(known)} name(s) enrolled ({sum(known.values())} picture(s))" if known else "Nobody enrolled yet: click the box of a face to name it",
                      "inventory": analyser.faces.inventory()})
    except Exception as error:  # noqa: BLE001 - no network for the download, a model that does not load…
        tell({"message": f"The face models could not be loaded: {error}", "error": True})
        return 4
    objects = Objects(args, lambda message: tell(message))
    if objects.on:
        objects.ensure()

    capture = open_source(args.source, args.width, args.height)
    if not capture.isOpened() and sys.platform == "darwin" and args.source.isdigit():
        tell({"message": "Waiting for the access to the camera: allow it in the dialog of macOS (or in System Settings > Privacy & Security > Camera)"})
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
        tell({"message": f"{what} could not be opened" + (" (macOS did not allow it: System Settings > Privacy & Security > Camera, for the chat)" if sys.platform == "darwin" and args.source.isdigit() else ""), "error": True})
        return 2

    frames = 0
    fps, spacing, last = 0.0, 0.0, time.perf_counter()
    period = 1.0 / max(1, args.fps)
    due = time.perf_counter()
    annotated = None
    recorder = FaceRecorder()
    movement = Movement()
    capture_next = False
    # The objects are detected in their own thread while the faces are read in this one: a frame takes the longer of
    # the two, not their sum (YOLO and the models of OpenCV both leave Python free while they compute).
    worker = ThreadPoolExecutor(max_workers=1, thread_name_prefix="objects")
    cv2.setNumThreads(OPENCV_THREADS)
    try:
        while True:
            while True:
                try:
                    command = commands.get_nowait()
                except queue.Empty:
                    break
                name, _, value = command.partition(" ")
                value = value.strip()
                flag = value not in ("", "0")
                if name == "quit":
                    for message in recorder.close():
                        tell(message)
                    objects.close()
                    return 0
                if objects.command(name, value, command):
                    continue
                if name == "enrol":
                    tell(enrol(analyser, value))
                elif name == "hold":
                    try:
                        x, y = (float(v) for v in value.split())
                        answer = hold(analyser, x, y)
                    except ValueError:
                        answer = {"message": "hold: the point of the face is expected (x y)", "error": True}
                    if answer:
                        tell(answer)
                elif name == "enrolheld":
                    tell(enrol_held(analyser, value))
                elif name == "enrolfile":
                    # {"name": …, "path": …}: a photo on the disk.
                    try:
                        asked = json.loads(value)
                        tell(enrol_file(analyser, str(asked.get("name", "")), str(asked.get("path", ""))))
                    except (json.JSONDecodeError, AttributeError):
                        tell({"message": "enrolfile: the name and the path of the photo are expected as JSON", "error": True})
                elif name == "forget":
                    tell(forget(analyser, value))
                elif name == "threshold" and value.isdigit():
                    options.threshold = min(max(int(value), 1), 99) / 100
                elif name == "expressions":
                    options.expressions = flag
                elif name == "landmarks":
                    options.landmarks = flag
                elif name == "motion":
                    options.motion = flag
                elif name == "movement":
                    options.movement = flag
                elif name == "capture":
                    # The next frame, as the camera gives it (nothing drawn on it), for the chat to analyse.
                    capture_next = True
                elif name == "record":
                    # Stopped: what was being recorded is closed.
                    if options.record and not flag:
                        for message in recorder.close():
                            tell(message)
                    options.record = flag
                elif name == "pause":
                    options.paused = flag
                elif name == "snapshot" and annotated is not None:
                    path = save_picture(annotated, Path(args.pictures))
                    tell({"message": f"Saved {path}"} if path else {"message": f"The picture could not be saved in {args.pictures}", "error": True})
            if options.paused:
                time.sleep(0.03)
                due = time.perf_counter()
                continue
            ok, frame = capture.read()
            if not ok:
                if frames == 0:
                    tell({"message": "no frame could be read from the camera", "error": True})
                    return 3
                tell({"message": "The video is over" if not args.source.isdigit() else "The camera stopped", "error": args.source.isdigit()})
                return 0
            if args.source.isdigit():
                frame = cv2.flip(frame, 1)
            if capture_next:
                capture_next = False
                ok, raw = cv2.imencode(".jpg", frame, [cv2.IMWRITE_JPEG_QUALITY, 90])
                if ok:
                    tell({"capture": True, "width": int(frame.shape[1]), "height": int(frame.shape[0])}, raw.tobytes())
            pending = worker.submit(objects.process, frame) if objects.on else None
            analysis = analyser.analyse(frame)
            # What moves: one rectangle around all of it (the background learnt by MOG2, as in vision_move.py).
            moving = movement.box(frame) if options.movement else None
            drawn, seen = pending.result() if pending is not None else (None, {})
            annotated, found = analyser.draw(frame.copy() if drawn is None else drawn, analysis)
            found = {**seen, **found, "objects_on": objects.on, "movement": None}
            if moving is not None:
                x, y, w, h = moving
                cv2.rectangle(annotated, (x, y), (x + w, y + h), MAGENTA, 2)
                found["movement"] = [x, y, w, h]
            frames += 1
            if options.record:
                for message in recorder.frame(time.time(), found["people"], frame.shape[1], frame.shape[0]):
                    tell(message)
            for person in found["people"]:
                person.pop("probabilities", None)
            now = time.perf_counter()
            # The rate, from the mean time between two frames (the mean of the rates of each would say too much as soon
            # as these times are not all the same).
            spacing = 0.9 * spacing + 0.1 * (now - last) if spacing else now - last
            fps = 1 / max(spacing, 1e-6)
            last = now
            ok, jpeg = cv2.imencode(".jpg", annotated, [cv2.IMWRITE_JPEG_QUALITY, 80])
            if ok:
                tell({**found, "fps": round(fps, 1), "width": int(annotated.shape[1]), "height": int(annotated.shape[0])}, jpeg.tobytes())
            if args.frames and frames >= args.frames:
                return 0
            # The next frame is due one period after this one was (not after it was done: a pause a little too long, as
            # macOS makes them, is made up at the next frame, and the rate asked is kept); late, nothing is caught up.
            due += period
            now = time.perf_counter()
            if due > now:
                time.sleep(due - now)
            else:
                due = now
    except (BrokenPipeError, OSError):
        return 0  # the chat closed the window
    finally:
        capture.release()
        worker.shutdown(wait=True)
        # What was being recorded is closed, when the chat still listens.
        try:
            for message in recorder.close():
                tell(message)
            objects.close()
        except (BrokenPipeError, OSError, ValueError):
            pass


def run_window(args) -> int:
    options = Options(args)
    models = Path(args.models)
    download_models(models)
    analyser = FaceAnalyser(models, Path(args.faces), options)
    capture = open_camera_when_allowed(args.source, args.width, args.height, args.wait)
    if not capture.isOpened():
        print(f"Error: the camera or the video '{args.source}' could not be opened", file=sys.stderr)
        return 2
    if args.enrol:
        ok, frame = capture.read()
        if ok:
            analyser.process(frame)
        print(enrol(analyser, args.enrol)["message"], flush=True)
        capture.release()
        return 0
    if not args.no_window:
        cv2.namedWindow(WINDOW, cv2.WINDOW_NORMAL)
        print("q or Esc to quit; e: enrol the largest face, x: expressions, l: landmarks, m: movement, s: picture", flush=True)
    frames = 0
    fps, last = 0.0, time.perf_counter()
    annotated, found = None, {"faces": 0, "people": [], "known": {}}
    while True:
        if not options.paused or annotated is None:
            ok, frame = capture.read()
            if not ok:
                break
            if args.source.isdigit():
                frame = cv2.flip(frame, 1)
            annotated, found = analyser.process(frame)
            frames += 1
            now = time.perf_counter()
            fps = 0.9 * fps + 0.1 / max(now - last, 1e-6) if fps else 1 / max(now - last, 1e-6)
            last = now
            height, width = annotated.shape[:2]
            cv2.rectangle(annotated, (0, height - 30), (width, height), (0, 0, 0), cv2.FILLED)
            label(annotated, f"{found['faces']} face(s): {describe(found)}  |  {len(found['known'])} name(s) enrolled  |  {fps:4.1f} fps", (10, height - 9), WHITE, 0.5, 1)
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
        elif key == ord("x"):
            options.expressions = not options.expressions
        elif key == ord("l"):
            options.landmarks = not options.landmarks
        elif key == ord("m"):
            options.motion = not options.motion
        elif key == ord("e"):
            print(enrol(analyser, input("Name of the largest face: "))["message"], flush=True)
        elif key == ord("s"):
            print(f"Saved {save_picture(annotated, Path(args.pictures))}", flush=True)
        try:
            if cv2.getWindowProperty(WINDOW, cv2.WND_PROP_VISIBLE) < 1:
                break
        except cv2.error:
            break
    capture.release()
    cv2.destroyAllWindows()
    print(f"{frames} frame(s); last: {found['faces']} face(s): {describe(found)}", flush=True)
    return 0


def main() -> int:
    home = Path.home() / "kaonomi"
    parser = argparse.ArgumentParser(description="Face and facial expression recognition with OpenCV (/vision of the Kaonomi chat)")
    parser.add_argument("--source", default="0", help="index of the camera (0 by default), a video file or a picture")
    parser.add_argument("--width", type=int, default=640, help="width asked to the camera")
    parser.add_argument("--height", type=int, default=480, help="height asked to the camera")
    parser.add_argument("--models", default=str(home / "models" / "face"), help="folder of the models (downloaded there when missing)")
    parser.add_argument("--faces", default=str(home / "vision" / "faces"), help="folder of the faces enrolled (one folder per name)")
    parser.add_argument("--threshold", type=float, default=0.363, help="smallest cosine similarity to recognise a face (0.363: the one of SFace)")
    parser.add_argument("--pictures", default=str(Path.home() / "Pictures" / "kaonomi" / "vision"), help="folder of the pictures saved")
    parser.add_argument("--wait", type=float, default=45, help="macOS: seconds given to allow the camera in the dialog of the system")
    parser.add_argument("--stream", action="store_true", help="no window: the frames go to the standard output, the commands come on the standard input (the chat)")
    parser.add_argument("--fps", type=int, default=25, help="with --stream: the most frames sent per second")
    parser.add_argument("--no-record", action="store_true", help="with --stream: do not tell the chat what is seen of the faces (who, emotions, area, movement) for its search engine")
    parser.add_argument("--objects", action="store_true", help="with --stream: detect the objects too (YOLOv8, vision_yolo.py) on the same frames")
    parser.add_argument("--model", choices=sorted(yolo.MODELS), default="n", help="the YOLO model: n (nano, fast), m (medium), l (large), x (extra large)")
    parser.add_argument("--yolo-models", default=str(home / "models" / "yolo"), help="folder of the YOLO models (downloaded there when missing)")
    parser.add_argument("--conf", type=float, default=0.5, help="smallest confidence of an object kept (0 to 1)")
    parser.add_argument("--persons-only", action="store_true", help="detect the persons only, not the other objects")
    parser.add_argument("--alert-class", default="wine glass", help="alert when an object of this class is seen (empty: none)")
    parser.add_argument("--log", action="store_true", help="write the objects detected as JSON lines")
    parser.add_argument("--log-directory", default=str(home / "vision" / "yolo"), help="folder of the JSON lines of the objects")
    parser.add_argument("--record-seconds", type=float, default=60, help="the objects detected summed up and told to the chat every this many seconds (60)")
    parser.add_argument("--no-record-objects", action="store_true", help="with --stream: do not tell the chat what objects were detected for its search engine")
    parser.add_argument("--download", action="store_true", help="download the models into --models, then stop")
    parser.add_argument("--enrol", default="", help="enrol the largest face of the first frame of --source under this name, then stop")
    parser.add_argument("--frames", type=int, default=0, help="stop after this many frames (0: never; for the tests)")
    parser.add_argument("--no-window", action="store_true", help="no window: what was seen is printed (for the tests)")
    args = parser.parse_args()
    if args.download:
        download_models(Path(args.models))
        for key in MODELS:
            print(f"{MODELS[key][1]}: {model_path(Path(args.models), key)}", flush=True)
        return 0
    return run_stream(args) if args.stream else run_window(args)


if __name__ == "__main__":
    sys.exit(main())

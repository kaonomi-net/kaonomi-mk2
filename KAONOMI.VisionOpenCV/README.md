[www.kaonomi.net](https://www.kaonomi.net)

# KAONOMI.VisionOpenCV

The vision applications of the chat written in **Python with OpenCV**: classic computer vision with no neural
network (LeLabIA-VisionOpenCV), and object detection with
YOLOv8 (LeLabIA-VisionYolo), the two tutorials of Le Lab IA.

| Command of the chat | Script | What it does |
| --- | --- | --- |
| `/vision:move` | `vision_move.py` | Motion detection: the moving zones, their contours, the gravity centre of what moves and its trail |
| `/vision` | `vision_face.py --objects` (and the YOLO of `vision_yolo.py`) | Face recognition (who it is, once enrolled) and facial expression recognition (7 expressions), and on the same frames the object detection of YOLOv8: the boxes, the centre and the direction of each person, alerts, JSON log (one camera, one process) |

## The camera

Each vision window has a **Camera** list: the cameras of this machine by their name. Choosing one starts the
detection again on it, and the choice is kept for the other vision windows and the next openings
(`~/kaonomi/vision/camera.txt`). The list comes from `vision_cameras.py`: on macOS the names are the ones of the
system, read without opening any camera, in the order OpenCV numbers them (their unique identifiers); elsewhere
OpenCV does not know the names, so the indexes 0 to 5 are tried and named "Camera 1", "Camera 2"… The scripts take
the camera as `--source <index>`.

## /vision:move

Based on `moving_contours.py` and `motion_detection.py`: the background is learnt (MOG2 background subtraction), what
differs from it is the motion; the mask is cleaned (opening, dilation), the zones smaller than **Min area** are
dropped, the contours of the others are the moving zones, and the moments of the mask give the gravity centre of
everything that moves (the red cross).

In the chat, the script runs with `--stream`, without a window of its own: it sends each frame drawn to the window
`/vision:move` of the Kaonomi desktop, and takes from it what its buttons and sliders ask (see the protocol below).
Alone in a terminal, it opens its own window.

Four views of the same frame, together (the mosaic) or one at a time:

| | |
| --- | --- |
| **Camera + zones**: the contours (green), the boxes with their area (yellow), the gravity centre (red cross) and its trail (blue) | **Foreground mask**: the moving pixels, in white |
| **Contours + trail**: the same drawing alone, on black | **Motion highlight**: the picture of the camera where it moves, black elsewhere |

Under them: MOTION or still, the number of zones, the part of the frame that moves, the gravity centre, the frames
per second. Two sliders: **Min area** (the smallest zone kept, in pixels) and **Threshold** (of the background
subtraction: the lower, the more sensitive).

In the window of the chat: **View**, **⏸ PAUSE**, **⟲ BACKGROUND**, **📷 PICTURE**, **📁 FOLDER**, the two sliders and
**TRAIL**. In its own window (terminal), the keys:

| Key | Effect |
| --- | --- |
| `q`, `Esc` | Quit (the close button of the window too) |
| `space` | Pause |
| `v` | The mosaic, then each view alone |
| `r` | Forget the background and learn it again (the camera moved, the light changed) |
| `t` | The trail of the gravity centre, on / off |
| `s` | Save the picture shown in `~/Pictures/kaonomi/vision` |
| `h` | The keys |

## /vision: the objects

In the chat, `/vision` shows the faces and the objects in one window: `vision_face.py --stream --objects` runs the `Detector` of
`vision_yolo.py` on each frame before the faces (class `Objects` of `vision_face.py`), with the same commands (`model`, `conf`,
`persons`, `centers`, `vectors`, `overlap`, `alert`, `log`) plus `objects 1|0` and `recordobjects 1|0`. Alone, in a terminal,
`vision_yolo.py` still opens its own window.

The scripts of LeLabIA-VisionYolo in one application, each as an option:

| Script of the tutorial | Here |
| --- | --- |
| `01-GravityCenter` | **CENTRES**: the centre of the box of each person, with its coordinates |
| `02-Vecteurdedirection` | **DIRECTIONS**: each person followed from frame to frame by the nearest centre; the trail of its last 4 positions and the arrow of its average direction |
| `03-PersonOverlapDetection` | **OVERLAP**: an alert (and red boxes) when the boxes of two persons overlap (IoU > 0) |
| `04-WineGlassDetector` | **Alert on**: an alert when an object of a class is seen, `wine glass` by default (any class of COCO; empty: none) |
| `05-SaveJsonFiles` | **JSON LOG**: the detections as JSON lines (timestamp, model, object, class, confidence, box, centre), at most once a second, one file per session in `~/kaonomi/vision/yolo` |

`06-SavetoES` (the detections sent to Elasticsearch) is not there yet.

The models are in `~/kaonomi/models/yolo`: the ones of the tutorial, `yolov8n.pt` (nano, 6 MB, fast) and
`yolov8m.pt` (medium, 50 MB, more accurate), and two larger ones of the same family, `yolov8l.pt` (large, 84 MB) and
`yolov8x.pt` (extra large, 131 MB: the most accurate, the slowest); Ultralytics downloads the one asked when it is missing
(`vision_yolo.py --download` fetches them all). They run on the GPU when PyTorch can use it (Metal on a Mac, CUDA), else
on the processor. `requirements-yolo.txt` (Ultralytics, which brings PyTorch) is installed in the venv the first time
`/vision` is opened: several minutes.

**What is recorded** (`objects_record.py`; the switch **RECORD** of the window, `--no-record` to start without it):
once a minute (`--record-seconds`), everything detected during that minute is summed up: each kind of object with
the most of them at once, their mean number, the share of the frames where one was seen, how sure the detections
were and when it was first and last seen; the most objects and persons at once; the share of the frames with an
overlap or an object of the class watched. The script sends it (message `detected`) and the window of the chat
writes it in the search engine (index `visionobjects`, see [`KAONOMI.Server`](../KAONOMI.Server/README.md)); the
last, shorter period is sent when the window closes.

Its commands on the standard input (`--stream`): `model n`, `model m`, `model l` or `model x`, `conf 50` (per cent), `persons 1`,
`centers 1`, `vectors 1`, `overlap 1`, `alert wine glass` (`alert` alone: none), `log 1`, `record 1`, `pause 1`,
`snapshot`, `quit`. The JSON of a frame holds `objects`, `persons`, `counts` (`{"person": 2, "cup": 1}`), `overlap`, `alert`,
`alert_class`, `model`, `device`, `fps`, `width`, `height`.

In a terminal: `~/kaonomi/vision/venv/bin/python vision_yolo.py` opens its own window (keys: `q`, `space`, `m` model,
`p` persons only, `c`, `d`, `o`, `j` JSON log, `s` picture); `--help` lists its arguments.

## /vision: the faces

Face recognition and facial expression recognition with OpenCV alone: only open source, and everything on this
machine (no picture leaves it). OpenCV (Apache 2.0) runs three small models of the
[OpenCV Zoo](https://github.com/opencv/opencv_zoo):

| Model | File | Licence | What it gives |
| --- | --- | --- | --- |
| YuNet | `face_detection_yunet_2026may.onnx` | MIT | The faces of the frame and 5 landmarks of each (eyes, nose, corners of the mouth) |
| SFace | `face_recognition_sface_2021dec.onnx` | Apache 2.0 | A face, aligned, as 128 numbers: two faces are compared by their cosine similarity |
| MobileFaceNet | `facial_expression_recognition_mobilefacenet_2022july.onnx` | Apache 2.0 | The expression: angry, disgust, fearful, happy, neutral, sad, surprised |

They are kept in `~/kaonomi/models/face`, downloaded from the OpenCV Zoo when missing (`vision_face.py --download`
fetches them); `requirements-face.txt` only adds certifi, for that download.

**Who is who**: a face is recognised when its similarity with a face enrolled under a name reaches the threshold
(0.363, the one of SFace; **Similarity** in the window). Enrolling keeps a face seen now, aligned (112 × 112), in
`~/kaonomi/vision/faces/<name>/`: in the window of the chat, the face clicked (its box), named in a popup; several
pictures per name, from other angles and lights, make the recognition steadier. Forgetting a name deletes its folder. The window of the chat keeps the search engine of
the Kaonomi server like this folder (index `face`: one document per picture, with the name, the picture in base 64
and the 128 numbers of SFace): what is missing on one side is copied from the other at the start, a face enrolled is
sent at once, a name forgotten is really erased there too. The script itself never talks to the server: it tells the
chat what it keeps (the messages `inventory`, `enrolled`, `forgotten`). The expression is the best guess of a small model on
one picture: an indication, not a measure of what someone feels.

**The movement** (`face_motion.py`; the switch **MOTION** of the window, `m` in its own window): each face is followed
from frame to frame (the closest centre of the frame before, within 1.5 times the width of the face; the centre
smoothed, half of the way at each frame, so that the jitter of the detector does not shake it). Its centre of gravity
is a white dot; an arrow goes from it in the direction it moves, as long as the way the centre goes in half a second
(its speed over the last 0.3 s; at most twice the width of the face), and none while the face is still (under the
threshold STILL of `face_record.py`, the one of the record); a yellow trail shows where the centre went, each piece
fainter as it gets older, gone after 3 seconds (it keeps fading when the face has left).

**What is recorded** (`face_record.py`; the switch **RECORD** of the window, `--no-record` to start without it): each
face is followed from frame to frame (two unknown faces are two tracks) and summed up every second: who it is, the
share of each of the seven emotions (per cent), the area of its rectangle (per cent of the picture) and its movement
(how far its middle goes, in per cent of the width of the face per second), each with its smallest and largest
value, its mean and its standard deviation. These seconds are gathered in **episodes**: a period where the state of
a face stays the same (the same person, the same leading emotion, the same activity: still, moving, agitated). The
script sends the episode again every second while it lasts (message `episode`, the same id); another one starts when
something changes and holds 2 seconds (a one-second blip is ignored), when the face leaves, or after 60 seconds. At
the instant of a change it also sends that second (message `observation`: `appeared`, `emotion`, `identity`,
`activity`, `left`, `stopped`). The window of the chat writes both in the search engine (indexes `faceepisode` and
`faceobservation`, see [`KAONOMI.Server`](../KAONOMI.Server/README.md)); the expressions are computed for the record
even when EXPRESSIONS is off.

Its commands on the standard input (`--stream`): `enrol <name>` (the largest face seen now), `hold <x> <y>` (the face
of the last frame at this point, in the pixels of the frame, is kept until it is named: the one clicked in the chat),
`enrolheld <name>` (the face kept by `hold`), `enrolfile {"name": …, "path": …}` (the largest face of a photo on the disk: ADD RECO of the chat), `forget <name>`, `threshold 36` (per cent),
`expressions 1`, `landmarks 1`, `motion 1`, `movement 1` (the rectangle around what moves, the `MotionDetector` of vision_move.py, drawn in magenta and sent as `movement`: `[x, y, w, h]` or null), `record 1`, `pause 1`, `snapshot`, `quit`. The JSON of a frame holds `faces`, `people`
(`[{"name", "known", "score", "expression", "confidence", "box"}]`, the largest face first), `known`
(`{"Anna": 3}`: the pictures kept per name), `fps`, `width`, `height`. Three messages hold more than their text:
`inventory` at the start (`[{"name", "file", "feature"}]`: every picture kept), `enrolled`
(`{"name", "file", "feature", "count"}`) and `forgotten` (the name).

In a terminal: `~/kaonomi/vision/venv/bin/python vision_face.py` opens its own window (keys: `q`, `space`, `e` enrol,
`x` expressions, `l` landmarks, `m` movement, `s` picture); `--enrol <name> --source picture.jpg` enrols the largest face of a
picture; `--help` lists its arguments.

### What a frame costs

With `--stream`, the time of a frame, before and now (the objects on, 25 frames a second asked):

| Frame | Before | Now |
|---|---|---|
| 640 × 480, one face | 40 ms | 14 ms |
| 1280 × 720, one face | 67 ms (15 frames a second at most) | 16 ms |
| 640 × 480, three faces | 77 ms | 19 ms |
| 1280 × 720, three faces | 101 ms (10 frames a second at most) | 22 ms |

At 1280 × 720 it took 92 % of a core of the processor for 16 frames a second; it takes 60 % for the 25 asked.

- **The objects in their own thread.** YOLO runs while the faces are read: a frame takes the longer of the two, not
  their sum (PyTorch and the models of OpenCV both leave Python free while they compute). What the objects tell the
  chat is written one message at a time.
- **A face followed is not read again at each frame.** The faces are found at each frame (YuNet: their boxes, their
  movement), but who a face is (SFace, 13 ms a face) is read again every 0.2 s only, and what it expresses (5 ms a
  face) every 0.12 s (`IDENTITY_SECONDS`, `EXPRESSION_SECONDS`), kept with its track meanwhile. A face just seen is
  read at once; every face is after an enrolment, a name forgotten or another threshold. The names and the
  expressions sent are the same; the similarity shown moves 5 times a second.
- **The faces looked for on a smaller frame.** YuNet costs as many times more as the frame has pixels: a frame wider
  than 640 (`DETECT_WIDTH`) is made 640 wide for it, the boxes and the landmarks brought back to its pixels, and who a
  face is still read on the frame itself. A face under about 40 pixels wide (someone far) is not found on the
  smaller frame: 4 times a second (`WHOLE_SECONDS`) the frame is also read whole for the faces under 64 pixels
  (`SMALL_FACE`), kept meanwhile; someone far is found as before, down to 16 pixels. The boxes of the other faces
  always come from the smaller frame (they do not jump from one reading to the other): at 1280 × 720 they are a few
  pixels from what they were, and up to 6 % larger. Frames of 640 or less: nothing changes.
- **One thread for OpenCV** (`OPENCV_THREADS`). Ultralytics, once loaded, left OpenCV one thread; with more, macOS
  spreads each model over every core: a frame is done sooner, for half more processor time. One is kept, on purpose.
- **The movement on a smaller frame.** The background (MOG2) is learnt on the frame made 320 pixels wide: the
  rectangle is the same to a few pixels.
- **The rate kept.** Each frame is due one period after the one before was due, not after it was done: a pause a
  little too long (macOS makes them) is made up at the next frame. The rate shown is the one of the mean time between
  two frames.

The chat decodes each frame (a JPEG) off its main thread, in a buffer it uses again.

## Run

From the chat: `/vision:move` (or its 👁 icon) opens the window. The first time, the chat builds the venv
`~/kaonomi/vision/venv` with the Python of the machine (3.9 or newer) and installs `requirements.txt` in it
(opencv-python, numpy): a minute or two. On macOS, the camera must be allowed for the chat (System Settings > Privacy &
Security > Camera): the script waits up to 45 s for the answer to the dialog of the system. A chat started from a
shell that has no access to the camera is refused: start it as an application (`open -a Godot_mono --args --path …`).

From a terminal:

```bash
python3 -m venv ~/kaonomi/vision/venv
~/kaonomi/vision/venv/bin/pip install -r requirements.txt
~/kaonomi/vision/venv/bin/python vision_move.py
```

| Argument | Effect |
| --- | --- |
| `--source <n or file>` | The camera of this index (0 by default), or a video file |
| `--width`, `--height` | The size asked to the camera (640 × 480 by default) |
| `--min-area <pixels>` | The smallest moving zone kept (800 by default) |
| `--sensitivity <n>` | The threshold of the background subtraction (25 by default) |
| `--pictures <folder>` | Where `s` saves the pictures |
| `--wait <seconds>` | macOS: the time given to allow the camera in the dialog of the system (45 by default) |
| `--stream`, `--fps <n>` | No window: the frames go to the standard output (at most n per second, 25 by default), the commands come on the standard input |
| `--no-window`, `--frames <n>` | No window, stop after n frames: the counts are printed (for the tests) |

## The stream (`--stream`)

On the standard output, one message after the other: the length of a JSON text (4 bytes, big endian), the JSON, the
length of a JPEG picture (4 bytes, 0 when there is none), the picture.

- A frame: `{"state": "learning" | "still" | "motion", "zones": 2, "ratio": 0.031, "center": [320, 240], "fps": 24.8, "width": 1280, "height": 960}` and its picture.
- A message: `{"message": "…"}`, with `"error": true` when it is one; no picture.

On the standard input, one command per line: `view 0` (0: the mosaic, 1 to 4: one view), `min_area 800`,
`threshold 25`, `trail 1`, `pause 1`, `reset` (the background learnt again), `snapshot`, `quit`. The input closed
quits too.

"""
The movement of the faces, drawn on the frames of /vision (the switch MOTION of the window).

For each face: the centre of gravity of its box (a white dot), an arrow from it in the direction it moves (as long as
the way the centre goes in half a second; none while the face is still), and a yellow trail of where the centre went,
fading out in 3 seconds (it keeps fading when the face is gone).

Each face is followed from frame to frame: the closest centre of the frame before (within 1.5 times the width of the
face) is the same face. Its centre is smoothed (half of the way to the new one at each frame: the detector moves the
box by a pixel or two from one frame to the next, the trail would shake). The speed is the way gone in the last 0.3
seconds, steadier than from one frame to the next.
A face is still under STILL of face_record.py (per cent of its width per second): the same measure as the record.
"""

from __future__ import annotations

import math
from collections import deque

import cv2
import numpy as np

from face_record import STILL

TRAIL_SECONDS = 3.0     # the trail fades out in this long
SPEED_SECONDS = 0.3     # the speed: the way gone in this long
ARROW_SECONDS = 0.5     # the arrow: where the centre goes in this long
LOST_SECONDS = 0.5      # a face not seen this long: its track ends (its trail keeps fading)
ARROW_LONGEST = 2.0     # the longest arrow, in widths of the face
SMOOTHING = 0.5         # the share of the new centre taken at each frame

TRAIL_COLOR = (0, 255, 255)     # yellow (BGR)
ARROW_COLOR = (255, 200, 0)     # light blue
CENTRE_COLOR = (255, 255, 255)
DARK = (20, 20, 20)


class Track:
    """A face followed: where its centre was (moment, x, y), the last width of its box, when it was seen."""

    def __init__(self, moment: float, x: float, y: float, width: float):
        self.points: deque[tuple[float, float, float]] = deque([(moment, x, y)])
        self.width, self.seen = width, moment
        # What vision_face.py read of this face and when, kept from a frame to the next ones (who it is, what it
        # expresses): a face followed is not read again at each frame.
        self.identity: tuple | None = None
        self.expression: tuple | None = None

    @property
    def centre(self) -> tuple[float, float]:
        return self.points[-1][1], self.points[-1][2]

    def velocity(self) -> tuple[float, float] | None:
        """Pixels per second, over the last SPEED_SECONDS; None while too few frames tell it."""
        moment, x, y = self.points[-1]
        oldest = None
        for point in reversed(self.points):
            if moment - point[0] > SPEED_SECONDS:
                break
            oldest = point
        if oldest is None or moment - oldest[0] < 0.05:
            return None
        elapsed = moment - oldest[0]
        return (x - oldest[1]) / elapsed, (y - oldest[2]) / elapsed


class FaceMotion:
    """The tracks of the faces seen, and what is drawn of them."""

    def __init__(self):
        self.tracks: list[Track] = []

    def update(self, moment: float, boxes: list[list[int]]) -> list[Track]:
        """The boxes of a frame ([x, y, w, h], the largest first): the track of each, in their order."""
        live = [t for t in self.tracks if moment - t.seen <= LOST_SECONDS]
        found: list[Track] = []
        for x, y, w, h in boxes:
            cx, cy = x + w / 2, y + h / 2
            best, best_distance = None, 1.5 * max(w, 1)
            for track in live:
                if track in found:
                    continue
                distance = math.hypot(cx - track.centre[0], cy - track.centre[1])
                if distance < best_distance:
                    best, best_distance = track, distance
            if best is None:
                best = Track(moment, cx, cy, w)
                self.tracks.append(best)
            else:
                px, py = best.centre
                best.points.append((moment, px + SMOOTHING * (cx - px), py + SMOOTHING * (cy - py)))
                best.width, best.seen = w, moment
            found.append(best)
        # The points older than the trail go; a track with none left is forgotten.
        for track in self.tracks:
            while track.points and moment - track.points[0][0] > TRAIL_SECONDS:
                track.points.popleft()
        self.tracks = [t for t in self.tracks if t.points]
        return found

    def draw_trails(self, image: np.ndarray, moment: float) -> None:
        """The yellow trails, under the boxes: each piece fainter as it gets older."""
        thickness = max(2, round(image.shape[1] / 320))
        for track in self.tracks:
            points = list(track.points)
            for (_, x0, y0), (when, x1, y1) in zip(points, points[1:]):
                alpha = 1 - (moment - when) / TRAIL_SECONDS
                if alpha > 0.02:
                    blend_line(image, (round(x0), round(y0)), (round(x1), round(y1)), TRAIL_COLOR, alpha, thickness)

    def draw_marks(self, image: np.ndarray, found: list[Track]) -> None:
        """Over the boxes: the centre of each face of this frame and the arrow of its movement."""
        thickness = max(2, round(image.shape[1] / 320))
        for track in found:
            cx, cy = track.centre
            centre = (round(cx), round(cy))
            velocity = track.velocity()
            if velocity is not None:
                speed = math.hypot(*velocity)
                # Still (under STILL per cent of its width per second): no arrow, the jitter of the detector aside.
                if speed >= STILL / 100 * track.width:
                    length = min(speed * ARROW_SECONDS, ARROW_LONGEST * track.width)
                    end = (round(cx + velocity[0] / speed * length), round(cy + velocity[1] / speed * length))
                    tip = min(0.4, 14 / max(length, 1))
                    cv2.arrowedLine(image, centre, end, DARK, thickness + 2, cv2.LINE_AA, tipLength=tip)
                    cv2.arrowedLine(image, centre, end, ARROW_COLOR, thickness, cv2.LINE_AA, tipLength=tip)
            cv2.circle(image, centre, thickness + 4, DARK, cv2.FILLED, cv2.LINE_AA)
            cv2.circle(image, centre, thickness + 2, CENTRE_COLOR, cv2.FILLED, cv2.LINE_AA)


def blend_line(image: np.ndarray, a: tuple[int, int], b: tuple[int, int], color, alpha: float, thickness: int) -> None:
    """A line drawn at this opacity: only the small rectangle around it is mixed."""
    height, width = image.shape[:2]
    margin = thickness + 1
    x0, y0 = max(min(a[0], b[0]) - margin, 0), max(min(a[1], b[1]) - margin, 0)
    x1, y1 = min(max(a[0], b[0]) + margin + 1, width), min(max(a[1], b[1]) + margin + 1, height)
    if x1 <= x0 or y1 <= y0:
        return
    region = image[y0:y1, x0:x1]
    drawn = region.copy()
    cv2.line(drawn, (a[0] - x0, a[1] - y0), (b[0] - x0, b[1] - y0), color, thickness, cv2.LINE_AA)
    cv2.addWeighted(drawn, alpha, region, 1 - alpha, 0, dst=region)

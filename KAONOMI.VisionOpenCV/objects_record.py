"""
What /vision records of the objects it detects, for the search engine of the Kaonomi server.

Every minute (--record-seconds), everything detected during that minute is summed up in one document: each kind of
object with the most of them seen at once, their mean number, the share of the frames where one was seen, how sure
the detections were and when it was seen for the first and the last time; the most objects and persons at once; the
share of the frames where two persons overlapped, and where an object of the class watched was seen. The script
sends it to the chat ("detected" message), which writes it in the search engine (index visionobjects). When the
window closes, the last, shorter period is sent too.

The document is the one of the server (VisionObjects: KAONOMI.Core/Models/VisionObjects.cs).
"""

from __future__ import annotations

import getpass
import socket
import uuid
from datetime import datetime, timezone


def iso(moment: float) -> str:
    return datetime.fromtimestamp(moment, timezone.utc).isoformat(timespec="milliseconds").replace("+00:00", "Z")


class ObjectsRecorder:
    """Sums up the detections period after period: `frame` returns the document of a period when it ends, else None."""

    def __init__(self, seconds: float = 60):
        self.seconds = max(1.0, seconds)
        self.session = uuid.uuid4().hex[:12]
        self.origin = {"appName": "KAONOMI.VisionOpenCV", "machineName": socket.gethostname(), "userName": getpass.getuser()}
        self.number = 0
        self._reset(None)

    def _reset(self, start: float | None) -> None:
        self.start = start
        self.last = start
        self.frames = 0
        self.kinds: dict[str, dict] = {}
        self.max_objects = self.max_persons = 0
        self.overlap = self.alert = 0
        self.found: dict = {}

    def frame(self, moment: float, detections: list[dict], found: dict, threshold: float) -> dict | None:
        """The detections of a frame ({"object", "class_id", "confidence"}) and what was found in it (see vision_yolo.py)."""
        done = None
        if self.start is not None and moment - self.start >= self.seconds:
            done = self.document(self.start + self.seconds)
            self._reset(None)
        if self.start is None:
            self.start = moment
        self.last = moment
        self.frames += 1
        self.found, self.threshold = found, threshold
        counts: dict[str, int] = {}
        for detection in detections:
            name = detection["object"]
            counts[name] = counts.get(name, 0) + 1
            kind = self.kinds.setdefault(name, {"classId": detection["class_id"], "max": 0, "total": 0, "frames": 0, "confidence": 0.0, "best": 0.0,
                                                "first": moment - self.start, "last": moment - self.start})
            kind["total"] += 1
            kind["confidence"] += detection["confidence"]
            kind["best"] = max(kind["best"], detection["confidence"])
            kind["last"] = moment - self.start
        for name, count in counts.items():
            kind = self.kinds[name]
            kind["max"] = max(kind["max"], count)
            kind["frames"] += 1
        self.max_objects = max(self.max_objects, len(detections))
        self.max_persons = max(self.max_persons, int(found.get("persons", 0)))
        self.overlap += 1 if found.get("overlap") else 0
        self.alert += 1 if found.get("alert") else 0
        return done

    def document(self, end: float | None = None) -> dict | None:
        """The document of the period so far; None when it has no frame."""
        if self.start is None or self.frames == 0:
            return None
        end = end if end is not None else (self.last or self.start)
        self.number += 1
        objects = [{
            "name": name, "classId": kind["classId"], "max": kind["max"], "mean": round(kind["total"] / self.frames, 3),
            "presence": round(100 * kind["frames"] / self.frames, 1), "confidence": round(kind["confidence"] / kind["total"], 3), "best": round(kind["best"], 3),
            "firstSeen": round(kind["first"], 1), "lastSeen": round(kind["last"], 1),
        } for name, kind in self.kinds.items()]
        objects.sort(key=lambda o: (-o["presence"], -o["mean"], o["name"]))
        names = [o["name"] for o in objects]
        return {
            **self.origin, "id": f"visionobjects-{self.session}-{self.number}", "date": iso(self.start),
            "title": ", ".join(f"{o['max']} {o['name']}" for o in objects[:8]) or "nothing",
            "session": self.session, "start": iso(self.start), "end": iso(end), "seconds": round(end - self.start, 1), "frames": self.frames,
            "model": self.found.get("model", ""), "device": self.found.get("device", ""), "threshold": round(self.threshold, 2),
            "objects": objects, "names": names, "maxObjects": self.max_objects, "maxPersons": self.max_persons,
            "overlap": round(100 * self.overlap / self.frames, 1), "alertClass": self.found.get("alert_class", ""),
            "alert": round(100 * self.alert / self.frames, 1),
        }

    def close(self) -> dict | None:
        """The last period, shorter (the window closes, or the recording is switched off)."""
        done = self.document()
        self._reset(None)
        return done

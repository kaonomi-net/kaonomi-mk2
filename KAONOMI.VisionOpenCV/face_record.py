"""
What /vision records of the faces it sees, for the search engine of the Kaonomi server.

Each face is followed from frame to frame (the nearest rectangle: two unknown faces are two tracks). Every second,
the frames of that second are summed up for each face: the share of each emotion (per cent), the area of the
rectangle of the face (per cent of the picture) and its movement (how far its middle goes, in per cent of the width
of the face per second), each with its smallest and largest value, its mean and its standard deviation, and who the
face is.

These seconds are not all written. They are gathered in episodes: a period where the state of a face stays the same
(the same person, the same leading emotion, the same activity: still, moving or agitated). An episode is written
again every second while it lasts ("episode" message, the same id); another one starts when something changes and
holds 2 seconds, when the face leaves, or after 60 seconds. At the instant of a change, the second itself is written
too ("observation" message: appeared, emotion, identity, activity, left). So the exact time of each change is kept,
without one document per second.

The documents are the ones of the server (FaceEpisode, FaceObservation: KAONOMI.Core/Models/FaceEpisode.cs).
"""

from __future__ import annotations

import getpass
import math
import socket
import uuid
from datetime import datetime, timezone

EMOTIONS = ("angry", "disgust", "fearful", "happy", "neutral", "sad", "surprised")

HOLD_SECONDS = 2        # a change must hold this long to end an episode
MAX_SECONDS = 60        # the longest episode
LOST_SECONDS = 1.5      # a face not seen this long has left
MIN_FRAMES = 3          # a second with fewer frames of a face is not summed up
STILL, MOVING = 25.0, 120.0   # the movement (per cent of the width of the face per second) under which a face is still, then moving; over: agitated


def now_iso(moment: float | None = None) -> str:
    stamp = datetime.fromtimestamp(moment, timezone.utc) if moment is not None else datetime.now(timezone.utc)
    return stamp.isoformat(timespec="milliseconds").replace("+00:00", "Z")


class Stats:
    """The count, sum, sum of squares, smallest and largest of a measure: its spread, and it can be added to another."""

    def __init__(self):
        self.count, self.total, self.squares, self.low, self.high = 0, 0.0, 0.0, math.inf, -math.inf

    def add(self, value: float) -> None:
        self.count += 1
        self.total += value
        self.squares += value * value
        self.low, self.high = min(self.low, value), max(self.high, value)

    def merge(self, other: "Stats") -> None:
        self.count += other.count
        self.total += other.total
        self.squares += other.squares
        self.low, self.high = min(self.low, other.low), max(self.high, other.high)

    @property
    def mean(self) -> float:
        return self.total / self.count if self.count else 0.0

    def spread(self) -> dict:
        if not self.count:
            return {"min": 0.0, "max": 0.0, "mean": 0.0, "std": 0.0}
        variance = max(0.0, self.squares / self.count - self.mean ** 2)
        return {"min": round(self.low, 3), "max": round(self.high, 3), "mean": round(self.mean, 3), "std": round(math.sqrt(variance), 3)}


class Second:
    """What was seen of a face during one second."""

    def __init__(self, start: float):
        self.start = start
        self.frames = 0
        self.emotions = [0.0] * len(EMOTIONS)
        self.area, self.movement = Stats(), Stats()
        self.names: dict[str, list[float]] = {}   # the similarities of each name given during the second

    def add(self, probabilities, area: float, movement: float | None, name: str, similarity: float) -> None:
        self.frames += 1
        if probabilities is not None:
            for i, value in enumerate(probabilities):
                self.emotions[i] += float(value)
        self.area.add(area)
        if movement is not None:
            self.movement.add(movement)
        self.names.setdefault(name, []).append(similarity)

    @property
    def name(self) -> str:
        """The name given most often during the second."""
        return max(self.names, key=lambda n: len(self.names[n])) if self.names else "unknown"

    @property
    def similarity(self) -> float:
        scores = self.names.get(self.name, [])
        return sum(scores) / len(scores) if scores else 0.0

    @property
    def emotion(self) -> str:
        return EMOTIONS[max(range(len(EMOTIONS)), key=lambda i: self.emotions[i])] if sum(self.emotions) > 0 else ""

    @property
    def activity(self) -> str:
        mean = self.movement.mean
        return "still" if mean < STILL else "moving" if mean < MOVING else "agitated"

    @property
    def state(self) -> tuple[str, str, str]:
        return self.name, self.emotion, self.activity


def shares(totals: list[float]) -> dict:
    """The share of each emotion, in per cent."""
    whole = sum(totals)
    return {name: round(100 * value / whole, 1) if whole > 0 else 0.0 for name, value in zip(EMOTIONS, totals)}


class Episode:
    """A period where the state of a face stays the same: the seconds it gathers."""

    def __init__(self, session: str, track: int, number: int, first: Second):
        self.id = f"faceepisode-{session}-{track}-{number}"
        self.session, self.track = session, track
        self.start = first.start
        self.end = first.start
        self.frames = 0
        self.emotions = [0.0] * len(EMOTIONS)
        self.area, self.movement = Stats(), Stats()
        self.similarity = Stats()
        self.state = first.state
        self.closed_by = ""

    def add(self, second: Second) -> None:
        self.end = second.start + 1
        self.frames += second.frames
        for i, value in enumerate(second.emotions):
            self.emotions[i] += value
        self.area.merge(second.area)
        self.movement.merge(second.movement)
        self.similarity.add(second.similarity)

    def document(self, origin: dict) -> dict:
        name, _, activity = self.state
        emotion = EMOTIONS[max(range(len(EMOTIONS)), key=lambda i: self.emotions[i])] if sum(self.emotions) > 0 else ""
        return {
            **origin, "id": self.id, "date": now_iso(self.start), "title": f"{name} {emotion}".strip(),
            "session": self.session, "track": self.track, "name": name, "known": name != "unknown",
            "similarity": round(self.similarity.mean, 3), "start": now_iso(self.start), "end": now_iso(self.end),
            "seconds": round(self.end - self.start, 1), "frames": self.frames, "emotion": emotion, "emotions": shares(self.emotions),
            "area": self.area.spread(), "movement": self.movement.spread(), "activity": activity, "closedBy": self.closed_by,
        }


class Track:
    """A face followed from frame to frame."""

    def __init__(self, number: int, centre, width: float, moment: float):
        self.number = number
        self.centre, self.width, self.seen = centre, width, moment
        self.second: Second | None = None
        self.episode: Episode | None = None
        self.episodes = 0
        self.pending: list[Second] = []   # the seconds whose state differs from the one of the episode, waiting to hold


class FaceRecorder:
    """
    Follows the faces and tells what is to be written: `frame` is given the faces of each frame and returns the
    messages for the chat ({"episode": …} to write or write again, {"observation": …} to write once); `close` ends
    everything (the window closes).
    """

    def __init__(self):
        self.session = uuid.uuid4().hex[:12]
        self.tracks: dict[int, Track] = {}
        self._next_track = 1
        self._observations = 0
        self.origin = {"appName": "KAONOMI.VisionOpenCV", "machineName": socket.gethostname(), "userName": getpass.getuser()}

    # ── The frames ──

    def frame(self, moment: float, people: list[dict], width: int, height: int) -> list[dict]:
        """`people`: for each face, its box [x, y, w, h], its name, its score and its probabilities (7, or None)."""
        messages: list[dict] = []
        matched: set[int] = set()
        for person in people:
            x, y, w, h = person["box"]
            centre = (x + w / 2, y + h / 2)
            track = self._match(centre, w, matched)
            movement = None
            if track is None:
                track = Track(self._next_track, centre, w, moment)
                self._next_track += 1
                self.tracks[track.number] = track
            else:
                elapsed = moment - track.seen
                if 0 < elapsed < 0.5 and w > 0:
                    # How far the middle of the face went, in widths of the face per second, in per cent.
                    movement = 100 * math.hypot(centre[0] - track.centre[0], centre[1] - track.centre[1]) / ((w + track.width) / 2) / elapsed
                track.centre, track.width, track.seen = centre, w, moment
            matched.add(track.number)
            if track.second is None:
                track.second = Second(moment)
            elif moment - track.second.start >= 1.0:
                messages += self._second_done(track)
                track.second = Second(moment)
            area = 100.0 * (w * h) / max(1, width * height)
            track.second.add(person.get("probabilities"), area, movement, person["name"], float(person.get("score", 0)))
        # The faces not seen for a while have left.
        for number in [n for n, t in self.tracks.items() if n not in matched and moment - t.seen > LOST_SECONDS]:
            messages += self._leave(self.tracks.pop(number), "left")
        return messages

    def _match(self, centre, width: float, matched: set[int]) -> Track | None:
        """The track whose face was the nearest, when it is close enough (less than a width and a half of the face)."""
        best, best_distance = None, 1.5 * max(width, 1)
        for track in self.tracks.values():
            if track.number in matched:
                continue
            distance = math.hypot(centre[0] - track.centre[0], centre[1] - track.centre[1])
            if distance < best_distance:
                best, best_distance = track, distance
        return best

    # ── The seconds and the episodes ──

    def _second_done(self, track: Track) -> list[dict]:
        second, track.second = track.second, None
        if second is None or second.frames < MIN_FRAMES:
            return []
        messages: list[dict] = []
        if track.episode is None:
            # The face appears: its first episode, and the observation of this second.
            self._open(track, second)
            messages.append(self._observation(track, second, "appeared", ""))
        elif second.state == track.episode.state:
            # The same state: what was waiting was a blip, it belongs to the episode too.
            for waiting in track.pending:
                track.episode.add(waiting)
            track.pending = []
            track.episode.add(second)
            if track.episode.end - track.episode.start >= MAX_SECONDS:
                track.episode.closed_by = "timeout"
                messages.append({"episode": track.episode.document(self.origin)})
                track.episode = None
                return messages
        else:
            # Another state: it must hold before it ends the episode. A third state while waiting starts the wait again.
            if track.pending and track.pending[-1].state != second.state:
                for waiting in track.pending:
                    track.episode.add(waiting)
                track.pending = []
            track.pending.append(second)
            if len(track.pending) >= HOLD_SECONDS:
                former = track.episode
                first = track.pending[0]
                # What changed, the identity first, then the emotion, then the activity.
                change, previous = next(((kind, old) for kind, old, new in zip(("identity", "emotion", "activity"), former.state, first.state) if old != new),
                                        ("emotion", former.state[1]))
                former.closed_by = change
                messages.append({"episode": former.document(self.origin)})
                self._open(track, first)
                for waiting in track.pending[1:]:
                    track.episode.add(waiting)
                messages.append(self._observation(track, first, change, previous))
                track.pending = []
            else:
                return messages
        if track.episode is not None:
            messages.append({"episode": track.episode.document(self.origin)})
        return messages

    def _open(self, track: Track, second: Second) -> None:
        track.episodes += 1
        track.episode = Episode(self.session, track.number, track.episodes, second)
        track.episode.add(second)

    def _observation(self, track: Track, second: Second, change: str, previous: str) -> dict:
        self._observations += 1
        name, emotion, activity = second.state
        return {"observation": {
            **self.origin, "id": f"faceobservation-{self.session}-{self._observations}", "date": now_iso(second.start),
            "title": f"{name} {change}".strip(), "session": self.session, "track": track.number, "name": name, "known": name != "unknown",
            "similarity": round(second.similarity, 3), "frames": second.frames, "emotion": emotion, "emotions": shares(second.emotions),
            "area": second.area.spread(), "movement": second.movement.spread(), "activity": activity, "change": change, "previous": previous,
            "episode": track.episode.id if track.episode else "",
        }}

    def _leave(self, track: Track, reason: str) -> list[dict]:
        """The face left (or everything stops): its last seconds join its episode, which is closed."""
        messages: list[dict] = []
        last = track.second if track.second is not None and track.second.frames >= MIN_FRAMES else None
        if track.episode is None:
            return messages
        for waiting in track.pending + ([last] if last else []):
            track.episode.add(waiting)
        track.pending = []
        track.episode.closed_by = reason
        messages.append({"episode": track.episode.document(self.origin)})
        final = last or Second(track.seen)
        if not last:
            # Nothing summed up in its last second: the observation holds what the episode says.
            final.names = {track.episode.state[0]: [track.episode.similarity.mean]}
            final.emotions = list(track.episode.emotions)
            final.area, final.movement = track.episode.area, track.episode.movement
        messages.append(self._observation(track, final, "left" if reason == "left" else reason, track.episode.state[1]))
        track.episode = None
        return messages

    def close(self) -> list[dict]:
        messages: list[dict] = []
        for track in list(self.tracks.values()):
            messages += self._leave(track, "stopped")
        self.tracks = {}
        return messages

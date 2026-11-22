"""
The cameras of this machine, for the camera chosen in the vision windows of the chat.

Prints one JSON line: [{"index": 0, "name": "MacBook Pro Camera"}, …]. The index is the one OpenCV opens
(cv2.VideoCapture(index), the --source of the vision scripts).

On macOS the names come from the system (system_profiler), without opening any camera: OpenCV numbers the cameras
in the order of their unique identifiers, so the list is sorted the same way. Elsewhere the names are not known to
OpenCV: the indexes 0 to 5 are tried (each camera is opened for an instant) and named "Camera 1", "Camera 2"…
"""

from __future__ import annotations

import json
import subprocess
import sys


def mac_cameras() -> list[dict]:
    listed = json.loads(subprocess.run(["system_profiler", "SPCameraDataType", "-json"], capture_output=True, text=True, timeout=20).stdout or "{}")
    cameras = sorted(listed.get("SPCameraDataType", []), key=lambda camera: camera.get("spcamera_unique-id", ""))
    return [{"index": index, "name": camera.get("_name") or f"Camera {index + 1}"} for index, camera in enumerate(cameras)]


def probed_cameras(most: int = 6) -> list[dict]:
    import cv2

    backend = cv2.CAP_DSHOW if sys.platform == "win32" else cv2.CAP_ANY
    cameras = []
    for index in range(most):
        capture = cv2.VideoCapture(index, backend)
        if capture.isOpened():
            cameras.append({"index": index, "name": f"Camera {index + 1}"})
        capture.release()
    return cameras


def main() -> int:
    try:
        cameras = mac_cameras() if sys.platform == "darwin" else probed_cameras()
    except Exception as error:  # noqa: BLE001 - whatever fails, the first camera is still there to try
        print(f"The cameras could not be listed: {error}", file=sys.stderr)
        cameras = []
    print(json.dumps(cameras), flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())

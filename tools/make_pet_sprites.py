"""Generate pixel-art pet sprite sheets for the break overlay.

Each pet is described as small text pixel maps ("parts") plus per-frame placements of those
parts, so a frame is mostly "which part variant goes where". Output per pet:

    src/Time-to-stand-up.App/Assets/Pets/<pet>.png   one row per animation, 32x32 cells
    src/Time-to-stand-up.App/Assets/Pets/<pet>.json  animation table (row, frames, fps, loop)

Only the Python standard library is used. Sprites face right; the app mirrors them for left.

Usage:
    python tools/make_pet_sprites.py                 # write the assets
    python tools/make_pet_sprites.py --preview DIR   # also write enlarged contact sheets to DIR
"""

from __future__ import annotations

import argparse
import json
import struct
import zlib
from pathlib import Path

CELL = 32
ASSETS = Path(__file__).resolve().parent.parent / "src" / "Time-to-stand-up.App" / "Assets" / "Pets"
PREVIEW_BACKGROUND = (0x0F, 0x17, 0x20, 0xFF)  # the overlay's background colour


# ---------------------------------------------------------------- image helpers

def hex_rgba(value: str) -> tuple[int, int, int, int]:
    value = value.lstrip("#")
    return (int(value[0:2], 16), int(value[2:4], 16), int(value[4:6], 16), 0xFF)


class Canvas:
    def __init__(self, width: int, height: int, fill=(0, 0, 0, 0)):
        self.width = width
        self.height = height
        self.pixels = [[fill] * width for _ in range(height)]

    def put(self, x: int, y: int, colour) -> bool:
        """Returns False when the pixel falls outside the canvas (it is dropped)."""
        if 0 <= x < self.width and 0 <= y < self.height:
            self.pixels[y][x] = colour
            return True
        return False

    def blit(self, other: "Canvas", left: int, top: int) -> None:
        for y in range(other.height):
            for x in range(other.width):
                colour = other.pixels[y][x]
                if colour[3]:
                    self.put(left + x, top + y, colour)

    def scaled(self, factor: int, background) -> "Canvas":
        out = Canvas(self.width * factor, self.height * factor, background)
        for y in range(self.height):
            for x in range(self.width):
                colour = self.pixels[y][x]
                if colour[3]:
                    for dy in range(factor):
                        for dx in range(factor):
                            out.pixels[y * factor + dy][x * factor + dx] = colour
        return out

    def save_png(self, path: Path) -> None:
        raw = bytearray()
        for row in self.pixels:
            raw.append(0)  # filter: none
            for colour in row:
                raw.extend(colour)

        def chunk(tag: bytes, data: bytes) -> bytes:
            body = tag + data
            return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)

        header = struct.pack(">IIBBBBB", self.width, self.height, 8, 6, 0, 0, 0)
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(
            b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", header)
            + chunk(b"IDAT", zlib.compress(bytes(raw), 9))
            + chunk(b"IEND", b""))


# ---------------------------------------------------------------- pet model

class Pet:
    """parts: name -> rows of palette characters ('.' is transparent).
    animations: name -> (fps, loop, [frame, ...]); a frame is a list of (part, x, y) drawn in order.
    A placement may carry a fourth item "v" to draw that part upside down."""

    def __init__(self, name: str, palette: dict[str, str], parts: dict[str, list[str]], animations: dict,
                 clip_ok: tuple[str, ...] = ()):
        self.name = name
        self.palette = {key: hex_rgba(value) for key, value in palette.items()}
        self.parts = parts
        self.animations = animations
        # 這些動畫故意讓部件超出格子(例如鑽進地面),不當成錯誤。
        self.clip_ok = clip_ok
        self.clipped: dict[str, int] = {}

    def render_frame(self, frame, animation: str = "") -> Canvas:
        canvas = Canvas(CELL, CELL)
        for part, left, top, *flags in frame:
            rows = self.parts[part]
            if "v" in flags:  # 上下顛倒(例如倒立)
                rows = rows[::-1]
            for y, row in enumerate(rows):
                for x, char in enumerate(row):
                    if char != "." and not canvas.put(left + x, top + y, self.palette[char]):
                        self.clipped[animation] = self.clipped.get(animation, 0) + 1
        return canvas

    def sheet(self) -> tuple[Canvas, dict]:
        columns = max(len(frames) for _, _, frames in self.animations.values())
        sheet = Canvas(columns * CELL, len(self.animations) * CELL)
        table = {}
        for row, (name, (fps, loop, frames)) in enumerate(self.animations.items()):
            for column, frame in enumerate(frames):
                sheet.blit(self.render_frame(frame, name), column * CELL, row * CELL)
            table[name] = {"row": row, "frames": len(frames), "fps": fps, "loop": loop}
        return sheet, {"cell": CELL, "animations": table}


# ---------------------------------------------------------------- four-legged pets

class Quadruped:
    """Poses shared by four-legged pets (cat, shiba). A pet supplies parts named head / headBlink /
    body / leg / legFar / sitBody / loaf / loafTall / stretchBody / pawForward plus its own tails;
    this class only decides where they go. Feet rest on the last row; legs are drawn first so the
    body hides their tops."""

    BACK_FAR, BACK_NEAR, FRONT_FAR, FRONT_NEAR = 8, 10, 15, 17
    LEG_TOP = 27

    # A leg's (dx, lift) through one stride: three stance frames moving back, three swing frames.
    STRIDE = [(1, 0), (0, 0), (-1, 0), (-1, -1), (0, -1), (1, -1)]

    def __init__(self, tail_positions, default_tail_position, sit_tail_positions, stretch_tail_position):
        self.tail_positions = tail_positions
        self.default_tail_position = default_tail_position
        self.sit_tail_positions = sit_tail_positions
        self.stretch_tail_position = stretch_tail_position

    def stand(self, body_dy=0, legs=((0, 0),) * 4, tail="tailUp", head="head", head_dx=0, head_dy=0):
        tail_x, tail_y = self.tail_positions.get(tail, self.default_tail_position)
        (bf, bn, ff, fn) = legs
        return [
            (tail, tail_x, tail_y + body_dy),
            ("legFar", self.BACK_FAR + bf[0], self.LEG_TOP + bf[1]),
            ("legFar", self.FRONT_FAR + ff[0], self.LEG_TOP + ff[1]),
            ("body", 7, 20 + body_dy),
            ("leg", self.BACK_NEAR + bn[0], self.LEG_TOP + bn[1]),
            ("leg", self.FRONT_NEAR + fn[0], self.LEG_TOP + fn[1]),
            (head, 15 + head_dx, 13 + body_dy + head_dy),
        ]

    def sit(self, head="head", head_dy=0, tail="tailSit"):
        tail_x, tail_y = self.sit_tail_positions[tail]
        return [
            (tail, tail_x, tail_y),
            ("sitBody", 11, 22),
            (head, 10, 15 + head_dy),
        ]

    def sleep(self, tall: bool, z=None):
        frame = [("loafTall", 5, 25)] if tall else [("loaf", 5, 26)]
        frame.append(("headBlink", 15, 21 if tall else 22))
        if z:
            frame.append(z)
        return frame

    def stretch(self, head_dy=0, tail="tailUp", head="headBlink"):
        tail_x, tail_y = self.stretch_tail_position
        return [
            (tail, tail_x, tail_y),
            ("legFar", 8, 27),
            ("pawForward", 21, 29),
            ("stretchBody", 6, 22),
            ("leg", 10, 27),
            (head, 19, 19 + head_dy),
        ]

    def walk_frame(self, i: int, tail: str, head="head"):
        a = self.STRIDE[i % 6]        # back-near + front-far
        b = self.STRIDE[(i + 3) % 6]  # back-far + front-near
        return self.stand(body_dy=-1 if i % 3 == 1 else 0, legs=(b, a, a, b), tail=tail, head=head)

    def run_frames(self, tail: str, head="head"):
        return [
            self.stand(body_dy=-1, legs=((-2, -1), (-2, -1), (2, -1), (2, -1)), tail=tail, head=head),
            self.stand(body_dy=0, legs=((-1, 0), (-1, 0), (1, 0), (1, 0)), tail=tail, head=head),
            self.stand(body_dy=0, legs=((2, 0), (2, 0), (-2, 0), (-2, 0)), tail=tail, head=head),
            self.stand(body_dy=-1, legs=((1, -1), (1, -1), (-1, -1), (-1, -1)), tail=tail, head=head),
        ]

    def jump_poses(self, crouch_tail: str, up_tail: str, down_tail: str):
        crouch = self.stand(body_dy=1, legs=((0, 0),) * 4, tail=crouch_tail)
        air_up = self.stand(body_dy=-1, legs=((-2, -1), (-2, -1), (2, -2), (2, -2)), tail=up_tail)
        air_down = self.stand(body_dy=-1, legs=((-1, -1), (-1, -1), (1, 0), (1, 0)), tail=down_tail)
        return crouch, air_up, air_down


# ---------------------------------------------------------------- cat

def make_cat() -> Pet:
    palette = {
        "o": "#3A2A2A",  # outline
        "b": "#F4A259",  # fur
        "d": "#D17F3A",  # darker fur (stripes, far legs)
        "l": "#FCE9CF",  # light belly / muzzle
        "e": "#2A1E1E",  # eye
        "p": "#F58FA5",  # nose, inner ear, tongue
        "z": "#CFE3F5",  # sleeping "z"
    }

    head_rows = [
        ".oo.....oo.",
        "obbo...obbo",
        "obpbooobpbo",
        "obbbdbdbbbo",
        "{brow}",
        "{eyes}",
        "obblllllbbo",
        "{mouth}",
        ".obbbbbbbo.",
        "..ooooooo..",
    ]

    def head(eyes: str, mouth: str) -> list[str]:
        brow = "obbebbbebbo" if eyes == eyes_open else "obbbbbbbbbo"
        return [row.format(brow=brow, eyes=eyes, mouth=mouth) for row in head_rows]

    eyes_open = "obbelllebbo"
    eyes_shut = "oboollloobo"
    mouth_shut = "obbllpllbbo"

    parts = {
        "head": head(eyes_open, mouth_shut),
        "headBlink": head(eyes_shut, mouth_shut),
        "headYawnSmall": head(eyes_shut, "obbloooLbbo".replace("L", "l")),
        "headYawnBig": [
            ".oo.....oo.",
            "obbo...obbo",
            "obpbooobpbo",
            "obbbdbdbbbo",
            "obbbbbbbbbo",
            eyes_shut,
            "obbloooLbbo".replace("L", "l"),
            "obbopppobbo",
            ".obopppobo.",
            "..ooooooo..",
        ],
        "body": [
            "..oooooooooo..",
            ".obbdbbdbbdbo.",
            "obbbdbbdbbdbbo",
            "obbbbbbbbbbbbo",
            "obbbbbbbbbbbbo",
            "obbllllllllbbo",
            ".obllllllllbo.",
            "..oooooooooo..",
        ],
        "leg": ["obo", "obo", "obo", "obo", "ooo"],
        "legFar": ["odo", "odo", "odo", "odo", "ooo"],
        "tailUp": [
            "..oo",
            ".obo",
            ".obo",
            "obo.",
            "obo.",
            "obo.",
        ],
        "tailCurl": [
            "oo..",
            "obo.",
            "obo.",
            ".obo",
            ".obo",
            ".obo",
        ],
        "tailBack": [
            "oooooo",
            "obbbbb",
            "oooooo",
        ],
        "sitBody": [
            "..ooooo..",
            ".obbdbbo.",
            "obbbdbbbo",
            "obbbbbbbo",
            "obblllbbo",
            "obblllbbo",
            "obblllbbo",
            "obblllbbo",
            "oblololbo",
            "ooooooooo",
        ],
        "tailSit": [
            ".oooo",
            "obbbb",
            "ooooo",
        ],
        "tailSitUp": [
            "oo...",
            "obo..",
            ".oboo",
            ".obbb",
            ".oooo",
        ],
        "loaf": [
            "...ooooooooooo....",
            "..obbdbbdbbdbbo...",
            ".obbbdbbdbbdbbbo..",
            "obbbbbbbbbbbbbbbo.",
            "obbbbbbbbbbbbbbbbo",
            "oooooooooooooooooo",
        ],
        "loafTall": [
            "...ooooooooooo....",
            "..obbdbbdbbdbbo...",
            ".obbbdbbdbbdbbbo..",
            "obbbbdbbdbbdbbbbo.",
            "obbbbbbbbbbbbbbbo.",
            "obbbbbbbbbbbbbbbbo",
            "oooooooooooooooooo",
        ],
        "z": ["zzzz", "..z.", ".z..", "zzzz"],
        "zBig": ["zzzzz", "...z.", "..z..", ".z...", "zzzzz"],
        "stretchBody": [
            "..oooo..........",
            ".obbdboooo......",
            "obbbdbbbdboooo..",
            "obbbbbbbdbbbdboo",
            ".oobbbbbbbbbdbbo",
            "...ooollllbbbbbo",
            "......oooollllbo",
            "..........ooooo.",
        ],
        "pawForward": [
            "ooooooo.",
            "obbbbblo",
            "oooooooo",
        ],
    }

    q = Quadruped(
        tail_positions={"tailBack": (2, 21)},
        default_tail_position=(4, 15),
        sit_tail_positions={"tailSit": (7, 29), "tailSitUp": (7, 27)},
        stretch_tail_position=(4, 17))
    stand, sit, sleep, stretch = q.stand, q.sit, q.sleep, q.stretch

    def walk_frame(i: int):
        return q.walk_frame(i, "tailUp" if i < 3 else "tailCurl")

    run = q.run_frames("tailBack")
    crouch, air_up, air_down = q.jump_poses("tailCurl", "tailBack", "tailUp")

    animations = {
        "idle": (3, True, [
            stand(),
            stand(tail="tailCurl"),
            stand(head="headBlink", tail="tailCurl"),
            stand(),
        ]),
        "walk": (8, True, [walk_frame(i) for i in range(6)]),
        "run": (12, True, run),
        "jump": (8, False, [crouch, air_up, air_down, crouch]),
        "sit": (2, True, [
            sit(),
            sit(tail="tailSitUp"),
            sit(head="headBlink", tail="tailSitUp"),
            sit(),
        ]),
        "sleep": (1.5, True, [
            sleep(False),
            sleep(True, ("z", 24, 16)),
            sleep(True, ("zBig", 26, 10)),
            sleep(False, ("zBig", 27, 6)),
        ]),
        # 特殊動作 1:貓式伸展(前腳往前、屁股翹高)
        "stretch": (3, False, [
            stand(head="headBlink"),
            stretch(),
            stretch(head_dy=1, tail="tailCurl"),
            stretch(),
            stretch(head_dy=1, tail="tailCurl"),
            stand(),
        ]),
        # 特殊動作 2:坐著打哈欠
        "yawn": (3, False, [
            sit(),
            sit(head="headYawnSmall"),
            sit(head="headYawnBig"),
            sit(head="headYawnBig"),
            sit(head="headYawnSmall"),
            sit(head="headBlink"),
        ]),
    }

    return Pet("cat", palette, parts, animations)


# ---------------------------------------------------------------- shiba

def make_shiba() -> Pet:
    palette = {
        "o": "#3A2A2A",  # outline
        "b": "#EDA85F",  # cream-orange fur
        "d": "#C8843F",  # darker fur (far legs)
        "l": "#FFF4E0",  # white cheeks, chest, belly, socks
        "e": "#2A1E1E",  # eye, nose
        "p": "#F58FA5",  # tongue
        "z": "#CFE3F5",  # sleeping "z"
    }

    def head(open_eyes: bool, tongue: bool) -> list[str]:
        return [
            ".oo.....oo.",
            "obbo...obbo",
            "obbbooobbbo",
            "obbbbbbbbbo",
            "obbebbbebbo" if open_eyes else "obbbbbbbbbo",
            "oblelllelbo" if open_eyes else "oboollloobo",
            "ollllellllo",
            "olllooolllo",
            ".olllplllo." if tongue else ".olllllllo.",
            "..ooooooo..",
        ]

    parts = {
        "head": head(True, False),
        "headBlink": head(False, False),
        "headPant": head(True, True),
        "body": [
            "..oooooooooo..",
            ".obbbbbbbbbbo.",
            "obbbbbbbbbbbbo",
            "obbbbbbbbbbbbo",
            "obbbbbbbbbbllo",
            "obbllllllllllo",
            ".ollllllllllo.",
            "..oooooooooo..",
        ],
        "leg": ["obo", "obo", "olo", "olo", "ooo"],
        "legFar": ["odo", "odo", "odo", "odo", "ooo"],
        # 捲在背上的尾巴,兩個變體交替就是搖尾巴。
        "tailA": [
            ".ooo.",
            "obblo",
            "obooo",
            "obbo.",
            ".oo..",
        ],
        "tailB": [
            "..ooo",
            ".obbo",
            "obboo",
            "obbo.",
            ".oo..",
        ],
        "sitBody": [
            "..ooooo..",
            ".obbbbbo.",
            "obbbbbbbo",
            "obblllbbo",
            "obblllbbo",
            "obblllbbo",
            "obblllbbo",
            "obblllbbo",
            "ollolollo",
            "ooooooooo",
        ],
        "loaf": [
            "...ooooooooooo....",
            "..obbbbbbbbbbbo...",
            ".obbbbbbbbbbbbbo..",
            "obbbbbbbbbbbbbbbo.",
            "obbllllllllllbbbbo",
            "oooooooooooooooooo",
        ],
        "loafTall": [
            "...ooooooooooo....",
            "..obbbbbbbbbbbo...",
            ".obbbbbbbbbbbbbo..",
            "obbbbbbbbbbbbbbbo.",
            "obbbbbbbbbbbbbbbo.",
            "obbllllllllllbbbbo",
            "oooooooooooooooooo",
        ],
        "z": ["zzzz", "..z.", ".z..", "zzzz"],
        "zBig": ["zzzzz", "...z.", "..z..", ".z...", "zzzzz"],
        "stretchBody": [
            "..oooo..........",
            ".obbbboooo......",
            "obbbbbbbbboooo..",
            "obbbbbbbbbbbbboo",
            ".oobbbbbbbbbbbbo",
            "...ooollllbbbbbo",
            "......oooollllbo",
            "..........ooooo.",
        ],
        "pawForward": [
            "ooooooo.",
            "obbblllo",
            "oooooooo",
        ],
    }

    q = Quadruped(
        tail_positions={},
        default_tail_position=(6, 16),
        sit_tail_positions={"tailA": (7, 26), "tailB": (7, 26)},
        stretch_tail_position=(5, 18))

    def wag(i: int) -> str:
        return "tailA" if i % 2 == 0 else "tailB"

    crouch, air_up, air_down = q.jump_poses("tailA", "tailB", "tailA")
    run_panting = q.run_frames("tailA", head="headPant")

    animations = {
        "idle": (4, True, [
            q.stand(tail="tailA"),
            q.stand(tail="tailB"),
            q.stand(tail="tailA", head="headBlink"),
            q.stand(tail="tailB"),
        ]),
        "walk": (8, True, [q.walk_frame(i, wag(i // 2)) for i in range(6)]),
        "run": (12, True, q.run_frames("tailA")),
        "jump": (8, False, [crouch, air_up, air_down, crouch]),
        "sit": (3, True, [
            q.sit(tail="tailA"),
            q.sit(tail="tailB"),
            q.sit(tail="tailA", head="headBlink"),
            q.sit(tail="tailB", head="headPant"),
        ]),
        "sleep": (1.5, True, [
            q.sleep(False),
            q.sleep(True, ("z", 24, 16)),
            q.sleep(True, ("zBig", 26, 10)),
            q.sleep(False, ("zBig", 27, 6)),
        ]),
        # 特殊動作 1:原地跑(吐舌頭喘氣)
        "run_in_place": (10, False, run_panting + run_panting + run_panting),
        # 特殊動作 2:前撲伸展(邀玩的姿勢:前腳趴低、屁股翹高、搖尾巴)
        "play_bow": (3, False, [
            q.stand(tail="tailA", head="headPant"),
            q.stretch(tail="tailA", head="headPant"),
            q.stretch(head_dy=1, tail="tailB", head="headPant"),
            q.stretch(tail="tailA", head="headPant"),
            q.stretch(head_dy=1, tail="tailB", head="headPant"),
            q.stand(tail="tailB"),
        ]),
    }

    return Pet("shiba", palette, parts, animations)


# ---------------------------------------------------------------- slime

def blob_rows(width: int, height: int) -> list[str]:
    """A gumdrop: wide dome on top, shallow rounded bottom. Outline 'o', body 'b',
    a highlight 'w' near the top-left and a darker band 'd' along the bottom."""
    centre_y = height * 0.62
    inside = [[False] * width for _ in range(height)]
    for y in range(height):
        radius_y = centre_y if y + 0.5 <= centre_y else height - centre_y
        for x in range(width):
            dx = (x + 0.5 - width / 2) / (width / 2)
            dy = (y + 0.5 - centre_y) / radius_y
            inside[y][x] = dx * dx + dy * dy <= 1.0

    def is_inside(x: int, y: int) -> bool:
        return 0 <= x < width and 0 <= y < height and inside[y][x]

    rows = []
    highlight_x, highlight_y = int(width * 0.26), max(1, int(height * 0.22))
    for y in range(height):
        row = ""
        for x in range(width):
            if not inside[y][x]:
                row += "."
            elif not all(is_inside(x + ox, y + oy) for ox, oy in ((1, 0), (-1, 0), (0, 1), (0, -1))):
                row += "o"
            elif y == highlight_y and x in (highlight_x, highlight_x + 1):
                row += "w"
            elif y >= height - 3:
                row += "d"
            else:
                row += "b"
        rows.append(row)
    return rows


def make_slime() -> Pet:
    palette = {
        "o": "#3A2A2A",  # outline
        "b": "#7FD4C1",  # jelly
        "d": "#55AE9C",  # darker bottom
        "w": "#EFFFFA",  # highlight
        "e": "#2A1E1E",  # eye
        "z": "#CFE3F5",  # sleeping "z"
    }

    parts = {
        "eyeOpen": ["e", "e"],
        "eyeShut": ["ee"],
        "eyeHappy": [".e.", "e.e"],
        "mouth": ["oo"],
        "z": ["zzzz", "..z.", ".z..", "zzzz"],
        "zBig": ["zzzzz", "...z.", "..z..", ".z...", "zzzzz"],
    }

    def slime(width: int, height: int, lift: int = 0, eyes: str = "open", extra=None):
        """One frame: a blob of the given size whose bottom sits `lift` pixels above the ground."""
        name = f"blob{width}x{height}"
        if name not in parts:
            parts[name] = blob_rows(width, height)

        top = CELL - lift - height
        left = CELL // 2 - width // 2
        wide = width >= 18  # 窄的身體要把兩眼靠近,才不會壓到外框
        face_x = CELL // 2 + (2 if wide else 1)   # 臉偏右,表示面向右邊
        near, far = (face_x - 3, face_x + 2) if wide else (face_x - 2, face_x + 1)
        eye_y = top + max(2, round(height * 0.42))

        frame = [(name, left, top)]
        if eyes == "open":
            frame += [("eyeOpen", near, eye_y), ("eyeOpen", far, eye_y)]
        elif eyes == "shut":
            frame += [("eyeShut", near - 1, eye_y + 1), ("eyeShut", far, eye_y + 1)]
        else:
            frame += [("eyeHappy", near - 1, eye_y), ("eyeHappy", far, eye_y)]
        if height >= 12 and eyes != "shut":
            frame.append(("mouth", face_x - 1, eye_y + 3))
        if extra:
            frame.append(extra)
        return frame

    animations = {
        "idle": (4, True, [
            slime(18, 13),
            slime(19, 12),
            slime(18, 13, eyes="shut"),
            slime(17, 14),
        ]),
        # 史萊姆沒有腳,走路就是連續的小跳。
        "walk": (9, True, [
            slime(20, 11),
            slime(16, 15, lift=1),
            slime(17, 14, lift=4),
            slime(17, 14, lift=5),
            slime(16, 15, lift=2),
            slime(20, 11),
        ]),
        "run": (12, True, [
            slime(21, 10),
            slime(15, 16, lift=4),
            slime(16, 15, lift=8),
            slime(16, 15, lift=3),
        ]),
        "jump": (8, False, [
            slime(22, 9),
            slime(14, 17, eyes="happy"),
            slime(16, 15),
            slime(22, 9),
        ]),
        "sit": (2, True, [
            slime(20, 11),
            slime(21, 10),
            slime(20, 11, eyes="shut"),
            slime(21, 10),
        ]),
        "sleep": (1.5, True, [
            slime(24, 7, eyes="shut"),
            slime(24, 8, eyes="shut", extra=("z", 24, 15)),
            slime(24, 8, eyes="shut", extra=("zBig", 26, 9)),
            slime(24, 7, eyes="shut", extra=("zBig", 27, 5)),
        ]),
        # 特殊動作 1:連跳三下,一次比一次高
        "multi_hop": (8, False, [
            slime(21, 10),
            slime(16, 15, lift=6),
            slime(21, 10),
            slime(16, 15, lift=9, eyes="happy"),
            slime(21, 10),
            slime(15, 16, lift=12, eyes="happy"),
            slime(16, 15, lift=5, eyes="happy"),
            slime(22, 9),
        ]),
        # 特殊動作 2:壓到最扁再拉到最長
        "squish": (6, False, [
            slime(18, 13),
            slime(22, 9),
            slime(26, 6, eyes="shut"),
            slime(26, 6, eyes="shut"),
            slime(12, 19, eyes="happy"),
            slime(12, 19, eyes="happy"),
            slime(20, 11),
            slime(17, 14),
        ]),
    }

    return Pet("slime", palette, parts, animations)


# ---------------------------------------------------------------- sand sprite

def make_sand_sprite() -> Pet:
    palette = {
        "o": "#3A2A2A",  # outline
        "b": "#E8C068",  # sand (same gold as the hourglass)
        "d": "#C79A45",  # darker grains
        "l": "#F8E3A6",  # highlight
        "e": "#2A1E1E",  # eye
        "z": "#CFE3F5",  # sleeping "z"
    }

    def head(open_eyes: bool) -> list[str]:
        return [
            "...ooo...",
            "..obbbo..",
            ".obblbbo.",
            "obbbbbbbo",
            "obbebbebo" if open_eyes else "obbbbbbbo",
            "obbebbebo" if open_eyes else "obbobbobo",
            "obdbbbbbo",
            ".obbbbbo.",
            "..ooooo..",
        ]

    parts = {
        "head": head(True),
        "headBlink": head(False),
        "body": [
            ".ooooo.",
            "obbbbbo",
            "obdbbbo",
            "obbbdbo",
            "obbbbbo",
            "obbbbbo",
            ".ooooo.",
        ],
        "armL": ["ob", "ob", "ob", "oo"],
        "armR": ["bo", "bo", "bo", "oo"],
        # 沙做的手臂可以拉長:舉手與倒立時用。手掌(收口)在最後一列。
        "armLongL": ["ob"] * 10 + ["oo"],
        "armLongR": ["bo"] * 10 + ["oo"],
        "armOutL": ["oooo", "obbb", "oooo"],
        "armOutR": ["oooo", "bbbo", "oooo"],
        "leg": ["obo", "obo", "obo", "ooo"],
        "legForward": ["oooo", "bbbo", "oooo"],
        "moundSmall": [
            "...oooo...",
            ".oobbbboo.",
            "obbbdbbbbo",
            "oooooooooo",
        ],
        "mound": [
            "......oooo......",
            "....oobbbboo....",
            "...obbbbdbbbo...",
            "..obbdbbbbbbbo..",
            ".obbbbbbbbdbbbo.",
            "obbbbbbbbbbbbbbo",
            "oooooooooooooooo",
        ],
        "moundTall": [
            "......oooo......",
            "....oobbbboo....",
            "...obbbbdbbbo...",
            "..obbdbbbbbbbo..",
            ".obbbbbbbbbbbbo.",
            ".obbbbbbbbdbbbo.",
            "obbbbbbbbbbbbbbo",
            "oooooooooooooooo",
        ],
        "napEyes": ["oo..oo"],
        "grain": ["b"],
        "z": ["zzzz", "..z.", ".z..", "zzzz"],
        "zBig": ["zzzzz", "...z.", "..z..", ".z...", "zzzzz"],
    }

    stride = [(1, 0), (0, 0), (-1, 0), (-1, -1), (0, -1), (1, -1)]

    def stand(body_dy=0, legs=((0, 0), (0, 0)), arms="down", head="head", head_dx=0, sink=0):
        """Standing figure; `sink` pushes the whole figure into the ground (burrowing)."""
        y = body_dy + sink
        (left_leg, right_leg) = legs
        frame = []
        if arms == "down":
            frame += [("armL", 10, 23 + y), ("armR", 19, 23 + y)]
        elif arms == "swingA":
            frame += [("armL", 10, 24 + y), ("armR", 19, 22 + y)]
        elif arms == "swingB":
            frame += [("armL", 10, 22 + y), ("armR", 19, 24 + y)]
        elif arms == "up":
            frame += [("armLongL", 10, 13 + y, "v"), ("armLongR", 19, 13 + y, "v")]
        elif arms == "out":
            frame += [("armOutL", 8, 23 + y), ("armOutR", 19, 23 + y)]
        frame += [
            ("leg", 12 + left_leg[0], 28 + left_leg[1] + sink),
            ("leg", 16 + right_leg[0], 28 + right_leg[1] + sink),
            ("body", 12, 22 + y),
            (head, 11 + head_dx, 15 + y),
        ]
        return frame

    def sit(head="head"):
        return [
            ("armL", 10, 26),
            ("armR", 19, 26),
            ("body", 12, 25),
            ("legForward", 17, 29),
            (head, 11, 18),
        ]

    def inverted(legs=(0, 0)):
        """Handstand: palms on the ground, head down between the arms, legs in the air."""
        return [
            ("armLongL", 10, 21),
            ("armLongR", 19, 21),
            ("leg", 12 + legs[0], 12, "v"),
            ("leg", 16 + legs[1], 12, "v"),
            ("body", 12, 15, "v"),
            ("headBlink", 11, 20, "v"),
        ]

    def nap(tall: bool, z=None):
        frame = [("moundTall", 8, 24), ("napEyes", 13, 28)] if tall else [("mound", 8, 25), ("napEyes", 13, 28)]
        if z:
            frame.append(z)
        return frame

    def walk_frame(i: int):
        return stand(
            body_dy=-1 if i % 3 == 1 else 0,
            legs=(stride[i % 6], stride[(i + 3) % 6]),
            arms="swingA" if i < 3 else "swingB")

    crouch = stand(body_dy=1)
    animations = {
        "idle": (3, True, [
            stand(),
            stand(body_dy=1),
            stand(head="headBlink"),
            stand(body_dy=1),
        ]),
        "walk": (8, True, [walk_frame(i) for i in range(6)]),
        "run": (12, True, [
            stand(body_dy=-1, legs=((-2, -1), (2, -1)), arms="swingA", head_dx=1),
            stand(legs=((-1, 0), (1, 0)), arms="swingA", head_dx=1),
            stand(legs=((2, 0), (-2, 0)), arms="swingB", head_dx=1),
            stand(body_dy=-1, legs=((1, -1), (-1, -1)), arms="swingB", head_dx=1),
        ]),
        "jump": (8, False, [
            crouch,
            stand(body_dy=-1, legs=((0, -1), (0, -1)), arms="up"),
            stand(body_dy=-1, legs=((-1, -1), (1, -1)), arms="out"),
            crouch,
        ]),
        "sit": (2, True, [
            sit(),
            sit(),
            sit(head="headBlink"),
            sit(),
        ]),
        "sleep": (1.5, True, [
            nap(False),
            nap(True, ("z", 22, 17)),
            nap(True, ("zBig", 24, 11)),
            nap(False, ("zBig", 25, 7)),
        ]),
        # 特殊動作 1:倒立(舉手 → 倒立晃腳 → 站回來)
        "handstand": (4, False, [
            stand(),
            stand(arms="up"),
            inverted((0, 0)),
            inverted((-1, 1)),
            inverted((0, 0)),
            inverted((1, -1)),
            stand(arms="up"),
            stand(),
        ]),
        # 特殊動作 2:沉進沙堆被埋住,再鑽出來
        "burrow": (4, False, [
            stand(),
            stand(sink=3) + [("moundSmall", 11, 28)],
            stand(sink=8) + [("mound", 8, 25)],
            [("mound", 8, 25), ("grain", 6, 24), ("grain", 24, 23)],
            [("moundTall", 8, 24), ("grain", 5, 21), ("grain", 26, 20)],
            stand(sink=8, head="headBlink") + [("mound", 8, 25)],
            stand(sink=3) + [("moundSmall", 11, 28)],
            stand(arms="up"),
        ]),
    }

    # burrow 故意讓身體沉到格子下緣以外。
    return Pet("sandsprite", palette, parts, animations, clip_ok=("burrow",))


PETS = [make_cat, make_shiba, make_slime, make_sand_sprite]


# ---------------------------------------------------------------- main

def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--preview", type=Path, help="also write enlarged contact sheets into this folder")
    parser.add_argument("--scale", type=int, default=6, help="preview enlargement factor")
    args = parser.parse_args()

    for factory in PETS:
        pet = factory()
        sheet, table = pet.sheet()
        sheet.save_png(ASSETS / f"{pet.name}.png")
        (ASSETS / f"{pet.name}.json").write_text(json.dumps(table, indent=2) + "\n", encoding="utf-8")
        print(f"{pet.name}: {sheet.width}x{sheet.height}, {len(table['animations'])} animations")
        for animation, count in pet.clipped.items():
            if animation not in pet.clip_ok:
                print(f"  WARNING: {pet.name}/{animation} has {count} pixel(s) outside the {CELL}x{CELL} cell")

        if args.preview:
            sheet.scaled(args.scale, PREVIEW_BACKGROUND).save_png(args.preview / f"{pet.name}-preview.png")


if __name__ == "__main__":
    main()

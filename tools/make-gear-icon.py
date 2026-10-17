"""ギアメニューのアイコン（32x32 PNG）を書き出す。傾いたヘイローを主役に、下端に頭頂部を添える。

使い方: python tools/make-gear-icon.py [拡大プレビューの出力先]
"""
import math
import sys
from pathlib import Path

from PIL import Image

SIZE = 32
SCALE = 8
OUT = Path(__file__).resolve().parent.parent / "source" / "COM3D2.HaloFollow.Plugin" / "Resources" / "gear_icon.png"

# 頭: 下端に頭頂部だけ見せるトゥーン調の球（2 段階の陰影と縁取り）
HEAD_TOP_Y = 25.0
HEAD_RADIUS = 16.0
HEAD_CENTER = (16.0, HEAD_TOP_Y + HEAD_RADIUS)
HEAD_OUTLINE = 1.5
LIGHT_DIR = (-0.8, -0.5, 0.35)
SHADE_THRESHOLD = -0.05
HEAD_LIGHT = (214, 236, 255)
HEAD_SHADE = (140, 178, 228)
HEAD_OUTLINE_COLOR = (40, 52, 84)

# ヘイロー: アイコンの主役。中央に大きく置いた傾いた輪
RING_CENTER = (16.0, 14.0)
RING_RADIUS_X = 12.5
RING_RADIUS_Y = 5.2
RING_TILT_DEG = -12.0
RING_WIDTH = 3.0
RING_OUTLINE = 1.2
# 手前側の帯は RING_FRONT（外寄り半分は RING_HIGHLIGHT）、奥側の帯は RING_BACK
RING_FRONT = (255, 222, 110)
RING_BACK = (226, 170, 60)
RING_HIGHLIGHT = (255, 248, 210)
RING_OUTLINE_COLOR = (92, 62, 22)

# 輪のきらめき（4 方向に伸びる星）。中心・腕の長さ・腕の太さ
SPARKLES = [((6.0, 4.5), 3.6, 0.9), ((27.5, 22.5), 2.8, 0.8)]
SPARKLE_COLOR = (255, 236, 150)
SPARKLE_OUTLINE = 0.8
SPARKLE_OUTLINE_COLOR = RING_OUTLINE_COLOR


def normalize(v):
    length = math.sqrt(sum(c * c for c in v))
    return tuple(c / length for c in v)


LIGHT = normalize(LIGHT_DIR)
COS_T = math.cos(math.radians(RING_TILT_DEG))
SIN_T = math.sin(math.radians(RING_TILT_DEG))


def head(x, y):
    dx = (x - HEAD_CENTER[0]) / HEAD_RADIUS
    dy = (y - HEAD_CENTER[1]) / HEAD_RADIUS
    d2 = dx * dx + dy * dy
    if d2 > 1.0:
        return None
    if math.sqrt(d2) * HEAD_RADIUS > HEAD_RADIUS - HEAD_OUTLINE:
        return HEAD_OUTLINE_COLOR
    nz = math.sqrt(1.0 - d2)
    ndotl = dx * LIGHT[0] + dy * LIGHT[1] + nz * LIGHT[2]
    return HEAD_LIGHT if ndotl > SHADE_THRESHOLD else HEAD_SHADE


def ring(x, y):
    px = x - RING_CENTER[0]
    py = y - RING_CENTER[1]
    # 輪の傾きを打ち消した座標で、楕円からの距離（px 換算の近似）を測る
    lx = px * COS_T + py * SIN_T
    ly = -px * SIN_T + py * COS_T
    r = math.hypot(lx / RING_RADIUS_X, ly / RING_RADIUS_Y)
    scale = math.hypot(lx, ly) / r if r > 1e-6 else 0.0
    distance = abs(r - 1.0) * scale
    if distance <= RING_WIDTH / 2:
        # 手前側（下半分）を明るく、奥側を暗くして輪の奥行きを出す
        if ly < 0:
            return RING_BACK
        # 手前側を一色にすると、輪が平板に見えて金属らしい艶が出ない
        return RING_HIGHLIGHT if r > 1.0 and lx < 0 else RING_FRONT
    if distance <= RING_WIDTH / 2 + RING_OUTLINE:
        return RING_OUTLINE_COLOR
    return None


def in_cross(dx, dy, arm, width):
    """中心からの距離 (dx, dy) が、中心で太さ width、腕の長さ arm で先が細る十字の内側か"""
    return dy <= width * max(0.0, 1.0 - dx / arm) or dx <= width * max(0.0, 1.0 - dy / arm)


def sparkle(x, y):
    for (cx, cy), arm, width in SPARKLES:
        dx = abs(x - cx)
        dy = abs(y - cy)
        if in_cross(dx, dy, arm, width):
            return SPARKLE_COLOR
        if in_cross(dx, dy, arm + SPARKLE_OUTLINE, width + SPARKLE_OUTLINE):
            return SPARKLE_OUTLINE_COLOR
    return None


def shade(x, y):
    """(x, y) は 32px 座標系。何も無ければ None。手前から sparkle → ring → head の順に重ねる"""
    return sparkle(x, y) or ring(x, y) or head(x, y)


def main():
    big = SIZE * SCALE
    image = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    pixels = image.load()
    for py in range(big):
        for px in range(big):
            color = shade((px + 0.5) / SCALE, (py + 0.5) / SCALE)
            if color is not None:
                pixels[px, py] = color + (255,)
    # NEAREST で縮小すると縁がギザギザになるため、平均を取る BOX で縮める
    icon = image.resize((SIZE, SIZE), Image.BOX)
    OUT.parent.mkdir(parents=True, exist_ok=True)
    icon.save(OUT)
    if len(sys.argv) > 1:
        icon.resize((SIZE * 8, SIZE * 8), Image.NEAREST).save(sys.argv[1])
    print(OUT)


if __name__ == "__main__":
    main()

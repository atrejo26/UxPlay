#!/usr/bin/env python3
"""Generate windows/tray/uxplay.ico (a phone casting to a screen).

Run from the repository root:  python3 windows/tools/make_icon.py
Requires Pillow.
"""
from PIL import Image, ImageDraw

SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
BLUE = (0, 122, 255, 255)
WHITE = (255, 255, 255, 255)


def draw(size):
    s = 4 * size  # draw large, then downsample for smooth edges
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([0, 0, s - 1, s - 1], radius=s * 0.22, fill=BLUE)
    # monitor
    w = s * 0.055
    d.rounded_rectangle([s * 0.14, s * 0.20, s * 0.86, s * 0.66],
                        radius=s * 0.05, outline=WHITE, width=round(w))
    d.rectangle([s * 0.44, s * 0.66, s * 0.56, s * 0.76], fill=WHITE)
    d.rounded_rectangle([s * 0.32, s * 0.75, s * 0.68, s * 0.81],
                        radius=s * 0.03, fill=WHITE)
    # phone inside the monitor
    d.rounded_rectangle([s * 0.41, s * 0.27, s * 0.59, s * 0.59],
                        radius=s * 0.035, fill=WHITE)
    d.rounded_rectangle([s * 0.435, s * 0.31, s * 0.565, s * 0.55],
                        radius=s * 0.015, fill=BLUE)
    return img.resize((size, size), Image.LANCZOS)


if __name__ == "__main__":
    big = draw(256)
    big.save("windows/tray/uxplay.ico", sizes=[(n, n) for n in SIZES],
             append_images=[draw(n) for n in SIZES[:-1]])
    print("wrote windows/tray/uxplay.ico")

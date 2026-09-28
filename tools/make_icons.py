# Renders the FlowBoard app icon (a white feather on a gradient tile) and the .flowboard document icon.
# Usage: pip install pillow numpy && python tools/make_icons.py src/FlowBoard/Assets
import os
import sys

import numpy as np
from PIL import Image, ImageFilter

N = 1024
yy, xx = np.mgrid[0:N, 0:N].astype(np.float32) + 0.5


def hexc(h):
    return np.array([int(h[i:i + 2], 16) for i in (1, 3, 5)], np.float32)


def cov(d):
    """Signed distance (px, negative inside) -> antialiased coverage."""
    return np.clip(0.5 - d, 0, 1)


def squircle(X, Y, x0, y0, x1, y1, n=5.0):
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    rx, ry = (x1 - x0) / 2, (y1 - y0) / 2
    v = (np.abs((X - cx) / rx) ** n + np.abs((Y - cy) / ry) ** n) ** (1 / n)
    return (v - 1) * min(rx, ry)


def bezier(p0, p1, p2, n=240):
    t = np.linspace(0, 1, n)[:, None]
    return (1 - t) ** 2 * np.array(p0) + 2 * (1 - t) * t * np.array(p1) + t * t * np.array(p2)


def along(pts, X, Y):
    """For every pixel: distance to the curve, position along it (0..1) and which side it is on."""
    best = np.full(X.shape, 1e9, np.float32)
    tpos = np.zeros(X.shape, np.float32)
    side = np.zeros(X.shape, np.float32)
    n = len(pts) - 1
    for i, (a, b) in enumerate(zip(pts[:-1], pts[1:])):
        vx, vy = b[0] - a[0], b[1] - a[1]
        L = vx * vx + vy * vy
        t = np.clip(((X - a[0]) * vx + (Y - a[1]) * vy) / L, 0, 1)
        dx, dy = X - (a[0] + t * vx), Y - (a[1] + t * vy)
        d = np.sqrt(dx * dx + dy * dy)
        m = d < best
        best = np.where(m, d, best)
        tpos = np.where(m, (i + t) / n, tpos)
        side = np.where(m, np.sign(vx * dy - vy * dx), side)
    return best, tpos, side


def feather(X, Y, scale=1.0, ox=0.0, oy=0.0):
    """Coverage of a white feather: a curved quill with a soft vane and a few splits."""
    X = (X - ox) / scale
    Y = (Y - oy) / scale
    shaft = bezier((318, 742), (470, 560), (716, 262))
    d, t, side = along(shaft, X, Y)

    # Vane: widest a little above the middle, tapering to a point at the tip and near the quill.
    u = np.clip((t - 0.16) / 0.84, 0, 1)
    profile = np.sin(np.pi * u) ** 0.75 * (1 - 0.25 * u)
    width = np.where(side > 0, 128, 104) * profile                 # one side a bit fuller
    vane = d - width
    vane = np.where(t < 0.16, 1e3, vane)

    # Splits in the vane: wedges that open at the edge and run in toward the quill, angled to the tip,
    # which is what makes it read as a feather rather than a leaf.
    splits = ((0.30, 1, 0.62), (0.42, -1, 0.55), (0.52, 1, 0.58), (0.64, -1, 0.5), (0.76, 1, 0.45))
    for tc, sd, depth in splits:
        rel = np.clip(d / np.maximum(width, 1), 0, 1.5)
        centre = tc + rel * 0.075                     # barbs lean toward the tip
        half = 0.004 + 0.016 * np.clip((rel - (1 - depth)) / depth, 0, 1)   # wider at the edge
        cut = (side == sd) & (rel > 1 - depth) & (np.abs(t - centre) < half)
        vane = np.where(cut, np.maximum(vane, 2.0), vane)

    quill = d - np.where(t < 0.16, 11 - 4 * (0.16 - t) / 0.16, 7 * (1 - t) + 2)
    quill = np.where(t > 0.97, 1e3, quill)
    body = np.minimum(vane, quill)
    return cov(body * scale), cov((d - 5) * scale)  # feather, and the centre line (for a subtle shaft tint)


def app_icon():
    rgba = np.zeros((N, N, 4), np.float32)
    a = cov(squircle(xx, yy, 40, 40, 984, 984))
    # Diagonal gradient: deep violet -> magenta -> coral.
    t = np.clip((xx * 0.55 + yy * 0.45) / N, 0, 1)
    c1, c2, c3 = hexc('#4C1D95'), hexc('#A21CAF'), hexc('#FB7185')
    bg = np.where((t < 0.55)[..., None],
                  c1 * (1 - t / 0.55)[..., None] + c2 * (t / 0.55)[..., None],
                  c2 * (1 - (t - 0.55) / 0.45)[..., None] + c3 * ((t - 0.55) / 0.45)[..., None])
    # Soft light from the top-left and a gentle vignette.
    glow = np.exp(-(((xx - 300) ** 2 + (yy - 260) ** 2) / (2 * 380 ** 2)))[..., None]
    bg = bg + glow * 38

    f, spine = feather(xx, yy, 1.12, 512 - 512 * 1.12, 512 - 500 * 1.12)
    # Drop shadow under the feather.
    shadow = Image.fromarray((f * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(18))
    sh = np.roll(np.roll(np.asarray(shadow, np.float32) / 255, 14, 0), 8, 1)[..., None] * 0.28
    col = bg * (1 - sh)
    white = hexc('#FFFFFF')
    col = col * (1 - f[..., None]) + white * f[..., None]
    # Faint shaft line so the quill reads at large sizes.
    col = col * (1 - (spine * f * 0.18)[..., None]) + hexc('#E9D5FF') * (spine * f * 0.18)[..., None]
    rgba[..., :3] = col
    rgba[..., 3] = a * 255
    return Image.fromarray(np.clip(rgba, 0, 255).astype(np.uint8), 'RGBA')


def doc_icon(app):
    """A white page with a folded corner and the app tile on it."""
    rgba = np.zeros((N, N, 4), np.float32)
    x0, y0, x1, y1, f = 170, 60, 854, 964, 190
    inside = (xx >= x0) & (xx <= x1) & (yy >= y0) & (yy <= y1) & ~((xx > x1 - f) & (yy < y0 + f) & ((xx - (x1 - f)) > (yy - y0)))
    col = np.zeros((N, N, 3), np.float32) + hexc('#FAFAFB')
    fold = (xx > x1 - f) & (yy < y0 + f) & ((xx - (x1 - f)) <= (yy - y0))
    col = np.where(fold[..., None], hexc('#E4DDF4'), col)
    edge = inside & ((xx < x0 + 10) | (xx > x1 - 10) | (yy > y1 - 10) | (yy < y0 + 10))
    col = np.where(edge[..., None], hexc('#D6D0E6'), col)
    rgba[..., :3] = col
    rgba[..., 3] = np.where(inside, 255, 0)
    page = Image.fromarray(np.clip(rgba, 0, 255).astype(np.uint8), 'RGBA')
    tile = app.resize((470, 470), Image.LANCZOS)
    page.alpha_composite(tile, (277, 330))
    return page


out = sys.argv[1] if len(sys.argv) > 1 else '.'
app = app_icon()
doc = doc_icon(app)
sizes = [(s, s) for s in (16, 20, 24, 32, 40, 48, 64, 128, 256)]
app.resize((256, 256), Image.LANCZOS).save(os.path.join(out, 'app.png'))
app.save(os.path.join(out, 'app.ico'), sizes=sizes)
doc.save(os.path.join(out, 'project.ico'), sizes=sizes)
if len(sys.argv) > 2:
    app.resize((512, 512), Image.LANCZOS).save(os.path.join(sys.argv[2], 'app-512.png'))
    doc.resize((256, 256), Image.LANCZOS).save(os.path.join(sys.argv[2], 'project-preview.png'))
print('icons written to', out)

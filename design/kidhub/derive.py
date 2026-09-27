"""Leitet die Symbole von KidHub aus der gezeichneten Vorlage ab (Rezept: public-kidhub/ASSETS.md).

Vorlage: design/kidhub/KidHub.png (1254 x 1254, von einer Bild-KI, ohne Alphakanal: die Ecken
ausserhalb des abgerundeten Quadrats sind WEISS). Aufruf aus dem Repo-Wurzelverzeichnis:
    python3 design/kidhub/derive.py
Optional design/kidhub/KidHub2.png (maskable) — fehlt sie, entsteht die maskable-Fassung aus dem
Motiv der ersten Vorlage (blauer Grund herausgerechnet, auf 72 % verkleinert, randloser Verlauf).
"""
from collections import deque
from pathlib import Path

import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
OUT = HERE.parent.parent / 'src' / 'frontend' / 'app' / 'public-kidhub'
NAVY = np.array([10, 43, 102], dtype=float)
WHITE = np.array([250, 251, 251], dtype=float)


def row_background(a: np.ndarray) -> np.ndarray:
    """Farbe des blauen Grunds je Zeile (Median der blauen Pixel), Luecken interpoliert."""
    h = a.shape[0]
    rows = np.full((h, 3), np.nan)
    for y in range(h):
        px = a[y]
        blue = px[(px[:, 2] > 200) & (px[:, 0] < 110) & (px[:, 1] > 115) & (px[:, 1] < 190)]
        if len(blue) > 20:
            rows[y] = np.median(blue, axis=0)
    idx = np.arange(h)
    for c in range(3):
        ok = ~np.isnan(rows[:, c])
        rows[:, c] = np.interp(idx, idx[ok], rows[ok, c])
    return rows


def fix_red_stroke(a: np.ndarray) -> int:
    """Ein Stueck Umriss an der Maehne ist rotlila statt dunkelblau — auf die Linie Dunkelblau↔Weiss legen."""
    y0, y1, x0, x1 = 640, 920, 760, 980
    sub = a[y0:y1, x0:x1].astype(float)
    r, g, b = sub[..., 0], sub[..., 1], sub[..., 2]
    mx, mn = sub.max(axis=2), sub.min(axis=2)
    sat = (mx - mn) / np.maximum(mx, 1)
    reddish = (sat > 0.18) & (r > g + 15) & (b > 40) & (r >= b - 25) & (mx < 245)
    lum = 0.299 * r + 0.587 * g + 0.114 * b
    lum_navy = float(0.299 * NAVY[0] + 0.587 * NAVY[1] + 0.114 * NAVY[2])
    t = np.clip((lum - lum_navy) / (250 - lum_navy), 0, 1)[..., None]
    sub[reddish] = (NAVY * (1 - t) + WHITE * t)[reddish]
    a[y0:y1, x0:x1] = sub.astype(np.uint8)
    return int(reddish.sum())


def transparent_corners(a: np.ndarray, bg: np.ndarray) -> np.ndarray:
    """Alpha fuer die weissen Ecken: nur, was mit der Bildecke verbunden ist (ein weisses Funkeln
    im Blau bleibt stehen), Randpixel weich nach ihrem Weissanteil."""
    h, w, _ = a.shape
    white_share = np.clip((a[..., 0].astype(float) - bg[:, None, 0]) / (250 - bg[:, None, 0]), 0, 1)
    alpha = np.ones((h, w))
    seen = np.zeros((h, w), dtype=bool)
    box = 300
    for cy, cx in ((0, 0), (0, w - 1), (h - 1, 0), (h - 1, w - 1)):
        q = deque([(cy, cx)])
        seen[cy, cx] = True
        while q:
            y, x = q.popleft()
            alpha[y, x] = 1 - white_share[y, x]
            for ny, nx in ((y + 1, x), (y - 1, x), (y, x + 1), (y, x - 1)):
                if 0 <= ny < h and 0 <= nx < w and not seen[ny, nx] \
                        and abs(ny - cy) < box and abs(nx - cx) < box and white_share[ny, nx] > 0.03:
                    seen[ny, nx] = True
                    q.append((ny, nx))
    rgb = a.astype(float)
    # Randpixel tragen Weiss beigemischt — die Farbe auf den blauen Grund der Zeile setzen.
    rgb[seen] = np.broadcast_to(bg[:, None, :], rgb.shape)[seen]
    return np.dstack([rgb, alpha * 255]).astype(np.uint8)


def full_bleed(size: int, bg: np.ndarray) -> Image.Image:
    """Randloser Verlauf in der Farbe des Grunds (fuer Apple-Symbol und maskable)."""
    ys = np.linspace(0, len(bg) - 1, size)
    col = np.stack([np.interp(ys, np.arange(len(bg)), bg[:, c]) for c in range(3)], axis=1)
    return Image.fromarray(np.broadcast_to(col[:, None, :], (size, size, 3)).astype(np.uint8), 'RGB')


def motif_only(a: np.ndarray, bg: np.ndarray, rgba: np.ndarray) -> Image.Image:
    """Alles ausser dem blauen Grund (Stern, Springer, Funkeln) — fuer die maskable-Fassung."""
    # Die BEREINIGTEN Farben (Eckensaum schon auf den Grund gesetzt) — mit den rohen kaeme der weisse
    # Saum der alten Ecken als feiner Bogen mit ins Motiv.
    rgb = rgba[..., :3]
    diff = np.abs(rgb.astype(float) - bg[:, None, :]).sum(axis=2)
    alpha = np.clip((diff - 18) / 40, 0, 1) * (rgba[..., 3] / 255)
    return Image.fromarray(np.dstack([rgb, alpha * 255]).astype(np.uint8), 'RGBA')


def main() -> None:
    src = Image.open(HERE / 'KidHub.png').convert('RGB')
    a = np.array(src)
    print('rotlila Pixel umgefaerbt:', fix_red_stroke(a))
    bg = row_background(a)
    rgba = transparent_corners(a, bg)
    icon = Image.fromarray(rgba, 'RGBA')
    (OUT / 'icons').mkdir(parents=True, exist_ok=True)

    for size in (512, 192):
        icon.resize((size, size), Image.LANCZOS).save(OUT / 'icons' / f'icon-{size}.png', optimize=True)
    apple = full_bleed(1254, bg)
    apple.paste(icon, (0, 0), icon)
    apple.resize((180, 180), Image.LANCZOS).save(OUT / 'icons' / 'apple-touch-icon.png', optimize=True)
    icon.save(OUT / 'favicon.ico', sizes=[(16, 16), (32, 32), (48, 48)])

    maskable_src = HERE / 'KidHub2.png'
    if maskable_src.exists():
        mask = Image.open(maskable_src).convert('RGB')
    else:
        motif = motif_only(a, bg, rgba)
        mask = full_bleed(1254, bg)
        scale = 0.72
        m = motif.resize((round(1254 * scale), round(1254 * scale)), Image.LANCZOS)
        off = (1254 - m.width) // 2
        mask.paste(m, (off, off), m)
    for size in (512, 192):
        mask.resize((size, size), Image.LANCZOS).save(OUT / 'icons' / f'icon-{size}-maskable.png', optimize=True)
    og_src = HERE / 'KidHub3.png'
    if og_src.exists():
        # Gezeichnete Vorlage 1536 x 1024 → senkrecht mittig auf 1200 x 630 zuschneiden.
        og = Image.open(og_src).convert('RGB')
        w = 1200; h = round(og.height * w / og.width)
        og = og.resize((w, h), Image.LANCZOS).crop((0, (h - 630) // 2, 1200, (h - 630) // 2 + 630))
    else:
        og = og_from_icon(icon, bg)
    og.save(OUT / 'og-image.png', optimize=True)
    print('fertig:', sorted(p.name for p in (OUT / 'icons').iterdir()), '+ favicon.ico, og-image.png')


def og_from_icon(icon: Image.Image, bg: np.ndarray) -> Image.Image:
    """Vorschaubild 1200 x 630: Symbol links, Name und Satz rechts, Grund im Verlauf des Symbols."""
    from PIL import ImageDraw, ImageFont
    og = full_bleed(1200, bg).crop((0, 285, 1200, 915))
    size = 520
    og.paste(icon.resize((size, size), Image.LANCZOS), (55, (630 - size) // 2), icon.resize((size, size), Image.LANCZOS))
    draw = ImageDraw.Draw(og)
    font_dir = Path('/usr/share/fonts/truetype/dejavu')
    title = ImageFont.truetype(str(font_dir / 'DejaVuSans-Bold.ttf'), 118)
    sub = ImageFont.truetype(str(font_dir / 'DejaVuSans-Bold.ttf'), 36)
    shadow = (16, 60, 120)
    for (x, y), text, font, fill in (((625, 190), 'KidHub', title, (255, 255, 255)),
                                     ((632, 345), 'Schach-Puzzles für Kinder –', sub, (234, 246, 255)),
                                     ((632, 395), 'ganz einfach, Stufe für Stufe.', sub, (234, 246, 255))):
        draw.text((x + 3, y + 4), text, font=font, fill=shadow)
        draw.text((x, y), text, font=font, fill=fill)
    return og


if __name__ == '__main__':
    main()

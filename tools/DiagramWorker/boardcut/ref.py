"""Bildpipeline für Brett-Bilder — NUR mit Operationen, die in C# 1:1 nachgebaut werden (EbookBoardImage.cs).
Grau: float32 0..1 (0 = schwarz). Kein PIL-Resize, kein scipy."""
import numpy as np

def area_resize(a, h, w):
    """Flächenmittel (Box-Filter) auf h×w über das Integralbild mit bilinearer Abtastung an gebrochenen Rändern."""
    H, W = a.shape
    I = np.zeros((H + 1, W + 1)); I[1:, 1:] = np.cumsum(np.cumsum(a.astype(np.float64), 0), 1)
    ys = np.linspace(0, H, h + 1); xs = np.linspace(0, W, w + 1)
    def S(y, x):   # y: (n,1), x: (1,m)
        y0 = np.floor(y).astype(int); x0 = np.floor(x).astype(int)
        y1 = np.minimum(y0 + 1, H); x1 = np.minimum(x0 + 1, W); fy = y - y0; fx = x - x0
        return I[y0, x0] * (1 - fy) * (1 - fx) + I[y1, x0] * fy * (1 - fx) + I[y0, x1] * (1 - fy) * fx + I[y1, x1] * fy * fx
    Y = ys[:, None]; X = xs[None, :]
    T = S(Y, X)
    tot = T[1:, 1:] - T[:-1, 1:] - T[1:, :-1] + T[:-1, :-1]
    area = (np.diff(ys)[:, None] * np.diff(xs)[None, :])
    return (tot / np.maximum(area, 1e-9)).astype(np.float32)

def gauss_kernel(sigma):
    r = int(4 * sigma + 0.5); x = np.arange(-r, r + 1); k = np.exp(-0.5 * (x / sigma) ** 2); return (k / k.sum()).astype(np.float64), r

def blur(a, sigma):
    """Separabler Gauß, Rand gespiegelt (… c b a | a b c …) wie scipy 'reflect'."""
    k, r = gauss_kernel(sigma)
    def conv1(a):
        p = np.concatenate([a[:, :r][:, ::-1], a, a[:, -r:][:, ::-1]], axis=1) if r > 0 else a
        out = np.zeros_like(a, dtype=np.float64)
        for i in range(2 * r + 1): out += k[i] * p[:, i:i + a.shape[1]]
        return out
    return conv1(conv1(a.astype(np.float64)).T).T.astype(np.float32)

def dilate4(m):
    o = m.copy(); o[1:] |= m[:-1]; o[:-1] |= m[1:]; o[:, 1:] |= m[:, :-1]; o[:, :-1] |= m[:, 1:]; return o

def shift_bilinear(a, dy, dx, cval=1.0):
    H, W = a.shape
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float64)
    sy, sx = yy - dy, xx - dx
    y0 = np.floor(sy).astype(int); x0 = np.floor(sx).astype(int); fy = sy - y0; fx = sx - x0
    def at(y, x):
        ok = (y >= 0) & (y < H) & (x >= 0) & (x < W)
        v = np.full((H, W), cval, np.float64); v[ok] = a[y[ok], x[ok]]; return v
    out = at(y0, x0) * (1 - fy) * (1 - fx) + at(y0 + 1, x0) * fy * (1 - fx) + at(y0, x0 + 1) * (1 - fy) * fx + at(y0 + 1, x0 + 1) * fy * fx
    return out.astype(np.float32)

def cells(g, box, size=32):
    x0, y0, x1, y1 = box; out = []
    for r in range(8):
        for c in range(8):
            cx0 = x0 + (x1 - x0) * c / 8; cx1 = x0 + (x1 - x0) * (c + 1) / 8
            cy0 = y0 + (y1 - y0) * r / 8; cy1 = y0 + (y1 - y0) * (r + 1) / 8
            m = (cx1 - cx0) * 0.06
            sub = g[int(cy0 + m):int(cy1 - m), int(cx0 + m):int(cx1 - m)]
            out.append(area_resize(sub, size, size))
    return out

def dist(a, b): return np.abs(blur(a, 1.6) - blur(b, 1.6))[4:28, 4:28]
def occupied_strong(d): return (d > 0.18).mean() > 0.06
def occupied_weak(d): return d.max() > 0.12

def backgrounds(boards):
    light = np.median([cl[i] for cl in boards for i in range(64) if (i // 8 + i % 8) % 2 == 0], axis=0)
    dark = np.median([cl[i] for cl in boards for i in range(64) if (i // 8 + i % 8) % 2 == 1], axis=0)
    flip = light.mean() < dark.mean()
    par = lambda i: ((light if flip else dark) if (i // 8 + i % 8) % 2 == 1 else (dark if flip else light))
    pos = []
    for i in range(64):
        empt = [cl[i] for cl in boards if not occupied_strong(dist(cl[i], par(i)))]
        pool = empt if len(empt) >= max(5, len(boards) * 0.3) else [cl[i] for cl in boards]
        pos.append(np.median(pool, axis=0).astype(np.float32))
    return pos

def feature1(c, bg):
    full = np.abs(blur(c, 1.0) - blur(bg, 1.0))
    fg = np.where(dilate4(full > 0.2), c, 1.0).astype(np.float32)
    ink = 1.0 - fg; s = ink.sum()
    if s > 1:
        yy, xx = np.mgrid[0:32, 0:32]; cy = (ink * yy).sum() / s; cx = (ink * xx).sum() / s
        fg = shift_bilinear(fg, 15.5 - cy, 15.5 - cx)
    return (1.0 - area_resize(np.clip(fg, 0, 1), 24, 24)).ravel()

def feature2(c, bg):
    raw = 1.0 - area_resize(c, 24, 24)
    diff = area_resize(np.clip(0.5 + (blur(c, 0.8) - blur(bg, 0.8)), 0, 1), 24, 24) - 0.5
    return np.concatenate([raw.ravel(), diff.ravel() * 2]).astype(np.float32)

def find_board(g):
    """g: Grau 0..1. Größte Komponente (8er-Nachbarschaft) dunkler als 235/255, dann abschälen."""
    mask = g < 235 / 255
    H, W = mask.shape; lab = np.zeros((H, W), np.int32); best = None; nid = 0
    for y in range(H):
        for x in range(W):
            if mask[y, x] and lab[y, x] == 0:
                nid += 1; stack = [(y, x)]; lab[y, x] = nid; ymin = ymax = y; xmin = xmax = x
                while stack:
                    cy, cx = stack.pop()
                    if cy < ymin: ymin = cy
                    if cy > ymax: ymax = cy
                    if cx < xmin: xmin = cx
                    if cx > xmax: xmax = cx
                    for dy in (-1, 0, 1):
                        for dx in (-1, 0, 1):
                            ny, nx = cy + dy, cx + dx
                            if 0 <= ny < H and 0 <= nx < W and mask[ny, nx] and lab[ny, nx] == 0:
                                lab[ny, nx] = nid; stack.append((ny, nx))
                area = (ymax - ymin + 1) * (xmax - xmin + 1)
                if best is None or area > best[0]: best = (area, ymin, ymax + 1, xmin, xmax + 1)
    if best is None: return None
    _, y0, y1, x0, x1 = best
    w, h = x1 - x0, y1 - y0
    if w < 80 or h < 80 or not (0.85 < w / h < 1.18): return None
    for _ in range(40):
        changed = False
        for side in 'tblr':
            line = g[y0, x0:x1] if side == 't' else g[y1 - 1, x0:x1] if side == 'b' else g[y0:y1, x0] if side == 'l' else g[y0:y1, x1 - 1]
            if (line < 0.5).mean() > 0.8 or (line > 235 / 255).mean() > 0.97:
                if side == 't': y0 += 1
                elif side == 'b': y1 -= 1
                elif side == 'l': x0 += 1
                else: x1 -= 1
                changed = True
        if not changed: break
    w, h = x1 - x0, y1 - y0
    if w < 64 or h < 64 or not (0.9 < w / h < 1.1): return None
    return x0, y0, x1, y1

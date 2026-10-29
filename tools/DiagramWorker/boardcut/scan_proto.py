import sys, os, subprocess, numpy as np, json
from scipy import ndimage
from PIL import Image
sys.path.insert(0, os.path.dirname(__file__)); import ref, mlp
S = os.path.dirname(__file__) + '/'
M = np.load(S + os.environ.get('MODEL', 'model-ref.npz')); P1 = [M['W1a'], M['b1a'], M['W2a'], M['b2a']]; P2 = [M['W1b'], M['b1b'], M['W2b'], M['b2b']]
PIECES = 'PNBRQKpnbrqk'

def peel(g, y0, y1, x0, x1):
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
    return y0, y1, x0, x1

def find_boards(g):
    H, W = g.shape
    lab, n = ndimage.label(g < 235 / 255, structure=np.ones((3, 3)))
    out = []
    for i, sl in enumerate(ndimage.find_objects(lab)):
        y0, y1, x0, x1 = sl[0].start, sl[0].stop, sl[1].start, sl[1].stop
        w, h = x1 - x0, y1 - y0
        if w < 100 or h < 100 or not (0.85 < w / h < 1.18): continue
        y0, y1, x0, x1 = peel(g, y0, y1, x0, x1)
        w, h = x1 - x0, y1 - y0
        if w < 96 or h < 96 or not (0.85 < w / h < 1.18): continue
        # Fehlt eine Rahmenlinie, endet das Brett an der ersten Schraffur — auf Quadrat ergänzen, und zwar so, wie das
        # Schachbrettmuster am besten passt.
        d = abs(w - h)
        cands = [(x0, y0, x1, y1)]
        if w < h: cands = [(max(0, x0 - d), y0, x1, y1), (x0, y0, min(W, x1 + d), y1), (max(0, x0 - d // 2), y0, min(W, x1 + d - d // 2), y1)]
        elif h < w: cands = [(x0, max(0, y0 - d), x1, y1), (x0, y0, x1, min(H, y1 + d)), (x0, max(0, y0 - d // 2), x1, min(H, y1 + d - d // 2))]
        best = max(cands, key=lambda b: grid_fit(ref.cells(g, b)))  # erstes Maximum wie in C#
        out.append(best)
    return out

def grid_fit(cl):
    light = np.median([cl[i] for i in range(64) if (i // 8 + i % 8) % 2 == 0], axis=0)
    dark = np.median([cl[i] for i in range(64) if (i // 8 + i % 8) % 2 == 1], axis=0)
    ring = np.ones((32, 32), bool); ring[6:26, 6:26] = False
    fit = 0.0
    for i in range(64):
        own, other = (light, dark) if (i // 8 + i % 8) % 2 == 0 else (dark, light)
        fit += (np.abs(cl[i] - other)[ring].astype(np.float64).mean() - np.abs(cl[i] - own)[ring].astype(np.float64).mean()) / 64
    return fit if abs(float(light.mean()) - float(dark.mean())) >= 0.08 else fit - 100

def grid_score(cl):
    light = np.median([cl[i] for i in range(64) if (i // 8 + i % 8) % 2 == 0], axis=0)
    dark = np.median([cl[i] for i in range(64) if (i // 8 + i % 8) % 2 == 1], axis=0)
    ring = np.ones((32, 32), bool); ring[6:26, 6:26] = False
    hits = 0
    for i in range(64):
        own, other = (light, dark) if (i // 8 + i % 8) % 2 == 0 else (dark, light)
        hits += np.abs(cl[i] - own)[ring].mean() < np.abs(cl[i] - other)[ring].mean()
    return hits + (0 if abs(float(light.mean()) - float(dark.mean())) >= 0.08 else -100)

def render(path, page, scale=150):
    if path.lower().endswith('.pdf'):
        subprocess.run(['pdftoppm', '-r', str(scale * 2), '-gray', '-png', '-singlefile', '-f', str(page), '-l', str(page), path, S + 'tmp'], check=True, capture_output=True)
        a = np.asarray(Image.open(S + 'tmp.png').convert('L'), np.float32) / 255.0
        h, w = a.shape[0] // 2 * 2, a.shape[1] // 2 * 2
        a = a[:h, :w]
        a = ((a[0::2, 0::2] + a[1::2, 0::2] + a[0::2, 1::2] + a[1::2, 1::2]) / 4).astype(np.float32)
        sg = float(os.environ.get("SIG", "0"))
        return ref.blur(a, sg) if sg > 0 else a
    tmp = S + 'tmp.pgm'
    subprocess.run(['ddjvu', '-format=pgm', f'-page={page}', f'-scale={scale}', path, tmp], check=True, capture_output=True)
    return np.asarray(Image.open(tmp).convert('L'), np.float32) / 255.0

if __name__ == '__main__':
    path = sys.argv[1]; out = sys.argv[2]
    n = int(subprocess.run(['djvused', '-e', 'n', path], capture_output=True, text=True, encoding='utf-8', errors='replace').stdout) if not path.lower().endswith('.pdf') else int([l for l in subprocess.run(['pdfinfo', path], capture_output=True, text=True, encoding='utf-8', errors='replace').stdout.splitlines() if l.startswith('Pages:')][0].split()[1])
    boards = []
    for p in range(1, n + 1):
        g = render(path, p)
        for b in find_boards(g):
            boards.append((p, b, ref.cells(g, b)))
    print('Bretter', len(boards), flush=True)
    mode = os.environ.get('BG', 'board')
    if mode == 'book':
        samp = [cl for _, _, cl in boards][:: max(1, len(boards) // 240)][:240]
        bookpos = ref.backgrounds(samp)
    res = []; ok = 0; rej = 0
    for p, b, cl in boards:
        light = np.median([cl[i] for i in range(64) if (i // 8 + i % 8) % 2 == 0], axis=0)
        dark = np.median([cl[i] for i in range(64) if (i // 8 + i % 8) % 2 == 1], axis=0)
        # Schachbrettmuster: helle und dunkle Felder müssen sich deutlich unterscheiden, und jedes Feld liegt (in der
        # Feldmitte-Rand-Zone) näher an seinem eigenen Paritäts-Hintergrund.
        if grid_score(cl) < 52: rej += 1; continue
        pos = bookpos if mode == 'book' else [light if (i // 8 + i % 8) % 2 == 0 else dark for i in range(64)]
        grid = ['.'] * 64
        for i in range(64):
            d = ref.dist(cl[i], pos[i]); strong = ref.occupied_strong(d); weak = ref.occupied_weak(d)
            if not (strong or weak): continue
            if not strong and mlp.predict(P2, ref.feature2(cl[i], pos[i])[None])[0, 12] > 0.5: continue
            grid[i] = PIECES[int(mlp.predict(P1, ref.feature1(cl[i], pos[i])[None]).argmax())]
        good = grid.count('K') == 1 and grid.count('k') == 1 and not any(c in 'Pp' for c in grid[:8] + grid[56:])
        ok += good
        res.append([p, list(map(int, b)), ''.join(grid), bool(good)])
    print('verworfen', rej, 'plausibel', ok, 'von', len(res), f'({ok / max(1, len(res)):.0%})')
    json.dump(res, open(out, 'w'))

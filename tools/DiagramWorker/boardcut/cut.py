"""Brett-Ausschneiden für den Container-Worker (0.874.0). work() ist WÖRTLICH aus dem Laptop-Worker
(~/claude/ebook-board-training/worker/worker.py) übernommen, scan_proto.py und ref.py ebenso — die Opus-Schleife liest
cells.npz/crops genau in diesem Format (Felder 32×32 je Diagramm, Rand um das Brett). Änderungen nur gemeinsam mit dem
Laptop-Worker und fs/prep.py."""
import os, sys, subprocess
import numpy as np
from PIL import Image
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
CS = 64


def work(args):
    import scan_proto, ref
    path, page, diags, outdir = args
    tmp = os.path.join(outdir, f'tmp_{os.getpid()}')
    try:
        dpi = 300   # Riesenseiten (27.10., z. B. #465 mit 4462 pt): lange Seite höchstens ~6000 px, sonst 800 MB je Seite
        try:
            sz = [l for l in subprocess.run(['pdfinfo', '-f', str(page), '-l', str(page), path], capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=30).stdout.splitlines() if 'size:' in l][0]
            wpt, hpt = [float(v) for v in sz.split('size:')[1].split('pts')[0].split(' x ')]
            dpi = min(300, int(6000 * 72 / max(wpt, hpt)))
        except Exception: pass
        subprocess.run(['pdftoppm', '-r', str(dpi), '-singlefile', '-f', str(page), '-l', str(page), path, tmp], check=True, capture_output=True, timeout=120)
        color = Image.open(tmp + '.ppm').convert('RGB'); full = np.asarray(color.convert('L'), np.float32) / 255.0
    except Exception:
        return []
    h, w = full.shape[0] // 2 * 2, full.shape[1] // 2 * 2; a = full[:h, :w]
    g = ref.blur(((a[0::2, 0::2] + a[1::2, 0::2] + a[0::2, 1::2] + a[1::2, 1::2]) / 4).astype(np.float32), 1.0)
    H, W = g.shape
    boards = scan_proto.find_boards(g) + scan_proto.find_boards(np.where(g < 210 / 255, g, 1.0).astype(np.float32)); out = []
    for did, x, y, bw, bh, fen, conf in diags:
        best = None; bi = 0
        for b in boards:
            x0, y0, x1, y1 = b[0] / W, b[1] / H, b[2] / W, b[3] / H
            ix = max(0, min(x + bw, x1) - max(x, x0)); iy = max(0, min(y + bh, y1) - max(y, y0)); inter = ix * iy
            iou = inter / (bw * bh + (x1 - x0) * (y1 - y0) - inter + 1e-9)
            if iou > bi: bi = iou; best = b
        if best is None or bi < 0.5:
            best = (round(x * W), round(y * H), round((x + bw) * W), round((y + bh) * H))
        X0, Y0, X1, Y1 = [v * 2 for v in best]
        bd = np.asarray(Image.fromarray((full[Y0:Y1, X0:X1] * 255).astype(np.uint8)).resize((8 * CS, 8 * CS), Image.BILINEAR), np.uint8)
        cells = bd.reshape(8, CS, 8, CS).transpose(0, 2, 1, 3).reshape(64, CS, CS)
        cells = cells.reshape(64, 32, 2, 32, 2).mean(axis=(2, 4)).round().astype(np.uint8)
        m = int(0.04 * (X1 - X0))
        color.crop((max(0, X0 - m), max(0, Y0 - m), min(color.width, X1 + m), min(color.height, Y1 + m))).save(os.path.join(outdir, 'crops', f'{did}.png'))
        out.append((cells, did, fen, conf, (page, x, y, bw, bh)))
    try: os.remove(tmp + '.ppm')
    except OSError: pass
    return out

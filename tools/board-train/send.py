"""Trainingsdaten an den Trainingsdienst (server.py, z. B. auf dem DGX Spark) schicken und das Ergebnis abholen.

    python3 send.py --url http://spark:8000 [--epochs 12] [--out ~/claude/ebook-board-training/spark/out]
Token aus BOARD_TRAIN_TOKEN (oder --token). Packt opus-labels/*.npz → books/, hr/hr_*.npz → base/ (je 32 px) und
hr/final32.pt aus --src (Vorgabe ~/claude/ebook-board-training) als tgz, lädt hoch, wartet, lädt model_all.onnx/.pt,
eval.tsv und train.log nach --out.
"""
import argparse, glob, io, json, os, sys, tarfile, tempfile, time, urllib.request
import numpy as np

ap = argparse.ArgumentParser()
ap.add_argument('--url', required=True); ap.add_argument('--token', default=os.environ.get('BOARD_TRAIN_TOKEN', ''))
ap.add_argument('--src', default=os.path.expanduser('~/claude/ebook-board-training'))
ap.add_argument('--out', default=os.path.expanduser('~/claude/ebook-board-training/spark/out'))
ap.add_argument('--epochs', type=int, default=12); ap.add_argument('--batch', type=int, default=1024)
a = ap.parse_args()
if not a.token: sys.exit('Token fehlt: BOARD_TRAIN_TOKEN setzen oder --token')
H = {'Authorization': f'Bearer {a.token}'}

def small(c):
    c = np.asarray(c)
    if c.shape[-1] == 32: return c.reshape(len(c), 64, 32, 32)
    v = c.reshape(-1, 32, 2, 32, 2).mean(axis=(2, 4))   # 64 → 32: 2×2 mitteln
    return v.round().clip(0, 255).astype(np.uint8).reshape(len(c), 64, 32, 32)

def add_npz(t, name, cells, labels):
    b = io.BytesIO(); np.savez_compressed(b, cells=small(cells), labels=labels); data = b.getvalue()
    ti = tarfile.TarInfo(name); ti.size = len(data); t.addfile(ti, io.BytesIO(data))

tmp = tempfile.NamedTemporaryFile(suffix='.tgz', delete=False); n = boards = 0
with tarfile.open(fileobj=tmp, mode='w:gz') as t:
    for f in sorted(glob.glob(os.path.join(a.src, 'opus-labels', '*.npz'))):
        z = np.load(f); add_npz(t, f'books/{os.path.basename(f)}', z['cells'], z['labels']); n += 1; boards += len(z['labels'])
    for f in sorted(glob.glob(os.path.join(a.src, 'hr', 'hr_*.npz'))):
        z = np.load(f); add_npz(t, f'base/{os.path.basename(f)}', z['cells'], z['labels'])
    t.add(os.path.realpath(os.path.join(a.src, 'hr', 'final32.pt')), arcname='final32.pt')   # echte Datei, kein Symlink (Dienst lehnt Links ab)
tmp.close(); size = os.path.getsize(tmp.name)
print(f'{n} Bücher, {boards} Opus-Bretter, {size >> 20} MB → {a.url}', flush=True)

with open(tmp.name, 'rb') as f:
    req = urllib.request.Request(f'{a.url}/jobs?epochs={a.epochs}&batch={a.batch}', data=f, method='POST',
                                 headers={**H, 'Content-Type': 'application/gzip', 'Content-Length': str(size)})
    job = json.loads(urllib.request.urlopen(req, timeout=3600).read())['id']
os.unlink(tmp.name); print(f'Auftrag {job} angenommen', flush=True)

last = ''
while True:
    time.sleep(20)
    st = json.loads(urllib.request.urlopen(urllib.request.Request(f'{a.url}/jobs/{job}', headers=H), timeout=60).read())
    line = (st.get('log') or '').strip().splitlines()[-1:] or ['']
    if line[0] != last: print(line[0], flush=True); last = line[0]
    if st['status'] != 'läuft': break
if st['status'] != 'fertig': sys.exit(f'Training fehlgeschlagen: {st.get("error", "")}\n{st.get("log", "")}')
os.makedirs(a.out, exist_ok=True)
for name in ('model_all.onnx', 'model_all.pt', 'eval.tsv', 'train.log'):
    data = urllib.request.urlopen(urllib.request.Request(f'{a.url}/jobs/{job}/files/{name}', headers=H), timeout=600).read()
    open(os.path.join(a.out, name), 'wb').write(data)
print(f'Ergebnis in {a.out}')

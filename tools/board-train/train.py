"""Brettfeld-Netz für RookHub trainieren (GPU, z. B. DGX Spark; läuft auch auf CPU).

Eingabe  /data/books/<ebookId>.npz   cells (n,64,32,32 uint8), labels (n,) FEN-Stellungsteil  — Opus-Lesungen je Buch
         /data/base/<name>.npz       cells, labels                                             — ältere Lehrbuch-Daten
         /data/final32.pt            Startgewichte (bisheriges Netz)
Ausgabe  /out/model_all.pt, /out/model_all.onnx (Eingang „cells“ [n,1,32,32] 0–1, Ausgang „logits“ [n,13] — wie
         Assets/ebook-board-cnn.onnx in RookHub), /out/eval.tsv (je Buch richtig von zurückgehaltenen), /out/train.log
Klassen  PNBRQKpnbrqk. (Index 12 = leer)
"""
import argparse, glob, os, time, numpy as np, torch, torch.nn as nn, torch.nn.functional as F

PI = 'PNBRQKpnbrqk.'
ap = argparse.ArgumentParser()
ap.add_argument('--data', default='/data'); ap.add_argument('--out', default='/out')
ap.add_argument('--epochs', type=int, default=12); ap.add_argument('--batch', type=int, default=1024)
ap.add_argument('--lr', type=float, default=3e-3); ap.add_argument('--hold', type=int, default=10)
ap.add_argument('--empty', type=float, default=0.3, help='Anteil leerer Felder im Training')
ap.add_argument('--scratch', action='store_true', help='nicht von final32.pt starten')
a = ap.parse_args()
os.makedirs(a.out, exist_ok=True)
dev = torch.device('cuda' if torch.cuda.is_available() else 'cpu')
logf = open(os.path.join(a.out, 'train.log'), 'a')
def log(m):
    m = time.strftime('%H:%M:%S ') + m; print(m, flush=True); logf.write(m + '\n'); logf.flush()
log(f'Gerät: {dev} {torch.cuda.get_device_name(0) if dev.type == "cuda" else ""}')

class Net(nn.Module):
    def __init__(s):
        super().__init__()
        def blk(i, o): return nn.Sequential(nn.Conv2d(i, o, 3, padding=1), nn.BatchNorm2d(o), nn.ReLU(), nn.Conv2d(o, o, 3, padding=1), nn.BatchNorm2d(o), nn.ReLU(), nn.MaxPool2d(2))
        s.f = nn.Sequential(blk(1, 24), blk(24, 48), blk(48, 96))  # 32 → 4
        s.h = nn.Sequential(nn.Flatten(), nn.Dropout(0.3), nn.Linear(96 * 16, 128), nn.ReLU(), nn.Linear(128, 13))
    def forward(s, x): return s.h(s.f(x))

def augment(x):
    """Druck-/Scan-Varianten: Verschieben/Skalieren, Kontrast/Helligkeit, Schwellwert, Weichzeichnen, Rauschen."""
    b = x.shape[0]; d = x.device
    th = torch.zeros(b, 2, 3, device=d); sc = 1 + (torch.rand(b, device=d) - 0.5) * 0.2
    th[:, 0, 0] = sc; th[:, 1, 1] = sc; th[:, :, 2] = (torch.rand(b, 2, device=d) - 0.5) * 0.16
    x = F.grid_sample(x, F.affine_grid(th, x.shape, align_corners=False), padding_mode='border', align_corners=False)
    c = 1 + (torch.rand(b, 1, 1, 1, device=d) - 0.5) * 0.6; br = (torch.rand(b, 1, 1, 1, device=d) - 0.5) * 0.3
    x = (x - 0.5) * c + 0.5 + br
    m = torch.rand(b, device=d) < 0.25
    if m.any(): x[m] = (x[m] > torch.rand(int(m.sum()), 1, 1, 1, device=d) * 0.3 + 0.35).float()
    m = torch.rand(b, device=d) < 0.3
    if m.any(): x[m] = F.avg_pool2d(F.pad(x[m], (1, 1, 1, 1), mode='replicate'), 3, 1)
    x = x + torch.randn_like(x) * 0.03 * torch.rand(b, 1, 1, 1, device=d)
    return x.clamp(0, 1)

def lab(p):
    o = []
    for r in str(p).split('/'):
        for ch in r: o += ['.'] * int(ch) if ch.isdigit() else [ch]
    return [PI.index(c) for c in o] if len(o) == 64 and all(c in PI for c in o) else None

def small(c):
    c = np.asarray(c)
    if c.shape[-1] == 32: return c.reshape(len(c), 64, 32, 32)
    t = F.avg_pool2d(torch.from_numpy(c.reshape(-1, 1, 64, 64)).float(), 2)
    return t.round().clamp(0, 255).to(torch.uint8).numpy().reshape(len(c), 64, 32, 32)

rng = np.random.default_rng(1); Xs, Ys, test = [], [], {}
for f in sorted(glob.glob(os.path.join(a.data, 'books', '*.npz'))):
    z = np.load(f); L = [lab(l) for l in z['labels']]; ok = [k for k, l in enumerate(L) if l]
    if not ok: continue
    C = small(z['cells'][ok]); L = np.array([L[k] for k in ok]); idx = rng.permutation(len(ok))
    te, tr = idx[:a.hold], idx[a.hold:]
    test[os.path.basename(f)[:-4]] = (C[te], L[te]); Xs.append(C[tr].reshape(-1, 32, 32)); Ys.append(L[tr].reshape(-1))
for f in sorted(glob.glob(os.path.join(a.data, 'base', '*.npz'))):
    z = np.load(f); L = [lab(l) for l in z['labels']]; ok = [k for k, l in enumerate(L) if l]
    Xs.append(small(z['cells'][ok]).reshape(-1, 32, 32)); Ys.append(np.array([L[k] for k in ok]).reshape(-1))
X = np.concatenate(Xs); Y = np.concatenate(Ys); keep = (Y != 12) | (rng.random(len(Y)) < a.empty); X, Y = X[keep], Y[keep]
log(f'{len(test)} Bücher, {sum(len(v[1]) for v in test.values())} Testbretter, {len(Y)} Trainingsfelder')

net = Net().to(dev)
if not a.scratch and os.path.exists(os.path.join(a.data, 'final32.pt')):
    net.load_state_dict(torch.load(os.path.join(a.data, 'final32.pt'), map_location=dev)); log('Start: final32.pt')

def evaluate():
    net.eval(); ex = tot = 0; per = {}
    with torch.no_grad():
        for eid, (C, L) in test.items():
            p = net(torch.from_numpy(C.reshape(-1, 32, 32)).float().unsqueeze(1).to(dev) / 255).argmax(1).cpu().numpy().reshape(-1, 64)
            e = int((p == L).all(1).sum()); per[eid] = e; ex += e; tot += len(L)
    return ex, tot, per
e, t, _ = evaluate(); log(f'vorher: {e}/{t} Testbretter exakt ({e / max(t, 1):.1%})')

Xt = torch.from_numpy(X).to(dev); Yt = torch.from_numpy(Y).long().to(dev)
steps = a.epochs * ((len(Y) + a.batch - 1) // a.batch)
opt = torch.optim.AdamW(net.parameters(), a.lr / 3, weight_decay=1e-4)
sched = torch.optim.lr_scheduler.OneCycleLR(opt, a.lr, total_steps=steps)
best = -1; t0 = time.time()
for ep in range(a.epochs):
    net.train(); perm = torch.randperm(len(Yt), device=dev)
    for i in range(0, len(Yt), a.batch):
        j = perm[i:i + a.batch]; x = augment(Xt[j].float().unsqueeze(1) / 255)
        loss = F.cross_entropy(net(x), Yt[j], label_smoothing=0.05); opt.zero_grad(set_to_none=True); loss.backward(); opt.step(); sched.step()
    e, t, per = evaluate(); log(f'Epoche {ep + 1}/{a.epochs}: {e}/{t} exakt ({e / max(t, 1):.1%}), {time.time() - t0:.0f}s')
    if e >= best:
        best = e; torch.save(net.state_dict(), os.path.join(a.out, 'model_all.pt'))
        with open(os.path.join(a.out, 'eval.tsv'), 'w') as f:
            for k, v in sorted(per.items(), key=lambda kv: kv[1]): f.write(f'{k}\t{v}\t{len(test[k][1])}\n')

net.load_state_dict(torch.load(os.path.join(a.out, 'model_all.pt'), map_location=dev)); net.eval().cpu()
torch.onnx.export(net, torch.zeros(1, 1, 32, 32), os.path.join(a.out, 'model_all.onnx'), input_names=['cells'], output_names=['logits'],
                  dynamic_axes={'cells': {0: 'n'}, 'logits': {0: 'n'}}, opset_version=17, dynamo=False)
log(f'fertig: bestes {best} Testbretter exakt — /out/model_all.pt + model_all.onnx')

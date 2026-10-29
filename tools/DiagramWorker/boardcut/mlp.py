import numpy as np, sys, os
NC = int(os.environ.get('NC', '12'))
def augment(X, rng):
    """Leicht verschieben/skalieren/verdicken — damit fremde Figurensätze näher an den Trainingsraum rücken."""
    from scipy import ndimage
    out = np.empty_like(X)
    for k, x in enumerate(X):
        if x.size == 1152:   # zwei Ebenen gleich verschieben
            out[k] = np.concatenate([_aug1(x[:576].reshape(24, 24), rng, st := rng.bit_generator.state) , x[576:]]) if False else x
            continue
        im = x.reshape(24, 24)
        s = rng.uniform(0.85, 1.15)
        im = ndimage.zoom(im, s, order=1)
        h = im.shape[0]
        if h >= 24: o = (h - 24) // 2; im = im[o:o + 24, o:o + 24]
        else: p = (24 - h) // 2; im = np.pad(im, ((p, 24 - h - p), (p, 24 - h - p)))
        im = ndimage.shift(im, rng.uniform(-1.2, 1.2, 2), order=1)
        if rng.random() < 0.3: im = ndimage.grey_dilation(im, size=(2, 2))
        if rng.random() < 0.3: im = ndimage.gaussian_filter(im, 0.6)
        out[k] = np.clip(im, 0, 1).ravel()
    return out
def train(X, Y, hidden=192, epochs=25, lr=1e-3, seed=0, aug=True):
    rng = np.random.default_rng(seed)
    W1 = rng.normal(0, np.sqrt(2 / X.shape[1]), (X.shape[1], hidden)).astype(np.float32); b1 = np.zeros(hidden, np.float32)
    W2 = rng.normal(0, np.sqrt(2 / hidden), (hidden, NC)).astype(np.float32); b2 = np.zeros(NC, np.float32)
    P = [W1, b1, W2, b2]; M = [np.zeros_like(p) for p in P]; V = [np.zeros_like(p) for p in P]; t = 0
    for ep in range(epochs):
        idx = rng.permutation(len(X))
        Xa = augment(X, rng) if aug else X
        for s in range(0, len(X), 128):
            j = idx[s:s + 128]; x = Xa[j]; y = Y[j]
            h = np.maximum(0, x @ W1 + b1); z = h @ W2 + b2
            z -= z.max(1, keepdims=True); p = np.exp(z); p /= p.sum(1, keepdims=True)
            g = p; g[np.arange(len(y)), y] -= 1; g /= len(y)
            gW2 = h.T @ g; gb2 = g.sum(0); gh = g @ W2.T; gh[h <= 0] = 0
            gW1 = x.T @ gh; gb1 = gh.sum(0)
            t += 1
            for k, gr in enumerate([gW1, gb1, gW2, gb2]):
                M[k] = 0.9 * M[k] + 0.1 * gr; V[k] = 0.999 * V[k] + 0.001 * gr * gr
                P[k] -= lr * (M[k] / (1 - 0.9 ** t)) / (np.sqrt(V[k] / (1 - 0.999 ** t)) + 1e-8)
    return P
def predict(P, X):
    W1, b1, W2, b2 = P
    z = np.maximum(0, X @ W1 + b1) @ W2 + b2
    z -= z.max(1, keepdims=True); p = np.exp(z); return p / p.sum(1, keepdims=True)
if __name__ == '__main__':
    d = np.load(os.path.dirname(__file__) + '/' + os.environ.get('TRAIN', 'train.npz')); X, Y, B = d['X'], d['Y'], d['B']
    for hold in ['Grooten, Herman', 'Baird, W. J', 'Smith, Axel', 'Scheel, Johan']:
        tr = B != hold
        P = train(X[tr], Y[tr], epochs=8)
        acc = (predict(P, X[~tr]).argmax(1) == Y[~tr]).mean()
        print(f'ohne {hold}: Genauigkeit auf diesem ungesehenen Stil {acc:.3f} (n={(~tr).sum()})', flush=True)

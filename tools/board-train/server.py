"""Trainingsdienst im Container (DGX Spark): nimmt Trainingsdaten als tgz an, trainiert mit train.py, liefert das Netz.

    POST /jobs?epochs=12&batch=1024     Körper: tgz mit books/*.npz, base/*.npz, final32.pt  → {"id": …}
    GET  /jobs/<id>                     {"status": "läuft|fertig|fehler", "log": letzte Zeilen}
    GET  /jobs/<id>/files/<name>        model_all.onnx | model_all.pt | eval.tsv | train.log
    GET  /health
Zugang: Kopfzeile „Authorization: Bearer $TRAIN_TOKEN“ (Pflicht). Es läuft immer nur ein Training; ein zweites → 409.
"""
import json, os, secrets, shutil, subprocess, sys, tarfile, threading, time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlparse, parse_qs

TOKEN = os.environ.get('TRAIN_TOKEN', '')
WORK = os.environ.get('WORK_DIR', '/work')
MAX_UPLOAD = int(os.environ.get('MAX_UPLOAD_MB', '4096')) * 2**20
FILES = {'model_all.onnx', 'model_all.pt', 'eval.tsv', 'train.log'}
jobs = {}; lock = threading.Lock()

def safe_extract(tgz, dest):
    with tarfile.open(tgz) as t:
        for m in t.getmembers():
            p = os.path.realpath(os.path.join(dest, m.name))
            if not p.startswith(os.path.realpath(dest) + os.sep) or m.issym() or m.islnk() or m.isdev():
                raise ValueError(f'unerlaubter Eintrag im Archiv: {m.name}')
        t.extractall(dest)

def run(job_id, args):
    d = os.path.join(WORK, job_id)
    try:
        safe_extract(os.path.join(d, 'data.tgz'), os.path.join(d, 'data'))
        data = os.path.join(d, 'data')
        subs = [e for e in os.listdir(data) if os.path.isdir(os.path.join(data, e))]
        if 'books' not in subs and len(subs) == 1: data = os.path.join(data, subs[0])   # tgz mit einem Wurzelordner
        r = subprocess.run([sys.executable, os.path.join(os.path.dirname(__file__), 'train.py'), '--data', data, '--out', os.path.join(d, 'out')] + args,
                           stdout=open(os.path.join(d, 'stdout.log'), 'w'), stderr=subprocess.STDOUT)
        jobs[job_id]['status'] = 'fertig' if r.returncode == 0 else 'fehler'
    except Exception as ex:
        jobs[job_id]['status'] = 'fehler'; jobs[job_id]['error'] = str(ex)
    finally:
        jobs[job_id]['ended'] = time.time()
        shutil.rmtree(os.path.join(d, 'data'), ignore_errors=True)

class H(BaseHTTPRequestHandler):
    def _send(self, code, obj=None, body=None, ctype='application/json'):
        b = body if body is not None else json.dumps(obj, ensure_ascii=False).encode()
        self.send_response(code); self.send_header('Content-Type', ctype); self.send_header('Content-Length', str(len(b))); self.end_headers(); self.wfile.write(b)
    def _auth(self):
        if TOKEN and secrets.compare_digest(self.headers.get('Authorization', ''), f'Bearer {TOKEN}'): return True
        self._send(401, {'error': 'Token fehlt oder falsch'}); return False
    def do_GET(self):
        u = urlparse(self.path); parts = [p for p in u.path.split('/') if p]
        if parts == ['health']: return self._send(200, {'ok': True, 'cuda': _cuda()})
        if not self._auth(): return
        if len(parts) == 2 and parts[0] == 'jobs' and parts[1] in jobs:
            d = os.path.join(WORK, parts[1]); log = ''
            for f in (os.path.join(d, 'out', 'train.log'), os.path.join(d, 'stdout.log')):
                if os.path.exists(f): log = ''.join(open(f, errors='replace').readlines()[-15:]); break
            return self._send(200, {**jobs[parts[1]], 'log': log})
        if len(parts) == 4 and parts[0] == 'jobs' and parts[1] in jobs and parts[2] == 'files' and parts[3] in FILES:
            f = os.path.join(WORK, parts[1], 'out', parts[3])
            if os.path.exists(f): return self._send(200, body=open(f, 'rb').read(), ctype='application/octet-stream')
        self._send(404, {'error': 'nicht gefunden'})
    def do_POST(self):
        if not self._auth(): return
        u = urlparse(self.path)
        if u.path.rstrip('/') != '/jobs': return self._send(404, {'error': 'nicht gefunden'})
        n = int(self.headers.get('Content-Length', '0'))
        if n <= 0 or n > MAX_UPLOAD: return self._send(413, {'error': f'Upload fehlt oder größer als {MAX_UPLOAD >> 20} MB'})
        with lock:
            if any(j['status'] == 'läuft' for j in jobs.values()): return self._send(409, {'error': 'es läuft schon ein Training'})
            job_id = time.strftime('%Y%m%d-%H%M%S'); d = os.path.join(WORK, job_id); os.makedirs(d)
            jobs[job_id] = {'id': job_id, 'status': 'läuft', 'started': time.time()}
        with open(os.path.join(d, 'data.tgz'), 'wb') as f:
            left = n
            while left: chunk = self.rfile.read(min(left, 2**20)); f.write(chunk); left -= len(chunk)
        q = parse_qs(u.query); args = []
        for k in ('epochs', 'batch', 'lr', 'hold'):
            if k in q and q[k][0].replace('.', '', 1).isdigit(): args += [f'--{k}', q[k][0]]
        threading.Thread(target=run, args=(job_id, args), daemon=True).start()
        self._send(202, {'id': job_id})
    def log_message(self, fmt, *a): sys.stderr.write(time.strftime('%H:%M:%S ') + fmt % a + '\n')

def _cuda():
    try:
        import torch; return torch.cuda.is_available()
    except Exception: return False

if __name__ == '__main__':
    if not TOKEN: sys.exit('TRAIN_TOKEN fehlt — ohne Token startet der Dienst nicht.')
    os.makedirs(WORK, exist_ok=True)
    ThreadingHTTPServer(('0.0.0.0', int(os.environ.get('PORT', '8000'))), H).serve_forever()

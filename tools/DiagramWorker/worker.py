"""Externer Diagramm-Worker als Container (0.873.0): leiht wartende E-Books über /api/diagram-worker, erkennt die Diagramme
mit dem eingebauten rookhub-diagrams (tools/DiagramCli, derselbe Code wie der Server) und schickt das Ergebnis zurück.

Nur die Diagramm-Erkennung — die Brett-Aufträge über WebDAV bleiben beim Laptop-Worker
(~/claude/ebook-board-training/worker/worker.py, von dem dieser Ablauf stammt). Das Programm liegt im Image, also immer in
der Fassung des Builds; nach einer Änderung an der Erkennung das Image neu bauen (CI: diagram-worker.yml).

Umgebung:
  ROOKHUB_URL         Vorgabe https://rookhub.oberschmid.homes
  ROOKHUB_TOKEN       API-Token mit Bereich „Diagramm-Worker“ (Profil → API-Tokens), oder
  ROOKHUB_TOKEN_FILE  Datei mit dem Token (Docker-Secret); der Token wird nie ausgegeben
  WORKER_NAME         Name im Server-Log (Vorgabe: Rechnername)
  THREADS             Seitenstapel gleichzeitig (Vorgabe: Kerne − 2)
  IDLE_SECONDS        Pause, wenn nichts zu tun ist (Vorgabe 30)
"""
import os, sys, time, socket, shutil, tempfile, threading, subprocess
import requests

CLI = os.environ.get('ROOKHUB_DIAGRAMS', '/app/cli/rookhub-diagrams')
URL = os.environ.get('ROOKHUB_URL', 'https://rookhub.oberschmid.homes').rstrip('/')
NAME = os.environ.get('WORKER_NAME') or socket.gethostname()
THREADS = int(os.environ.get('THREADS') or max(1, (os.cpu_count() or 2) - 2))
IDLE = int(os.environ.get('IDLE_SECONDS') or 30)
WORK = os.path.join(tempfile.gettempdir(), 'diagram-worker')
RH = requests.Session()


def log(msg):
    print(msg, flush=True)


def token():
    path = os.environ.get('ROOKHUB_TOKEN_FILE')
    if path:
        with open(path, encoding='utf-8') as f:
            return f.read().strip()
    return (os.environ.get('ROOKHUB_TOKEN') or '').strip()


def rh(method, path, **kw):
    return RH.request(method, URL + '/' + path.lstrip('/'), timeout=kw.pop('timeout', 120), **kw)


def cli_version():
    r = subprocess.run([CLI, '--version'], capture_output=True, text=True, timeout=60)
    return (r.stdout.strip() or r.stderr.strip()).splitlines()[-1] if r.returncode == 0 else None


def release(job, error=None):
    try:
        rh('POST', f"api/diagram-worker/jobs/{job['jobId']}/release", json={'error': error} if error else {})
    except requests.RequestException:
        pass   # die Leihe läuft ohnehin ab


def build_command(job, paths, out):
    """Aufruf von rookhub-diagrams für einen Auftrag (Rollen wie EbookDiagramRecognizer.Choose)."""
    prim, fmt = paths['primary']
    cmd = [CLI, '--ebook-id', str(job['ebookId']), '--primary', prim, '--format', fmt, '--threads', str(THREADS), '--out', out]
    if 'reading' in paths:
        cmd += ['--reading', paths['reading'][0]]
    if 'epub' in paths:
        cmd += ['--epub', paths['epub'][0]]
    if job.get('supplement'):
        cmd.append('--supplement')
    return cmd


def pause_for(status):
    """Wie lange nach einer Antwort von /claim warten (None = Auftrag bearbeiten)."""
    if status == 200:
        return None
    if status == 204:
        return IDLE
    if status in (401, 403, 404):
        return 3600   # Token falsch/ohne Bereich bzw. Server vor 0.870.0 — nicht im Sekundentakt nachfragen
    return 300


def run_job(job, version):
    eid = job['ebookId']
    t0 = time.time()
    work = os.path.join(WORK, f'd{eid}')
    shutil.rmtree(work, ignore_errors=True)
    os.makedirs(work)
    lost, stop = threading.Event(), threading.Event()

    def heartbeat():
        while not stop.wait(max(60, job.get('heartbeatSeconds', 600))):
            try:
                if rh('POST', f"api/diagram-worker/jobs/{job['jobId']}/heartbeat").status_code in (404, 409):
                    lost.set()
                    return
            except requests.RequestException:
                pass

    threading.Thread(target=heartbeat, daemon=True).start()
    proc = None
    try:
        log(f"Buch {eid} „{job.get('title', '')}“ ({len(job['files'])} Dateien) …")
        paths = {}
        for f in job['files']:
            p = os.path.join(work, f"{f['role']}.{f['format']}")
            with rh('GET', f['url'], stream=True, timeout=600) as d, open(p, 'wb') as out:
                d.raise_for_status()
                for chunk in d.iter_content(1 << 20):
                    out.write(chunk)
            paths[f['role']] = (p, f['format'])
        result = os.path.join(work, 'result.json')
        with open(os.path.join(work, 'cli.log'), 'w', encoding='utf-8', errors='replace') as cli_log:
            proc = subprocess.Popen(build_command(job, paths, result), stdout=cli_log, stderr=subprocess.STDOUT)
            while proc.poll() is None:
                if lost.wait(5):
                    proc.kill()
                    proc.wait()
                    log(f'Buch {eid}: Leihe verloren, abgebrochen')
                    return
        if proc.returncode != 0:
            with open(os.path.join(work, 'cli.log'), encoding='utf-8', errors='replace') as f:
                tail = f.read()[-600:]
            release(job, f'rookhub-diagrams exit {proc.returncode} ({version}): {tail}')
            log(f'Buch {eid}: Fehler {proc.returncode} — zurück an den Server\n{tail}')
            return
        with open(result, 'rb') as res:
            r = rh('POST', f"api/diagram-worker/jobs/{job['jobId']}/result", data=res, timeout=900,
                   headers={'Content-Type': 'application/json'})
        if r.status_code == 200:
            a = r.json()
            log(f"Buch {eid}: {a['diagramCount']} Diagramme ({a['state']}) in {time.time() - t0:.0f} s")
        elif r.status_code == 400:
            release(job, f'Ergebnis abgelehnt: {r.text[:400]}')
            log(f'Buch {eid}: Ergebnis abgelehnt {r.text[:300]}')
        else:
            log(f'Buch {eid}: Ergebnis nicht angenommen ({r.status_code}, Leihe weg?)')
    except BaseException as e:   # auch SIGTERM beim Stoppen des Containers: Buch sofort freigeben
        if proc and proc.poll() is None:
            proc.kill()
        gone = isinstance(e, requests.HTTPError) and e.response is not None and e.response.status_code == 404
        release(job, f'Datei fehlt auf dem Server: {e}' if gone else None)
        if isinstance(e, (KeyboardInterrupt, SystemExit)):
            raise
        log(f'Buch {eid}: {type(e).__name__} {e} — freigegeben')
        time.sleep(30)
    finally:
        stop.set()
        shutil.rmtree(work, ignore_errors=True)


def main():
    import signal
    signal.signal(signal.SIGTERM, lambda *_: sys.exit(0))   # docker stop → finally/except geben das Buch frei
    tok = token()
    if not tok:
        sys.exit('ROOKHUB_TOKEN bzw. ROOKHUB_TOKEN_FILE fehlt (Token mit Bereich „Diagramm-Worker“, Profil → API-Tokens)')
    if shutil.which('pdftoppm') is None:
        sys.exit('pdftoppm fehlt (Poppler) — das Image ist unvollständig')
    version = cli_version()
    if not version:
        sys.exit(f'{CLI} startet nicht')
    RH.headers['Authorization'] = 'Bearer ' + tok
    log(f'{NAME}: rookhub-diagrams {version}, {THREADS} Threads, Server {URL}')
    while True:
        try:
            r = rh('POST', 'api/diagram-worker/claim', json={'worker': NAME, 'cliVersion': version})
        except requests.RequestException as e:
            log(f'RookHub nicht erreichbar ({type(e).__name__}) — 5 min Pause')
            time.sleep(300)
            continue
        wait = pause_for(r.status_code)
        if wait is None:
            run_job(r.json(), version)
            continue
        if r.status_code != 204:
            log(f'claim {r.status_code} — {wait // 60} min Pause')
        time.sleep(wait)


if __name__ == '__main__':
    main()

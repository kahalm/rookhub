#!/usr/bin/env python3
"""Partiebestand der Spielervorbereitung einspielen: PGN (auch aus einem 7z gestreamt) paketweise an
POST /api/prep/admin/games schicken (Recht prep.manage).

Nichts wird entpackt: ein 7z-Archiv wird mit `7z x -so` gelesen — lokal, oder (ohne 7z auf dem Host) über das
Docker-Image rookhub-explorer:latest. Megabase und Lumbra sind solide Archive, gelesen wird also immer am Stück.

Pakete sind fortlaufend nummeriert: Paket N = Partien [N*size, (N+1)*size) der Quelle. Vor dem Lauf fragt das
Skript den Server, welche Pakete es schon gibt (GET /api/prep/admin/imports), und überspringt sie — ein
abgebrochener Lauf setzt so einfach neu an. Ein doppelt geschicktes Paket verbucht der Server ohnehin nicht zweimal.

    # Stichprobe: jedes 23. Paket der Megabase
    ROOKHUB_PASSWORD=… python3 scripts/prep-import.py --api https://rookhub.example --user admin \\
        --source Mega --every 23 /srv/media/sftp/kahalm2/dumps/megabase.7z.001 --member MegaBasePGN.pgn

    # ohne Server: Pakete nur zählen bzw. als .pgn.gz ablegen
    python3 scripts/prep-import.py --source Lumbra --dry-run LumbrasGigaBase_OTB_Complete.7z

Drosseln: --rate (Anfragen je Minute, Vorgabe 60; die Prod-API erlaubt 100/min je IP, der Login zählt mit).
Bei 429 wartet das Skript (Retry-After), bei 5xx/Verbindungsfehlern versucht es es mit wachsender Pause erneut.
"""
import argparse
import fcntl
import gzip
import json
import os
import re
import select
import shlex
import signal
import shutil
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request

# Nur diese Kopfzeilen gehen an den Server — der Rest (ChessBase-Quellenangaben, „Beauty", GameId …) kostet nur Bytes.
KEEP_HEADERS = {
    b"Event", b"Site", b"Date", b"Round", b"White", b"Black", b"Result", b"WhiteElo", b"BlackElo",
    b"WhiteFideId", b"BlackFideId", b"ECO", b"FEN", b"SetUp", b"Variant",
}
HEADER = re.compile(rb'^\[([A-Za-z][A-Za-z0-9_]*)\s+"(.*)"\s*\]\s*$')
BOM = b"\xef\xbb\xbf"


def strip_comments(s, in_comment):
    """Kommentare aus einer Zugzeile entfernen → (Rest, noch im Kommentar?). {…} verschachtelt laut PGN-Spezifikation
    NICHT: die erste „}" schließt, eine „{" im Kommentar ist Text. Lumbra führt genau so eine Partie
    („Qb1 { {time | (time } 1-0", Zeile 31 859 533) — wer Klammern zählt, hält danach jede Kopfzeile der Datei für
    Kommentar. „;" kommentiert den Rest der Zeile aus."""
    if not in_comment and b"{" not in s and b";" not in s:
        return s, False
    out = []
    i, n = 0, len(s)
    while i < n:
        if in_comment:
            j = s.find(b"}", i)
            if j < 0:
                return b"".join(out), True
            i, in_comment = j + 1, False
            continue
        j, k = s.find(b"{", i), s.find(b";", i)
        if k >= 0 and (j < 0 or k < j):
            out.append(s[i:k])
            return b"".join(out), False
        if j < 0:
            out.append(s[i:])
            break
        out.append(s[i:j])
        out.append(b" ")
        i, in_comment = j + 1, True
    return b"".join(out), in_comment


def split_games(lines, want):
    """Zerlegt einen PGN-Strom in Partien → (Nummer, (Kopfzeilen, Zugzeilen)) bzw. (Nummer, None), wenn want(Nummer)
    die Partie nicht braucht. Grenzen wie PgnParser.SplitGamesCore auf dem Server: eine Kopfzeile nach Zugtext oder
    ein wiederholter Kopf-Schlüssel beginnt die nächste Partie; Zeilen in einem offenen {Kommentar} sind kein Kopf —
    außer eine exakte Kopfzeile direkt nach einer Leerzeile: dann war der Kommentar nie geschlossen, und eine einzige
    kaputte Partie verschluckte sonst den Rest der Datei. Kommentare gehen nicht mit (der Server verwirft sie ohnehin)."""
    idx = 0
    heads, moves, keys = [], [], set()
    in_moves = has = False
    keep = want(0)
    in_comment = False
    blank = True
    first = True
    for raw in lines:
        line = raw.rstrip(b"\r\n")
        if first:
            first = False
            if line.startswith(BOM):
                line = line[3:]
        s = line.strip()
        if not s:
            blank = True
            continue
        after_blank, blank = blank, False
        m = HEADER.match(s) if s[:1] == b"[" and (not in_comment or after_blank) else None
        if m:
            in_comment = False
            key = m.group(1)
            if in_moves or key in keys:
                if has:
                    yield idx, ((heads, moves) if keep else None)
                    idx += 1
                    keep = want(idx)
                heads, moves, keys = [], [], set()
                in_moves = False
            keys.add(key)
            has = True
            if keep and key in KEEP_HEADERS:
                heads.append(s)
        elif not in_comment and s[:1] == b"[":
            continue                       # Tag-artige Zeile außerhalb eines Kommentars: wie der Server ignorieren
        else:
            in_moves = has = True
            s, in_comment = strip_comments(s, in_comment)
            s = s.strip()
            if keep and s:
                moves.append(s)
    if has:
        yield idx, ((heads, moves) if keep else None)


def chunk_text(games):
    out = []
    for heads, moves in games:
        out.append(b"\n".join(heads))
        out.append(b"\n\n")
        out.append(b"\n".join(moves))
        out.append(b"\n\n")
    return b"".join(out)


class Chunker:
    """Fasst die Partien zu Paketen zusammen; liefert (Paket, erste Partie, Partien) nur für ausgewählte Pakete."""

    def __init__(self, size, selected):
        self.size = size
        self.selected = selected
        self.seen = 0          # Partien der Quelle bis hierher, ausgewählt oder nicht

    def want(self, idx):
        return self.selected(idx // self.size)

    def chunks(self, lines):
        cur, cur_no = [], None
        for idx, game in split_games(lines, self.want):
            self.seen = idx + 1
            no = idx // self.size
            if cur_no is not None and no != cur_no:
                if cur:
                    yield cur_no, cur_no * self.size, cur
                cur = []
            cur_no = no
            if game is not None:
                cur.append(game)
        if cur_no is not None and cur:
            yield cur_no, cur_no * self.size, cur


def open_input(path, member, image):
    """→ (Zeilen-Iterator, Aufräumen). Ein 7z-Archiv wird gestreamt: mit lokalem 7z über eine Pipe; ohne über Docker,
    das in eine Named Pipe (FIFO) schreibt — die angehängte Ausgabe von `docker run` liefe durch dockerd und schaffte
    dort nur einen Bruchteil (gemessen 2026-10-01: dockerd bei ~140 % CPU, 7z und dieses Skript warteten)."""
    if path == "-":
        return sys.stdin.buffer, lambda: None
    if not path.endswith((".7z", ".7z.001")):
        f = open(path, "rb")
        return f, f.close
    if shutil.which("7z"):
        proc = subprocess.Popen(["7z", "x", "-so", path] + ([member] if member else []),
                                stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, bufsize=1 << 20)
        return proc.stdout, lambda: _stop(proc, proc.stdout)

    folder, name = os.path.split(os.path.abspath(path))
    tmp = tempfile.mkdtemp(prefix="prep-import-")
    fifo = os.path.join(tmp, "pgn")
    os.mkfifo(fifo, 0o600)
    container = f"prep-import-7z-{os.getpid()}"
    inner = "7z x -so " + shlex.quote("/d/" + name) + (" " + shlex.quote(member) if member else "") + " > /out/pgn"
    proc = subprocess.Popen(["docker", "run", "--rm", "--name", container, "-v", f"{folder}:/d:ro", "-v", f"{tmp}:/out",
                             "--entrypoint", "sh", image, "-c", inner],
                            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

    def cleanup(stream=None):
        _stop(proc, stream)
        subprocess.run(["docker", "rm", "-f", container], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=False)
        shutil.rmtree(tmp, ignore_errors=True)

    # Lesend öffnen, ohne zu blockieren, und warten, bis 7z schreibt — endet der Container vorher, ist das ein Fehler
    # statt eines ewig hängenden open().
    fd = os.open(fifo, os.O_RDONLY | os.O_NONBLOCK)
    while not select.select([fd], [], [], 1.0)[0]:
        if proc.poll() is not None:
            os.close(fd)
            cleanup()
            raise RuntimeError(f"7z im Container {image} endete sofort (Exit {proc.returncode})")
    fcntl.fcntl(fd, fcntl.F_SETFL, fcntl.fcntl(fd, fcntl.F_GETFL) & ~os.O_NONBLOCK)
    stream = os.fdopen(fd, "rb", buffering=1 << 20)
    return stream, lambda: cleanup(stream)


def _stop(proc, stream):
    if stream is not None:
        try:
            stream.close()
        except OSError:
            pass
    proc.terminate()
    try:
        proc.wait(timeout=30)
    except subprocess.TimeoutExpired:
        proc.kill()


class ApiError(Exception):
    pass


class Api:
    def __init__(self, base, token=None, user=None, password=None, rate=60, retries=5, timeout=300,
                 sleep=time.sleep, opener=urllib.request.urlopen):
        self.base = base.rstrip("/")
        self.token = token
        self.user = user
        self.password = password
        self.min_gap = 60.0 / rate if rate > 0 else 0
        self.retries = retries
        self.timeout = timeout
        self.sleep = sleep
        self.opener = opener
        self.last = None
        self.requests = 0

    def _throttle(self):
        if self.last is not None and self.min_gap:
            wait = self.min_gap - (time.monotonic() - self.last)
            if wait > 0:
                self.sleep(wait)
        self.last = time.monotonic()
        self.requests += 1

    def _send(self, method, path, body=None, headers=None, auth=True):
        url = self.base + path
        delay = 5
        for attempt in range(self.retries + 1):
            self._throttle()
            h = dict(headers or {})
            if auth and self.token:
                h["Authorization"] = "Bearer " + self.token
            req = urllib.request.Request(url, data=body, headers=h, method=method)
            try:
                with self.opener(req, timeout=self.timeout) as resp:
                    return json.loads(resp.read().decode("utf-8") or "null")
            except urllib.error.HTTPError as e:
                text = e.read().decode("utf-8", "replace")[:500]
                if e.code == 429 and attempt < self.retries:
                    wait = int(e.headers.get("Retry-After") or 60)
                    print(f"  429 — warte {wait} s", file=sys.stderr, flush=True)
                    self.sleep(wait)
                    continue
                if e.code == 401 and auth and self.user and attempt < self.retries:
                    self.login()
                    continue
                if e.code >= 500 and attempt < self.retries:
                    print(f"  {e.code} — neuer Versuch in {delay} s", file=sys.stderr, flush=True)
                    self.sleep(delay)
                    delay = min(delay * 2, 300)
                    continue
                raise ApiError(f"{method} {path}: HTTP {e.code} {text}")
            except (urllib.error.URLError, TimeoutError, ConnectionError) as e:
                if attempt < self.retries:
                    print(f"  {e} — neuer Versuch in {delay} s", file=sys.stderr, flush=True)
                    self.sleep(delay)
                    delay = min(delay * 2, 300)
                    continue
                raise ApiError(f"{method} {path}: {e}")
        raise ApiError(f"{method} {path}: aufgegeben")

    def login(self):
        body = json.dumps({"username": self.user, "password": self.password}).encode()
        res = self._send("POST", "/api/auth/login", body, {"Content-Type": "application/json"}, auth=False)
        self.token = res["token"]

    def done_chunks(self, source):
        res = self._send("GET", "/api/prep/admin/imports?source=" + urllib.parse.quote(source))
        return {c["chunk"]: c["first"] for c in res["chunks"]}

    def post_chunk(self, source, chunk, first, payload):
        q = urllib.parse.urlencode({"source": source, "chunk": chunk, "first": first})
        return self._send("POST", "/api/prep/admin/games?" + q, payload,
                          {"Content-Type": "application/x-chess-pgn", "Content-Encoding": "gzip"})


def parse_args(argv):
    p = argparse.ArgumentParser(description="Partiebestand (Megabase/Lumbra) paketweise in RookHub einspielen")
    p.add_argument("input", help="PGN-Datei, 7z-Archiv (.7z / .7z.001) oder - für stdin")
    p.add_argument("--source", required=True, choices=["Mega", "Lumbra"])
    p.add_argument("--member", help="Datei im Archiv (z. B. MegaBasePGN.pgn)")
    p.add_argument("--docker-image", default="rookhub-explorer:latest", help="Image mit 7z, falls 7z auf dem Host fehlt")
    p.add_argument("--api", help="Basis-URL der RookHub-API (ohne /api)")
    p.add_argument("--token", default=os.environ.get("ROOKHUB_TOKEN"), help="JWT (sonst --user + ROOKHUB_PASSWORD)")
    p.add_argument("--user")
    p.add_argument("--chunk-size", type=int, default=5000)
    p.add_argument("--every", type=int, default=1, help="nur jedes k-te Paket (Stichprobe quer durch die Datei)")
    p.add_argument("--offset", type=int, default=0, help="mit --every: welches der k Pakete")
    p.add_argument("--start-chunk", type=int, default=0)
    p.add_argument("--max-chunks", type=int, default=0, help="nach so vielen geschickten Paketen aufhören (0 = alle)")
    p.add_argument("--rate", type=float, default=60, help="höchstens so viele Anfragen je Minute")
    p.add_argument("--retries", type=int, default=5)
    p.add_argument("--timeout", type=float, default=300, help="Sekunden je Anfrage (hinter nginx gelten dessen 60 s)")
    p.add_argument("--save-dir", help="Pakete als .pgn.gz ablegen statt schicken")
    p.add_argument("--dry-run", action="store_true", help="nur zählen")
    p.add_argument("--log", help="je Paket eine JSON-Zeile (Zeiten, Zähler) in diese Datei")
    a = p.parse_args(argv)
    if a.chunk_size < 1 or a.every < 1 or not 0 <= a.offset < a.every:
        p.error("--chunk-size/--every/--offset ungültig")
    if not (a.dry_run or a.save_dir or a.api):
        p.error("--api fehlt (oder --dry-run / --save-dir)")
    return a


def run(a, api=None, lines=None, out=sys.stdout):
    def selected(no):
        return no >= a.start_chunk and (no - a.offset) % a.every == 0

    done = {}
    if api is not None:
        if not api.token and a.user:
            api.login()
        done = api.done_chunks(a.source)
        bad = [c for c, f in done.items() if f != c * a.chunk_size]
        if bad:
            raise ApiError(f"Pakete {bad[:5]} stehen mit anderer Paketgröße auf dem Server — --chunk-size wie beim ersten Lauf")
        if done:
            print(f"{len(done)} Pakete schon eingespielt — werden übersprungen", file=out, flush=True)

    cleanup = None
    if lines is None:
        lines, cleanup = open_input(a.input, a.member, a.docker_image)
    logf = open(a.log, "a", encoding="utf-8") if a.log else None
    totals = {"chunks": 0, "games": 0, "read": 0, "added": 0, "duplicates": 0, "discarded": 0, "bytes": 0, "server_ms": 0}
    started = time.monotonic()
    chunker = Chunker(a.chunk_size, lambda n: selected(n) and n not in done)
    try:
        for no, first, games in chunker.chunks(lines):
            text = chunk_text(games)
            payload = gzip.compress(text, 6)
            totals["chunks"] += 1
            totals["games"] += len(games)
            totals["bytes"] += len(payload)
            t0 = time.monotonic()
            res = None
            if a.save_dir:
                os.makedirs(a.save_dir, exist_ok=True)
                with open(os.path.join(a.save_dir, f"{a.source}-{no:06d}.pgn.gz"), "wb") as f:
                    f.write(payload)
            elif not a.dry_run:
                res = api.post_chunk(a.source, no, first, payload)
                for k in ("read", "added", "duplicates", "discarded"):
                    totals[k] += res[k]
                totals["server_ms"] += res["millis"]
            wall = time.monotonic() - t0
            line = f"Paket {no} (ab {first}): {len(games)} Partien, {len(payload) // 1024} KB"
            if res:
                line += (f" → neu {res['added']}, Dubletten {res['duplicates']}, verworfen {res['discarded']}"
                         f"{' (schon da)' if res['already'] else ''}, Server {res['millis']} ms, gesamt {wall:.1f} s")
            print(line, file=out, flush=True)
            if logf:
                logf.write(json.dumps({"chunk": no, "first": first, "games": len(games), "bytes": len(payload),
                                       "rawBytes": len(text), "wall": round(wall, 3), "result": res}) + "\n")
                logf.flush()
            if a.max_chunks and totals["chunks"] >= a.max_chunks:
                break
    finally:
        if cleanup:
            cleanup()
        if logf:
            logf.close()
    secs = time.monotonic() - started
    totals["seen"] = chunker.seen
    print(f"fertig: {totals['chunks']} Pakete, {totals['games']} Partien (von {chunker.seen} gelesenen der Quelle),"
          f" {totals['bytes'] // 1024} KB gzip in {secs:.0f} s"
          + (f"; neu {totals['added']}, Dubletten {totals['duplicates']}, verworfen {totals['discarded']},"
             f" Server {totals['server_ms'] / 1000:.0f} s" if api is not None and not a.dry_run and not a.save_dir else ""),
          file=out, flush=True)
    return totals


def main(argv=None):
    a = parse_args(argv if argv is not None else sys.argv[1:])
    # Abbruch von außen räumt wie ein normales Ende auf (Container, Named Pipe) — `finally` läuft nur so.
    signal.signal(signal.SIGTERM, lambda *_: sys.exit(143))
    api = None
    if a.api and not a.dry_run and not a.save_dir:
        api = Api(a.api, token=a.token, user=a.user, password=os.environ.get("ROOKHUB_PASSWORD"), rate=a.rate, retries=a.retries,
                  timeout=a.timeout)
        if not api.token and not (a.user and api.password):
            print("Anmeldung fehlt: --token/ROOKHUB_TOKEN oder --user mit ROOKHUB_PASSWORD", file=sys.stderr)
            return 2
    try:
        run(a, api)
    except ApiError as e:
        print(f"Abbruch: {e} — derselbe Aufruf setzt beim ersten fehlenden Paket fort", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())

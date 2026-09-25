#!/usr/bin/env python3
"""Messung des eigenen Engine-Brokers mit dem ECHTEN Provider — Helfer für rookhub-broker.e2e.sh.

Alles läuft über das Frontend (nginx → API), also denselben Weg wie ein Browser bzw. der Provider.
Tokens werden nur in Dateien mit Modus 600 im Arbeitsverzeichnis abgelegt und NIE ausgegeben.

  setup    Konto anlegen (Browser-Login) + API-Token mit Bereich „Engine"
  wait     warten, bis N Engines direkt angemeldet und online sind
  measure  Live-Messung (erste Zeile, pvs, bestmove, zweiter Auftrag) + Last mit den
           Hintergrund-Engines (Auftrag an Auftrag) — Ergebnis als JSON, Exit ≠ 0 bei Verstoß

Gegen eine andere Umgebung (z. B. Dev nach dem Ausrollen): statt `setup` die Datei `jwt` (Browser-Anmeldung
des Kontos, dem die Engines gehoeren) im Arbeitsordner ablegen (Modus 600), dann `wait` und `measure`
mit `--base https://…`. `measure` setzt dabei die Hintergrund-Liste des Kontos auf die direkt angemeldeten
Engines und legt Analyseauftraege an.

Nur Standardbibliothek, außer für die Last: python-chess (`pip install chess`) erzeugt die Stellungen.
"""
import argparse
import http.client
import json
import os
import random
import secrets
import sys
import threading
import time
import urllib.parse

START = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1"


class Api:
    def __init__(self, base, token=None):
        u = urllib.parse.urlparse(base)
        self.https = u.scheme == "https"
        self.host, self.port = u.hostname, u.port or (443 if self.https else 80)
        self.token = token

    def _conn(self, timeout=60):
        # https fuer die Messung gegen Dev (siehe TODO.md „Eigener Engine-Broker ausrollen")
        cls = http.client.HTTPSConnection if self.https else http.client.HTTPConnection
        return cls(self.host, self.port, timeout=timeout)

    def call(self, method, path, body=None, timeout=60):
        conn = self._conn(timeout)
        headers = {"Accept": "application/json"}
        data = None
        if body is not None:
            data = json.dumps(body).encode()
            headers["Content-Type"] = "application/json"
        if self.token:
            headers["Authorization"] = f"Bearer {self.token}"
        conn.request(method, path, body=data, headers=headers)
        res = conn.getresponse()
        raw = res.read()
        conn.close()
        try:
            payload = json.loads(raw) if raw else None
        except ValueError:
            payload = raw.decode(errors="replace")
        return res.status, payload

    def stream_lines(self, path, body, timeout=600):
        """(Status, Generator von (Zeitpunkt, Zeile)) — die Zeilen, sobald sie ankommen."""
        conn = self._conn(timeout)
        headers = {"Content-Type": "application/json", "Accept": "application/x-ndjson"}
        if self.token:
            headers["Authorization"] = f"Bearer {self.token}"
        conn.request("POST", path, body=json.dumps(body).encode(), headers=headers)
        res = conn.getresponse()

        def lines():
            try:
                while True:
                    raw = res.readline()
                    if not raw:
                        return
                    line = raw.decode(errors="replace").strip()
                    if line:
                        yield time.monotonic(), line
            finally:
                conn.close()

        return res.status, lines()


def read_secret(path):
    with open(path, encoding="utf-8") as f:
        return f.read().strip()


def write_secret(path, value):
    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
    with os.fdopen(fd, "w", encoding="utf-8") as f:
        f.write(value)


def cmd_setup(args):
    api = Api(args.base)
    username = "broker" + secrets.token_hex(4)
    password = "Broker-E2E-" + secrets.token_urlsafe(12)
    status, auth = api.call("POST", "/api/auth/register", {"username": username, "password": password, "email": None})
    if status not in (200, 201) or not isinstance(auth, dict) or not auth.get("token"):
        sys.exit(f"Registrierung fehlgeschlagen: HTTP {status}")
    browser = Api(args.base, auth["token"])
    status, created = browser.call("POST", "/api/profile/tokens", {"name": "Engine-Provider E2E", "scope": "engine"})
    if status != 200 or not isinstance(created, dict) or created.get("scope") != "engine":
        sys.exit(f"API-Token anlegen fehlgeschlagen: HTTP {status}")
    write_secret(os.path.join(args.workdir, "jwt"), auth["token"])
    write_secret(os.path.join(args.workdir, "engine-token"), created["rawToken"])
    print(f"Konto {username} angelegt, API-Token mit Bereich engine erzeugt")


def cmd_wait(args):
    browser = Api(args.base, read_secret(os.path.join(args.workdir, "jwt")))
    deadline = time.monotonic() + args.timeout
    while time.monotonic() < deadline:
        status, listing = browser.call("GET", "/api/engine/external")
        if status == 200:
            online = [e for e in listing["engines"] if e.get("source") == "rookhub" and e.get("online")]
            if len(online) >= args.count:
                with open(os.path.join(args.workdir, "engines.json"), "w", encoding="utf-8") as f:
                    json.dump(listing["engines"], f)
                print(f"{len(online)} Engines direkt angemeldet und online")
                return
        time.sleep(1)
    sys.exit(f"Nach {args.timeout} s nicht {args.count} Engines online")


def analyse(api, engine_id, depth, multipv, fen=START, moves=()):
    """Eine Live-Analyse wie das Analysebrett; Zeiten relativ zum Absenden."""
    t0 = time.monotonic()
    status, lines = api.stream_lines(f"/api/engine/external/{engine_id}/analyse", {
        "sessionId": "e2e-" + secrets.token_hex(3), "initialFen": fen, "moves": list(moves),
        "multiPv": multipv, "depth": depth,
    })
    result = {"status": status, "first": None, "end": None, "lines": 0, "pvLines": 0, "keepalives": 0,
              "bestmove": None, "maxDepth": 0}
    if status != 200:
        return result
    last = None
    for t, line in lines:
        rel = t - t0
        if result["first"] is None:
            result["first"] = rel
        result["lines"] += 1
        obj = json.loads(line)
        if "keepalive" in obj:
            result["keepalives"] += 1
            continue
        if isinstance(obj.get("pvs"), list) and obj["pvs"]:
            result["pvLines"] += 1
            result["maxDepth"] = max(result["maxDepth"], obj.get("depth", 0))
        last = obj
        result["end"] = rel
    if last is not None:
        result["bestmove"] = last.get("bestmove")
    return result


def random_fens(n, seed):
    import chess  # python-chess
    rng = random.Random(seed)
    seen, out = set(), []
    while len(out) < n:
        board = chess.Board()
        for _ in range(rng.randint(6, 24)):
            moves = list(board.legal_moves)
            if not moves:
                break
            board.push(rng.choice(moves))
        if board.is_game_over():
            continue
        key = " ".join(board.fen().split(" ")[:4])
        if key in seen:
            continue
        seen.add(key)
        out.append(board.fen())
    return out


def cmd_measure(args):
    browser = Api(args.base, read_secret(os.path.join(args.workdir, "jwt")))
    with open(os.path.join(args.workdir, "engines.json"), encoding="utf-8") as f:
        engines = [e for e in json.load(f) if e.get("source") == "rookhub"]
    primary = next(e for e in engines if e["name"] == args.primary)
    background = [e["id"] for e in engines if e["id"] != primary["id"]]
    report = {"engines": len(engines), "background": len(background)}
    failures = []

    # --- 1) Live: erste Zeile < 1 s, pvs, bestmove; danach zweiter Auftrag < 1 s nach dem Ende -----------
    first = analyse(browser, primary["id"], args.live_depth, 3)
    second = analyse(browser, primary["id"], args.live_depth, 3, moves=["e2e4"])
    report["live1"], report["live2"] = first, second
    for name, r in (("live1", first), ("live2", second)):
        if r["status"] != 200:
            failures.append(f"{name}: HTTP {r['status']}")
            continue
        if r["first"] is None or r["first"] >= 1.0:
            failures.append(f"{name}: erste Zeile nach {r['first']} s (Soll < 1 s)")
        if r["pvLines"] == 0:
            failures.append(f"{name}: keine Zeile mit pvs")
        if not r["bestmove"]:
            failures.append(f"{name}: letzte Zeile ohne bestmove")

    # --- 2) Last: die Hintergrund-Engines rechnen Auftrag an Auftrag -----------------------------------------
    status, _ = browser.call("PUT", "/api/engine/background", {"engineIds": background})
    if status != 200:
        failures.append(f"Hintergrund-Engines setzen: HTTP {status}")
    fens = random_fens(2000, args.seed)
    created, create_errors = [], {}
    done_ids, failed_ids = set(), set()
    live_probes = []
    stop = threading.Event()

    def feed():
        i = 0
        while not stop.is_set():
            st, jobs = browser.call("GET", "/api/analysis-jobs")
            if st == 200:
                open_jobs = [j for j in jobs if str(j["status"]).lower() in ("queued", "running", "paused")]
                for j in jobs:
                    if str(j["status"]).lower() == "done":
                        done_ids.add(j["id"])
                    elif str(j["status"]).lower() == "failed":
                        failed_ids.add(j["id"])
                # je Engine zwei offene Aufträge: einer rechnet, einer wartet — kein Leerlauf außer dem Tick
                for _ in range(max(0, 2 * len(background) - len(open_jobs))):
                    st2, dto = browser.call("POST", "/api/analysis-jobs", {
                        "fen": fens[i % len(fens)], "targetDepth": args.job_depth, "multiPv": args.job_multipv,
                        "title": f"e2e-{i}"})
                    i += 1
                    if st2 in (200, 201):
                        created.append(dto["id"])
                    else:
                        create_errors[st2] = create_errors.get(st2, 0) + 1
            stop.wait(2)

    def probe():
        # Live neben der Last: der Primär-Engine gehört niemandem sonst — die erste Zeile muss trotzdem
        # sofort kommen (13 Provider pollen gleichzeitig).
        while not stop.is_set():
            live_probes.append(analyse(browser, primary["id"], 14, 1, fen=random.choice(fens)))
            stop.wait(8)

    threads = [threading.Thread(target=feed, daemon=True), threading.Thread(target=probe, daemon=True)]
    t_load = time.monotonic()
    for t in threads:
        t.start()
    time.sleep(args.load_seconds)
    stop.set()
    for t in threads:
        t.join(timeout=120)
    report["load"] = {
        "seconds": round(time.monotonic() - t_load, 1),
        "jobsCreated": len(created),
        "jobsDone": len(done_ids & set(created)),
        "jobsFailed": len(failed_ids & set(created)),
        "createErrors": create_errors,
        "liveProbes": len(live_probes),
        "liveProbeFirstLineMax": max((p["first"] or 99) for p in live_probes) if live_probes else None,
        "liveProbeFirstLineAvg": (sum((p["first"] or 99) for p in live_probes) / len(live_probes)) if live_probes else None,
        "liveProbeNon200": sum(1 for p in live_probes if p["status"] != 200),
        "liveProbeWithoutBestmove": sum(1 for p in live_probes if p["status"] == 200 and not p["bestmove"]),
    }
    if report["load"]["jobsFailed"]:
        failures.append(f"{report['load']['jobsFailed']} Aufträge gescheitert")
    if report["load"]["liveProbeNon200"]:
        failures.append(f"{report['load']['liveProbeNon200']} Live-Proben nicht 200")
    if create_errors:
        failures.append(f"Aufträge anlegen: {create_errors}")

    # Stand der Aufträge je Engine (wer hat gerechnet?)
    st, jobs = browser.call("GET", "/api/analysis-jobs")
    per_engine = {}
    if st == 200:
        for j in jobs:
            if j["id"] in done_ids:
                per_engine[j.get("engineId")] = per_engine.get(j.get("engineId"), 0) + 1
    report["load"]["doneJobsPerEngine"] = sorted(per_engine.values(), reverse=True)
    report["failures"] = failures
    print(json.dumps(report, indent=2, ensure_ascii=False))
    sys.exit(1 if failures else 0)


def main():
    p = argparse.ArgumentParser()
    sub = p.add_subparsers(dest="cmd", required=True)
    for name in ("setup", "wait", "measure"):
        s = sub.add_parser(name)
        s.add_argument("--base", required=True)
        s.add_argument("--workdir", required=True)
        if name == "wait":
            s.add_argument("--count", type=int, required=True)
            s.add_argument("--timeout", type=int, default=240)
        if name == "measure":
            s.add_argument("--primary", required=True)
            s.add_argument("--live-depth", type=int, default=18)
            s.add_argument("--job-depth", type=int, default=20)
            s.add_argument("--job-multipv", type=int, default=2)
            s.add_argument("--load-seconds", type=int, default=180)
            s.add_argument("--seed", type=int, default=20260925)
    args = p.parse_args()
    {"setup": cmd_setup, "wait": cmd_wait, "measure": cmd_measure}[args.cmd](args)


if __name__ == "__main__":
    main()

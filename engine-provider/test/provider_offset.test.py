#!/usr/bin/env python3
"""Versatz und Stillstand nach einem abgerissenen Upload — gegen einen nachgebauten Broker.

    python3 test/provider_offset.test.py /tmp/provider.py                      # Schein-Engine
    python3 test/provider_offset.test.py /tmp/provider.py --engine stockfish   # echte Engine
    python3 test/provider_offset.test.py /tmp/provider.py --mode reset --runs 5 --keep-going

Fehlerbild auf Prod (16./17.11.2026): nach einem API-Neustart lieferte jeder Auftrag die Zeilen der
VORIGEN Stellung samt deren `bestmove` und endete sofort — bis der Provider-Prozess endete. Dazu
Auftraege, die 30 min lang nur noch `{"keepalive":true}` schickten (Stillstand mitten in der Suche).

Ursache (siehe README, „Abgerissener Upload"): `Engine.analyse` ist ein Async-Generator, den aiohttp als
Upload-Rumpf verbraucht. Reisst der Upload ab, haengt die Aufraeumarbeit (`stop` + bis `bestmove` lesen)
an dessen `finally` — und das laeuft zu spaet, gar nicht oder wird mittendrin abgebrochen. Der Test
erwartet den GEPATCHTEN Provider (`patch_force_close.py` + `patch_search_owner.py`); ohne den zweiten
Patch ist er rot.

Ablauf je Lauf: Auftrag 1 (lange Suche) bricht nach einigen Zeilen ab — je nach `--mode`:
  reset  der Broker kappt die Verbindung (TCP-Abbruch) mitten im Upload
  early  der Broker antwortet 502 mitten im Upload, ohne den Rumpf fertig zu lesen (nginx vor einer
         API, die gerade neu startet)
  down   der ganze Broker ist weg (Verbindungen gekappt, Port zu) und kommt nach 3 s wieder
  gc     wie `reset`, dazu startet der Test den Provider mit abgeschalteter Muellabfuhr (GC_WRAPPER unten)
         und loest sie erst MITTEN in einem spaeteren Auftrag aus (`--gc-job`, `--gc-lines`) — so laeuft
         das `finally` des verlassenen Generators dort, wo es auf Prod irgendein Muelllauf tut
Danach die Auftraege 2 … N, abwechselnd mit Weiss und Schwarz am Zug. Erwartung: jeder Upload endet
mit `bestmove`, jede `info`-Zeile und das `bestmove` beginnen auf einem Feld der Seite am Zug des
EIGENEN Auftrags, und kein Auftrag haengt.

Die Sitzungswechsel-Pruefung (`--session-check`) ist eine Messung, kein Pflichtteil: sie laesst den
Provider erst versetzt laufen und prueft, ob ein Auftrag mit geaenderter `sessionId` (→ `ucinewgame` +
`isready`) ihn wieder in den Tritt bringt.
"""
import argparse
import asyncio
import logging
import os
import shlex
import sys
import tempfile
import time

from aiohttp import web

KEEPALIVE = '{"keepalive":true}'
HERE = os.path.dirname(os.path.abspath(__file__))
POS_W = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1"
POS_B = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1"

# Startet den Provider mit abgeschalteter zyklischer Muellabfuhr: ein Generator in einem Referenzzyklus
# wird dann erst bei gc.collect() finalisiert. Den Zeitpunkt bestimmt der Test (Datei-Signal), damit das
# spaete `finally` reproduzierbar MITTEN in einem spaeteren Auftrag laeuft — auf Prod tut das irgendein
# Muelllauf zu einem beliebigen Zeitpunkt.
GC_WRAPPER = r'''
import asyncio, gc, os, runpy, sys
gc.disable()
signal = os.environ["GC_SIGNAL"]
_orig_run = asyncio.run
def run(coro, **kw):
    async def wrapped():
        async def collector():
            while True:
                await asyncio.sleep(0.02)
                if os.path.exists(signal):
                    os.remove(signal)
                    gc.collect()
        t = asyncio.create_task(collector())
        try:
            return await coro
        finally:
            t.cancel()
    return _orig_run(wrapped(), **kw)
asyncio.run = run
sys.argv = sys.argv[1:]
runpy.run_path(sys.argv[0], run_name="__main__")
'''


def own_squares(fen):
    board, side = fen.split()[0], fen.split()[1]
    squares = set()
    for r, row in enumerate(board.split("/")):
        f = 0
        for ch in row:
            if ch.isdigit():
                f += int(ch)
                continue
            if (ch.isupper() and side == "w") or (ch.islower() and side == "b"):
                squares.add("abcdefgh"[f] + str(8 - r))
            f += 1
    return squares


def first_move(line):
    words = line.split()
    if words[0] == "bestmove":
        return words[1] if len(words) > 1 else ""
    return words[words.index("pv") + 1] if "pv" in words else ""


class Broker:
    def __init__(self):
        self.jobs = asyncio.Queue()
        self.uploads = {}
        self.abort = {}            # job-id -> (mode, nach so vielen Zeilen)
        self.down_event = asyncio.Event()

    async def list_engines(self, _r):
        return web.json_response([])

    async def register(self, _r):
        return web.json_response({"id": "eei_test"})

    async def acquire(self, _r):
        try:
            return web.json_response(await asyncio.wait_for(self.jobs.get(), timeout=3))
        except asyncio.TimeoutError:
            return web.Response(status=204)

    async def submit(self, request):
        jid = request.match_info["id"]
        up = self.uploads[jid] = {"lines": [], "start": time.monotonic()}
        mode, after = self.abort.get(jid, (None, 0))
        data = 0
        async for raw in request.content:
            line = raw.decode().strip()
            if not line:
                continue
            up["lines"].append(line)
            if line != KEEPALIVE:
                data += 1
            if mode and data >= after:
                up["aborted"] = mode
                up["end"] = time.monotonic()
                if mode == "early":
                    return web.Response(status=502, text="bad gateway")
                if mode == "down":
                    self.down_event.set()
                request.transport.abort()
                raise ConnectionResetError("Test: Verbindung gekappt")
        up["end"] = time.monotonic()
        done = any(l.startswith("bestmove") for l in up["lines"])
        up["status"] = 200 if done else 400
        return web.Response(status=up["status"])


async def start_site(app_runner, port):
    site = web.TCPSite(app_runner, "127.0.0.1", port, reuse_address=True)
    await site.start()
    return site


def job(jid, fen, session, depth=None, movetime=None):
    work = {"sessionId": session, "threads": 1, "hash": 16, "multiPv": 1, "variant": "chess",
            "initialFen": fen, "moves": []}
    if movetime:
        work["movetime"] = movetime
    else:
        work["depth"] = depth
    return {"id": jid, "work": work}


def verdict(up, fen):
    """(ok, Text) fuer einen fertigen Upload."""
    data = [l for l in up["lines"] if l != KEEPALIVE]
    own = own_squares(fen)
    foreign = [l for l in data if first_move(l)[:2] not in own]
    if not data or not data[-1].startswith("bestmove"):
        return False, f"kein bestmove am Ende ({len(data)} Datenzeilen)"
    if foreign:
        return False, f"{len(foreign)}/{len(data)} Zeilen gehoeren zu einer FREMDEN Stellung, z. B. {foreign[-1]!r}"
    return True, f"{len(data)} Zeilen, alle zur eigenen Stellung"


async def one_run(args, src, run_no, log_path):
    broker = Broker()
    app = web.Application()
    app.router.add_get("/api/external-engine", broker.list_engines)
    app.router.add_post("/api/external-engine", broker.register)
    app.router.add_post("/api/external-engine/work", broker.acquire)
    app.router.add_post("/api/external-engine/work/{id}", broker.submit)
    runner = web.AppRunner(app, shutdown_timeout=0.5)
    await runner.setup()
    site = await start_site(runner, 0)
    port = runner.addresses[0][1]
    base = f"http://127.0.0.1:{port}"

    real = args.engine != "fake"
    engine_cmd = args.engine if real else f"exec {shlex.quote(sys.executable)} {shlex.quote(os.path.join(HERE, 'fake_uci_engine.py'))}"
    tmp = tempfile.mkdtemp()
    env = dict(os.environ)
    cmd = [sys.executable]
    gc_signal = os.path.join(tmp, "gc-now")
    if args.mode == "gc":
        wrapper = os.path.join(tmp, "gc_wrapper.py")
        with open(wrapper, "w") as f:
            f.write(GC_WRAPPER)
        cmd += [wrapper]
        env["GC_SIGNAL"] = gc_signal
    cmd += [src, "--engine", engine_cmd, "--name", "Versatztest", "--lichess", base, "--broker", base,
            "--token", "lip_test", "--max-threads", "1", "--max-hash", "16", "--keep-alive", "86400",
            "--log-level", "debug"]

    results = []      # (Auftrag, ok, Text)
    stalls = []
    with open(log_path, "w") as log:
        proc = await asyncio.create_subprocess_exec(*cmd, stdout=log, stderr=log, env=env)
        try:
            long_job = job("j1", POS_W, "s1", movetime=60000) if real else job("j1", POS_W, "s1", depth=100000)
            broker.abort["j1"] = (args.mode if args.mode != "gc" else "reset", 3)
            await broker.jobs.put(long_job)
            t0 = time.monotonic()
            while "end" not in broker.uploads.get("j1", {}):
                if time.monotonic() - t0 > 30:
                    results.append(("j1", False, "Auftrag 1 kam nicht an"))
                    return results, stalls
                await asyncio.sleep(0.02)

            if args.mode == "down":
                await site.stop()
                await runner.shutdown()
                await asyncio.sleep(3)
                await runner.cleanup()
                runner = web.AppRunner(app, shutdown_timeout=0.5)
                await runner.setup()
                site = await start_site(runner, port)
            else:
                await asyncio.sleep(args.pause)

            fens = [POS_B, POS_W]
            session = "s1"
            for k in range(2, args.jobs + 2):
                jid = f"j{k}"
                fen = fens[k % 2]
                if args.session_check and k == args.session_check:
                    session = "s2"
                depth = None if real else args.depth
                jb = job(jid, fen, session, depth=depth, movetime=1500 if real else None)
                await broker.jobs.put(jb)
                if args.mode == "gc" and k == args.gc_job:
                    # mitten in diesem Auftrag: der verlassene Generator von Auftrag 1 wird jetzt finalisiert
                    while len([l for l in broker.uploads.get(jid, {}).get("lines", []) if l != KEEPALIVE]) < args.gc_lines:
                        if "end" in broker.uploads.get(jid, {}):
                            break
                        await asyncio.sleep(0.005)
                    open(gc_signal, "w").close()
                t0 = time.monotonic()
                while "end" not in broker.uploads.get(jid, {}):
                    if time.monotonic() - t0 > args.stall:
                        break
                    await asyncio.sleep(0.02)
                up = broker.uploads.get(jid)
                if up is None:
                    results.append((jid, False, "nie hochgeladen"))
                    continue
                if "end" not in up:
                    data = [l for l in up["lines"] if l != KEEPALIVE]
                    ka = len(up["lines"]) - len(data)
                    stalls.append(jid)
                    results.append((jid, False, f"STILLSTAND: nach {args.stall} s kein bestmove "
                                                f"({len(data)} Datenzeilen, {ka} Lebenszeichen)"))
                    break
                ok, text = verdict(up, fen)
                results.append((jid, ok, f"[{session}] {text}"))
        finally:
            proc.terminate()
            try:
                await asyncio.wait_for(proc.wait(), 5)
            except asyncio.TimeoutError:
                proc.kill()
                await proc.wait()
            await runner.cleanup()
    return results, stalls


async def main():
    p = argparse.ArgumentParser()
    p.add_argument("provider")
    p.add_argument("--engine", default="fake", help="Engine-Befehl, Vorgabe: Schein-Engine")
    p.add_argument("--mode", default="all", choices=["all", "reset", "early", "down", "gc"])
    p.add_argument("--runs", type=int, default=1)
    p.add_argument("--jobs", type=int, default=4, help="Auftraege nach dem Abriss")
    p.add_argument("--pause", type=float, default=0.3, help="Sekunden zwischen Abriss und Auftrag 2")
    p.add_argument("--stall", type=float, default=20, help="so lange darf ein Auftrag ohne bestmove laufen")
    p.add_argument("--session-check", type=int, default=0, metavar="K",
                   help="ab Auftrag K neue sessionId (Messung, ob ein Sitzungswechsel heilt)")
    p.add_argument("--keep-going", action="store_true", help="Fehler zaehlen statt beim ersten Lauf aufzuhoeren")
    p.add_argument("--gc-job", type=int, default=2, help="gc: in welchem Auftrag die Finalisierung laeuft")
    p.add_argument("--gc-lines", type=int, default=2, help="gc: nach so vielen Datenzeilen dieses Auftrags")
    p.add_argument("--depth", type=int, default=12, help="Tiefe der Auftraege nach dem Abriss (Schein-Engine)")
    p.add_argument("--show-log", action="store_true", help="Provider-Log jedes Laufs ausgeben")
    args = p.parse_args()
    # Der absichtlich gekappte Upload ist im Test-Broker eine Ausnahme — deren Stapel interessiert hier nicht.
    logging.getLogger("aiohttp.server").setLevel(logging.CRITICAL)
    if not os.path.exists(args.provider):
        print(f"FEHLER: Provider-Skript {args.provider!r} nicht vorhanden", file=sys.stderr)
        return 1

    modes = ["reset", "early", "down", "gc"] if args.mode == "all" else [args.mode]
    failed_runs = 0
    total = 0
    for mode in modes:
        args_mode = argparse.Namespace(**vars(args))
        args_mode.mode = mode
        for run in range(1, args.runs + 1):
            total += 1
            log_path = os.path.join(tempfile.mkdtemp(), "provider.log")
            results, stalls = await one_run(args_mode, args.provider, run, log_path)
            bad = [r for r in results if not r[1]]
            print(f"== {mode} Lauf {run}: {'ok' if not bad else 'FEHLER'}")
            for jid, ok, text in results:
                print(f"   {'ok  ' if ok else 'FAIL'} {jid}: {text}")
            if args.show_log:
                with open(log_path) as f:
                    print(f.read())
            if bad:
                failed_runs += 1
                with open(log_path) as f:
                    tail = f.read()[-3000:]
                print(f"   --- Provider-Log (Ende, {log_path}) ---")
                print("   " + tail.replace("\n", "\n   "))
                if not args.keep_going:
                    print(f"{failed_runs} von {total} Laeufen FEHLERHAFT")
                    return 1
    print("ALLE TESTS OK" if not failed_runs else f"{failed_runs} von {total} Laeufen FEHLERHAFT")
    return 1 if failed_runs else 0


if __name__ == "__main__":
    sys.exit(asyncio.run(main()))

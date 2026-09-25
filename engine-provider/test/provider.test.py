#!/usr/bin/env python3
"""Prueft, worauf RookHub beim OFFIZIELLEN Provider baut — gegen einen nachgebauten Broker.

    python3 test/provider.test.py /tmp/provider.py
    docker cp rookhub-engine-provider:/opt/provider.py /tmp/p.py && python3 test/provider.test.py /tmp/p.py

Seit lila-engine 0e1223b (2026-09-06) weist der Broker jeden Upload ohne abschliessendes `bestmove`
mit 400 ab. Der frueher gepinnte Provider (a6ef15a8) schickte nie eins, schlief nach der Ablehnung
5 s und holte erst DANACH den naechsten Auftrag: ein Zug kurz nach einer beendeten Suche wartete
rund vier Sekunden, bis die Engine anlief. Geprueft wird deshalb, was man am Brett merkt:

1. Eine beendete Suche endet mit `bestmove` (sonst 400 wie beim echten Broker), score-lose
   `info`-Zeilen bleiben beim Provider.
2. Der naechste Auftrag direkt danach liefert seine erste Zeile ohne Verzoegerung.
3. Schweigt die Engine (lange MultiPV-Iteration), kommt `{"keepalive":true}` — ohne ein
   Lebenszeichen kappt der Broker die stumme Verbindung nach 60 s.
4. Ein Zug WAEHREND der Suche: die alte Suche endet mit `bestmove`, die neue startet sofort.
5. Jeder Upload kommt auf einer FRISCHEN Verbindung (patch_force_close.py, upstream Issue #45):
   eine wiederverwendete Pool-Verbindung kann inzwischen von der Gegenseite geschlossen sein, der
   Upload stirbt dann im ersten Byte und der Anfragende wartet 15 s ins Leere. Der Test erwartet
   den GEPATCHTEN Provider (die CI wendet den Patch vor dem Test an); ungepatcht ist er hier rot.

Statt Stockfish laeuft ein Stub. Der Test dauert rund 20 s — der 15-s-Takt der Lebenszeichen ist
im Provider fest verdrahtet.
"""
import asyncio
import os
import shlex
import sys
import tempfile
import time

from aiohttp import web

MAX_START_SECONDS = 2.0       # frueherer Provider: ~5 s (Schlaf nach der 400)
KEEPALIVE_WAIT_SECONDS = 20   # Takt im Provider: 15 s
KEEPALIVE = '{"keepalive":true}'
FEN = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1"

# readline() statt `for line in sys.stdin`: die Iteration liest im Block voraus und gaebe die Zeilen
# erst verzoegert heraus — der Handshake (uci/uciok) haengt dann.
# `stop` beantwortet der Stub NUR waehrend einer Suche mit `bestmove`, wie Stockfish: der Provider
# schickt vor JEDEM Auftrag ein `stop`, und ein streunendes `bestmove` beendete sonst die naechste Suche.
STUB = r'''
import sys
searching = False
while True:
    line = sys.stdin.readline()
    if not line:
        break
    cmd = line.strip()
    if cmd == "uci":
        print("id name Stub", flush=True)
        print("uciok", flush=True)
    elif cmd == "isready":
        print("readyok", flush=True)
    elif cmd.startswith("go depth"):
        print("info depth 1 seldepth 1 multipv 1 score cp 10 nodes 20 nps 20 time 1 pv e2e4", flush=True)
        print("info depth 2 currmove e2e4 currmovenumber 1", flush=True)
        if int(cmd.split()[2]) <= 1:
            print("bestmove e2e4", flush=True)
        else:
            searching = True          # lange Iteration: ab hier SCHWEIGEN bis `stop`
    elif cmd == "stop" and searching:
        searching = False
        print("bestmove e2e4", flush=True)
'''


def job(job_id, depth):
    return {"id": job_id, "work": {"sessionId": "s1", "threads": 1, "hash": 16, "multiPv": 1,
                                   "variant": "chess", "initialFen": FEN, "moves": [], "depth": depth}}


class FakeBroker:
    """Lichess-API + Broker in einem: Registrierung, Auftragsvergabe, Upload."""

    def __init__(self):
        self.jobs = asyncio.Queue()
        self.uploads = {}
        self.keepalive = asyncio.Event()
        self.peers = []          # Client-Adresse je Upload: gleicher Port = wiederverwendete Verbindung

    async def list_engines(self, _request):
        return web.json_response([])

    async def register(self, _request):
        return web.json_response({"id": "eei_test"})

    async def acquire(self, _request):
        # Long-Poll wie beim Broker; unter der 12-s-Schranke des Providers bleiben.
        try:
            return web.json_response(await asyncio.wait_for(self.jobs.get(), timeout=5))
        except asyncio.TimeoutError:
            return web.Response(status=204)

    async def submit(self, request):
        self.peers.append(request.transport.get_extra_info("peername") if request.transport else None)
        upload = self.uploads[request.match_info["id"]] = {"lines": []}
        async for raw in request.content:
            line = raw.decode().strip()
            if not line:
                continue
            upload["lines"].append((time.monotonic(), line))
            if line == KEEPALIVE:
                self.keepalive.set()
        # Die Regel des echten Brokers seit lila-engine 0e1223b.
        finished = any(line.startswith("bestmove") for _, line in upload["lines"])
        upload["status"] = 200 if finished else 400
        upload["end"] = time.monotonic()
        if finished:
            return web.Response(status=200)
        return web.Response(status=400, text="uci protocol error: expected bestmove before end of stream")


async def until(predicate, timeout):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if predicate():
            return True
        await asyncio.sleep(0.02)
    return False


async def scenario(broker, check):
    ups = broker.uploads

    # ===== 1. Eine Suche laeuft zu Ende =====================================================
    await broker.jobs.put(job("j1", 1))
    if not check(await until(lambda: "end" in ups.get("j1", {}), 15),
                 "Suche 1 beendet", "Suche 1 kam binnen 15 s nicht zu Ende"):
        return
    lines = [line for _, line in ups["j1"]["lines"]]
    check(any(line.startswith("info") and "score" in line for line in lines),
          "info-Zeile mit score wird weitergereicht", f"keine score-Zeile im Upload: {lines}")
    check(not any("currmove" in line for line in lines),
          "score-lose info-Zeilen bleiben beim Provider", f"score-lose Zeile weitergereicht: {lines}")
    check(bool(lines) and lines[-1].startswith("bestmove") and ups["j1"]["status"] == 200,
          "Upload endet mit bestmove (Broker antwortet 200)",
          f"Upload ohne abschliessendes bestmove, Broker antwortet 400: {lines}")

    # ===== 2. Zug eine Sekunde nach der beendeten Suche ======================================
    await asyncio.sleep(1.0)
    offered = time.monotonic()
    await broker.jobs.put(job("j2", 99))
    if not check(await until(lambda: ups.get("j2", {}).get("lines"), 10),
                 "Suche 2 liefert", "Suche 2 lieferte binnen 10 s keine Zeile"):
        return
    wait = ups["j2"]["lines"][0][0] - offered
    check(wait < MAX_START_SECONDS,
          f"naechste Suche startet sofort ({wait:.2f} s nach der Vergabe)",
          f"naechste Suche lieferte erst nach {wait:.2f} s (Grenze {MAX_START_SECONDS} s)")

    # ===== 3. Die Engine schweigt ===========================================================
    try:
        await asyncio.wait_for(broker.keepalive.wait(), KEEPALIVE_WAIT_SECONDS)
        check(True, "Lebenszeichen {\"keepalive\":true} bei schweigender Engine", "")
    except asyncio.TimeoutError:
        check(False, "", f"kein Lebenszeichen binnen {KEEPALIVE_WAIT_SECONDS} s schweigender Engine")

    # ===== 4. Zug waehrend der Suche ========================================================
    offered = time.monotonic()
    await broker.jobs.put(job("j3", 1))
    if not check(await until(lambda: "end" in ups.get("j3", {}), 15),
                 "Suche 3 beendet", "Suche 3 kam binnen 15 s nicht zu Ende"):
        return
    aborted = [line for _, line in ups["j2"]["lines"]]
    check("end" in ups["j2"] and aborted[-1].startswith("bestmove") and ups["j2"]["status"] == 200,
          "abgebrochene Suche endet mit bestmove (Broker antwortet 200)",
          f"abgebrochene Suche endete nicht sauber: {aborted}")
    wait = ups["j3"]["lines"][0][0] - offered
    check(wait < MAX_START_SECONDS,
          f"Zug waehrend der Suche startet sofort ({wait:.2f} s)",
          f"Zug waehrend der Suche lieferte erst nach {wait:.2f} s (Grenze {MAX_START_SECONDS} s)")

    # ===== 5. Jeder Upload auf einer frischen Verbindung =====================================
    # Mit Verbindungs-Pool kaemen j2 und j3 ueber dieselbe Verbindung wie j1 (gleicher Client-Port);
    # mit force_close hat jeder Upload einen eigenen.
    ports = [peer[1] for peer in broker.peers if peer]
    check(len(ports) == 3 and len(set(ports)) == 3,
          f"jeder Upload auf frischer Verbindung (Client-Ports {ports})",
          f"Uploads teilen sich Verbindungen (Client-Ports {ports}) — patch_force_close.py fehlt oder greift nicht")


async def main(src):
    fails = []

    def check(condition, ok, fail):
        print(f"ok   {ok}" if condition else f"FAIL: {fail}")
        if not condition:
            fails.append(fail)
        return condition

    tmp = tempfile.mkdtemp()
    stub = os.path.join(tmp, "stub_engine.py")
    with open(stub, "w") as f:
        f.write(STUB)
    log_path = os.path.join(tmp, "provider.log")

    broker = FakeBroker()
    app = web.Application()
    app.router.add_get("/api/external-engine", broker.list_engines)
    app.router.add_post("/api/external-engine", broker.register)
    app.router.add_post("/api/external-engine/work", broker.acquire)
    app.router.add_post("/api/external-engine/work/{id}", broker.submit)
    runner = web.AppRunner(app)
    await runner.setup()
    site = web.TCPSite(runner, "127.0.0.1", 0)
    await site.start()
    base = f"http://127.0.0.1:{runner.addresses[0][1]}"

    with open(log_path, "w") as log:
        proc = await asyncio.create_subprocess_exec(
            sys.executable, src,
            "--engine", f"exec {shlex.quote(sys.executable)} {shlex.quote(stub)}",
            "--name", "Vertragstest", "--lichess", base, "--broker", base, "--token", "lip_test",
            "--max-threads", "1", "--max-hash", "16",
            stdout=log, stderr=log)
        try:
            await scenario(broker, check)
        finally:
            proc.terminate()
            try:
                await asyncio.wait_for(proc.wait(), 5)
            except asyncio.TimeoutError:
                proc.kill()
                await proc.wait()
            await runner.cleanup()

    if fails:
        with open(log_path) as log:
            print("--- Provider-Log (Ende) ---")
            print(log.read()[-4000:])
    print("ALLE TESTS OK" if not fails else f"{len(fails)} FEHLER")
    return 1 if fails else 0


if __name__ == "__main__":
    path = sys.argv[1] if len(sys.argv) > 1 else ""
    if not path or not os.path.exists(path):
        # Ein fehlender Pfad ist ein Fehler, kein Ueberspringen — sonst meldet der Test bei einem
        # Tippfehler im CI-Aufruf still Erfolg.
        print(f"FEHLER: Provider-Skript {path!r} nicht vorhanden (Pfad als Argument angeben)", file=sys.stderr)
        sys.exit(1)
    sys.exit(asyncio.run(main(path)))

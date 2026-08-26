#!/usr/bin/env python3
"""Prueft den EINEN Eingriff dieses Images in den offiziellen Provider: das Lebenszeichen im
Analyse-Stream (siehe ../patch_provider.py).

Aufruf mit einem ORIGINAL-provider.py (wird kopiert + gepatcht, das Original bleibt unangetastet):

    python3 test/heartbeat.test.py /opt/provider.py
    docker cp rookhub-engine-provider:/opt/provider.py /tmp/p.py && python3 test/heartbeat.test.py /tmp/p.py

Statt Stockfish laeuft ein Stub, der auf `uci`/`isready` antwortet, nach `go` aber SCHWEIGT — genau die
Lage, in der die Verbindung zum Broker frueher stumm zulief und geschlossen wurde. Erwartet wird, dass
der Stream in dieser Stille Leerzeilen liefert und danach die echte Zeile mit `score` durchreicht.
"""
import os, shutil, subprocess, sys, tempfile, types

HERE = os.path.dirname(os.path.abspath(__file__))
src = sys.argv[1] if len(sys.argv) > 1 else "/opt/provider.py"
if not os.path.exists(src):
    print(f"UEBERSPRUNGEN: {src} nicht vorhanden (Pfad zu einer provider.py angeben)")
    sys.exit(0)

tmp = tempfile.mkdtemp()
patched = os.path.join(tmp, "provider_patched.py")
shutil.copy(src, patched)
os.environ["HEARTBEAT_SECONDS"] = "1"          # Test soll nicht 15 s warten
subprocess.run([sys.executable, os.path.join(HERE, "..", "patch_provider.py"), patched], check=True)

sys.path.insert(0, tmp)
import provider_patched as provider                                    # noqa: E402

# readline() statt `for line in sys.stdin`: die Iteration liest im Block voraus und gaebe die Zeilen
# erst verzoegert heraus — der Handshake (uci/uciok) haengt dann.
STUB = r'''
import sys
while True:
    line = sys.stdin.readline()
    if not line: break
    line = line.strip()
    if line == "uci":       print("uciok", flush=True)
    elif line == "isready": print("readyok", flush=True)
    elif line == "stop":    print("bestmove e2e4", flush=True)
    elif line == "emit":    print("info depth 30 score cp 12 nodes 5 time 5", flush=True)
    elif line.startswith("go"): pass                # SCHWEIGEN — das ist der Testfall
'''
stub = os.path.join(tmp, "stub_engine.py")
open(stub, "w").write(STUB)

args = types.SimpleNamespace(engine=f"{sys.executable} {stub}", setoption=[])
engine = provider.Engine(args)
job = {"id": "t1", "work": {"sessionId": "s1", "threads": 1, "hash": 16, "multiPv": 1,
                            "variant": "chess", "initialFen": "startpos", "moves": [], "depth": 30}}

import threading                                                        # noqa: E402
started = threading.Event()
fails = 0
with engine.analyse(job, started) as stream:
    beats = 0
    for chunk in stream:
        if chunk == b"\n":
            beats += 1
            if beats == 2:
                engine.send("emit")   # zwei Lebenszeichen aus reinem Schweigen — jetzt eine echte Zeile
            if beats > 10:
                print("FAIL: Stream liefert nur noch Lebenszeichen"); fails += 1; break
            continue
        text = chunk.decode()
        if "score" not in text:
            print(f"FAIL: unerwartete Zeile im Stream: {text!r}"); fails += 1
        else:
            print("ok   Zeile mit score wird durchgereicht")
        break
    if beats >= 2:
        print(f"ok   Lebenszeichen bei Schweigen ({beats} Leerzeilen)")
    else:
        print(f"FAIL: kein Lebenszeichen bei Schweigen (nur {beats})"); fails += 1

engine.terminate()
shutil.rmtree(tmp, ignore_errors=True)
print("ALLE TESTS OK" if not fails else f"{fails} FEHLER")
sys.exit(1 if fails else 0)

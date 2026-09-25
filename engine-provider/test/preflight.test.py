#!/usr/bin/env python3
"""Testet preflight.py gegen einen nachgebauten /api/token/test (RookHub- und Lichess-Form).

Aufruf: python3 test/preflight.test.py  — Exit 0 = alles gut. Nur Standardbibliothek.
Geprüft: der Token geht als text/plain-Rumpf an {LICHESS_URL}/api/token/test; engine:read+engine:write
= Start; unbekannt (null) = Abbruch mit dem passenden Satz für RookHub bzw. Lichess; fehlender Scope =
Abbruch; Server nicht erreichbar = trotzdem Start (der Provider hat eigenes Backoff).
"""
import http.server
import json
import os
import subprocess
import sys
import threading

HERE = os.path.dirname(os.path.abspath(__file__))
PREFLIGHT = os.path.join(HERE, "..", "preflight.py")
fails = 0
seen = []
answer = {}


class Handler(http.server.BaseHTTPRequestHandler):
    def do_POST(self):
        body = self.rfile.read(int(self.headers.get("Content-Length", "0"))).decode()
        seen.append((self.path, self.headers.get("Content-Type"), body))
        data = json.dumps({body: answer.get("info")}).encode()
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def log_message(self, *args):
        pass


server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Handler)
threading.Thread(target=server.serve_forever, daemon=True).start()
base = f"http://127.0.0.1:{server.server_address[1]}"


def run(env):
    full = {"PATH": os.environ.get("PATH", ""), **env}
    return subprocess.run([sys.executable, PREFLIGHT], env=full, capture_output=True, text=True, timeout=30)


def check(name, cond):
    global fails
    print(("ok   " if cond else "FAIL ") + name)
    if not cond:
        fails += 1


# 1) RookHub: Token mit Scope „Engine" → Start
answer["info"] = {"userId": "kahalm", "scopes": "engine:read,engine:write", "expires": None}
seen.clear()
r = run({"LICHESS_API_TOKEN": "rkh_abc", "LICHESS_URL": base, "ROOKHUB_URL": base})
check("RookHub: Token in Ordnung → Exit 0", r.returncode == 0)
check("RookHub: nennt das RookHub-Konto", "RookHub-Konto: kahalm" in r.stdout)
check("POST an /api/token/test", seen and seen[0][0] == "/api/token/test")
check("Token als text/plain-Rumpf", seen and seen[0][1] == "text/plain" and seen[0][2] == "rkh_abc")

# 2) RookHub: unbekannt/falscher Scope (null) → Abbruch mit Hinweis auf den Engine-Scope
answer["info"] = None
r = run({"LICHESS_API_TOKEN": "rkh_ext", "LICHESS_URL": base, "ROOKHUB_URL": base})
check("RookHub: unbekannter Token → Exit 1", r.returncode == 1)
check("RookHub: Satz nennt RookHub", "RookHub kennt diesen Token nicht" in r.stderr)
check("RookHub: Hinweis auf Scope „Engine“", "Scope „Engine“" in r.stderr)
check("RookHub: Token selbst nicht ausgegeben", "rkh_ext" not in r.stdout + r.stderr)

# 3) Lichess-Weg unverändert: fehlender Scope → Abbruch
answer["info"] = {"userId": "x", "scopes": "engine:read"}
r = run({"LICHESS_API_TOKEN": "lip_x", "LICHESS_URL": base})
check("Lichess: fehlendes engine:write → Exit 1", r.returncode == 1 and "engine:write" in r.stderr)
answer["info"] = None
r = run({"LICHESS_API_TOKEN": "lip_x", "LICHESS_URL": base})
check("Lichess: Satz nennt Lichess", r.returncode == 1 and "Lichess kennt diesen Token nicht" in r.stderr)

# 4) Server nicht erreichbar → trotzdem Start (kein Stacktrace, kein Abbruch)
server.shutdown()
server.server_close()
r = run({"LICHESS_API_TOKEN": "rkh_abc", "LICHESS_URL": base, "ROOKHUB_URL": base})
check("nicht erreichbar → Exit 0 mit Hinweis", r.returncode == 0 and "nicht vorab geprüft" in r.stderr)

print("ALLE TESTS OK" if fails == 0 else f"{fails} Test(s) fehlgeschlagen")
sys.exit(1 if fails else 0)

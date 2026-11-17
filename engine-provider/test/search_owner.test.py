#!/usr/bin/env python3
"""Der verlassene Upload-Generator, Schritt fuer Schritt — ohne Broker, mit der Schein-Engine.

    python3 test/search_owner.test.py /tmp/provider.py

Ergaenzt test/provider_offset.test.py: dort entsteht der Versatz ueber echte Verbindungen, der Zeitpunkt der
Finalisierung haengt am Zufall. Hier treibt der Test `Engine.analyse` selbst (so wie aiohttp den Upload-Rumpf)
und legt die Zeitpunkte fest:

1. Auftrag A liefert einige Zeilen und wird dann LIEGEN GELASSEN, mitten in der Suche, an einem `yield` —
   genau das passiert, wenn aiohttp den Abriss beim Schreiben einer Zeile merkt.
2. Wie die Hauptschleife des Providers: `stop`, dann Auftrag B. B muss nur Zeilen der EIGENEN Stellung
   liefern und mit dem eigenen `bestmove` enden.
3. MITTEN in B (B rechnet langsam, ueber 200 Tiefen hinaus) laeuft das `finally` von A doch noch
   (`aclose()`, wie die spaete Muellabfuhr). Es darf nichts anrichten: kein `stop` an Bs Suche (B erreicht
   die volle Tiefe), kein zweiter Leser (B bekommt sein `bestmove`, kein Stillstand mit nur Lebenszeichen).
4. Auftrag C danach: wieder nur eigene Zeilen.

Erwartet den GEPATCHTEN Provider (patch_search_owner.py); ungepatcht ist er rot.
"""
import argparse
import asyncio
import importlib.util
import os
import shlex
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
POS_W = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1"
POS_B = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1"
DEPTH_B = 230         # Schein-Engine: 200 Tiefen im Schwall, danach je 50 ms
STEP_TIMEOUT = 5      # laenger als das hier ohne Zeile: Stillstand (der Provider schickte nur Lebenszeichen)


def own_squares(fen):
    board, side = fen.split()[0], fen.split()[1]
    out = set()
    for r, row in enumerate(board.split("/")):
        f = 0
        for ch in row:
            if ch.isdigit():
                f += int(ch)
                continue
            if (ch.isupper() and side == "w") or (ch.islower() and side == "b"):
                out.add("abcdefgh"[f] + str(8 - r))
            f += 1
    return out


def first_square(line):
    words = line.split()
    if words[0] == "bestmove":
        return words[1][:2]
    return words[words.index("pv") + 1][:2] if "pv" in words else ""


def job(jid, fen, depth):
    return {"id": jid, "work": {"sessionId": "s1", "threads": 1, "hash": 16, "multiPv": 1, "variant": "chess",
                                "initialFen": fen, "moves": [], "depth": depth}}


async def pull(gen, n=None):
    """Zeilen aus dem Generator holen (ohne Lebenszeichen): n Stueck oder bis bestmove. None = Stillstand."""
    lines = []
    while n is None or len(lines) < n:
        try:
            chunk = await asyncio.wait_for(gen.__anext__(), STEP_TIMEOUT)
        except StopAsyncIteration:
            break
        except asyncio.TimeoutError:
            return lines, "stall"
        line = chunk.decode().strip()
        if line == '{"keepalive":true}':
            continue
        lines.append(line)
        if line.startswith("bestmove"):
            break
    return lines, None


async def main(src):
    fails = []

    def check(cond, ok, fail):
        print(f"ok   {ok}" if cond else f"FAIL: {fail}")
        if not cond:
            fails.append(fail)
        return cond

    spec = importlib.util.spec_from_file_location("provider_under_test", src)
    provider = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(provider)
    args = argparse.Namespace(
        engine=f"exec {shlex.quote(sys.executable)} {shlex.quote(os.path.join(HERE, 'fake_uci_engine.py'))}",
        setoption=[])
    engine = await provider.Engine.create(args)
    sent = []
    orig_send = engine.send

    async def send(command):
        sent.append(command)
        await orig_send(command)
    engine.send = send

    def verdict(name, lines, fen, stalled, full_depth=None):
        foreign = [l for l in lines if first_square(l) not in own_squares(fen)]
        if not check(not stalled, f"{name} endet (kein Stillstand)",
                     f"{name}: STILLSTAND nach {len(lines)} Zeilen — kein bestmove binnen {STEP_TIMEOUT} s"):
            return
        check(bool(lines) and lines[-1].startswith("bestmove"), f"{name} endet mit bestmove",
              f"{name}: kein bestmove am Ende: {lines[-1:]}")
        check(not foreign, f"{name}: alle {len(lines)} Zeilen zur eigenen Stellung",
              f"{name}: {len(foreign)}/{len(lines)} Zeilen gehoeren zu einer FREMDEN Stellung, z. B. {foreign[:1]}")
        if full_depth:
            infos = [l for l in lines if l.startswith("info")]
            depth = int(infos[-1].split()[2]) if infos else 0
            check(depth == full_depth, f"{name} rechnet bis zur vollen Tiefe {full_depth} (kein fremdes stop)",
                  f"{name}: Suche bei Tiefe {depth}/{full_depth} abgebrochen — fremdes stop")

    try:
        # 1. A liefert und bleibt liegen
        a = engine.analyse(job("A", POS_W, 100000), asyncio.Event())
        lines_a, _ = await pull(a, 5)
        check(len(lines_a) == 5, "Auftrag A liefert", f"Auftrag A lieferte nur {lines_a}")

        # 2. Hauptschleife: stop, dann B
        await engine.stop()
        b = engine.analyse(job("B", POS_B, DEPTH_B), asyncio.Event())
        lines_b, stalled = await pull(b, 205)
        # 3. spaetes finally von A mitten in B
        stop_before = sent.count("stop")
        try:
            await asyncio.wait_for(a.aclose(), STEP_TIMEOUT)
            late = None
        except Exception as err:      # ungepatcht: z. B. RuntimeError (zweiter Leser)
            late = err
        check(late is None, "spaetes finally von A laeuft ohne Fehler", f"spaetes finally von A: {late!r}")
        check(sent.count("stop") == stop_before, "spaetes finally von A schickt kein stop",
              "spaetes finally von A schickt stop an die Suche von B")
        if not stalled and not (lines_b and lines_b[-1].startswith("bestmove")):
            more, stalled = await pull(b)
            lines_b += more
        verdict("Auftrag B", lines_b, POS_B, stalled, full_depth=DEPTH_B)

        # 4. C danach
        await engine.stop()
        c = engine.analyse(job("C", POS_W, 12), asyncio.Event())
        lines_c, stalled = await pull(c)
        verdict("Auftrag C", lines_c, POS_W, stalled)
    finally:
        await engine.terminate()

    print("ALLE TESTS OK" if not fails else f"{len(fails)} FEHLER")
    return 1 if fails else 0


if __name__ == "__main__":
    path = sys.argv[1] if len(sys.argv) > 1 else ""
    if not path or not os.path.exists(path):
        print(f"FEHLER: Provider-Skript {path!r} nicht vorhanden (Pfad als Argument angeben)", file=sys.stderr)
        sys.exit(1)
    sys.exit(asyncio.run(main(path)))

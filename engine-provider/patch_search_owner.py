#!/usr/bin/env python3
"""ZWEITER Eingriff in den geholten Provider: die Engine weiss selbst, ob eine Suche aussteht und wem sie gehoert.

Aufruf beim Bauen (NACH der Pruefsummen-Kontrolle, neben patch_force_close.py) und in der CI vor den
Provider-Tests:

    python3 patch_search_owner.py /opt/provider.py

Fehlerbild (Prod 16./17.11.2026, alle Rechner): nach einem abgerissenen Upload (API-Neustart, Verbindung
gekappt) liefert JEDER weitere Auftrag die Zeilen der VORIGEN Suche samt deren `bestmove` und endet sofort —
bis der Provider-Prozess endet. Nachgestellt in test/provider_offset.test.py (echtes Stockfish und
Schein-Engine).

Ursache: `Engine.analyse` ist ein Async-Generator, den aiohttp als Upload-Rumpf verbraucht; das Aufraeumen
einer abgebrochenen Suche (`stop`, dann bis zu ihrem `bestmove` lesen) steht in dessen `finally`. Merkt
aiohttp den Abriss beim SCHREIBEN einer Zeile (die Engine liefert gerade im Schwall), bleibt der Generator
an seinem `yield` liegen und wird nie oder erst viel spaeter finalisiert. Der naechste Auftrag schickt
`position`/`go` und liest die ungelesene Ausgabe der alten Suche bis zu deren `bestmove` — und endet damit
regulaer, ohne die EIGENE Suche abzuraeumen. Ab da liest jeder Auftrag die Ausgabe seines Vorgaengers.
Laeuft das `finally` spaeter doch noch, schickt es `stop` an eine fremde Suche und liest als zweiter Leser
mit (dazu kommt der `recv()`-Task, den der liegengebliebene Generator schon gestartet hatte).

Der Eingriff verlegt den Zustand vom Generator an die Engine:
- `searching`: `go` gesendet, zugehoeriges `bestmove` noch nicht gelesen.
- `reader`: der EINE ausstehende Lese-Task auf stdout; alles Lesen geht unter `io_lock` ueber ihn, ein
  abgebrochener Leser bleibt der Engine erhalten statt verloren zu gehen.
- `owner`: der Auftrag, dem die Engine gerade gehoert. Vor `position`/`go` raeumt ein neuer Auftrag eine
  ausstehende Suche ab (`stop`, bis `bestmove` lesen und verwerfen; antwortet die Engine binnen 30 s nicht,
  wird sie beendet und neu gestartet).
- Das `finally` eines Generators raeumt nur noch auf, solange ihm die Engine noch gehoert — sonst nichts
  (kein `stop` an eine fremde Suche, kein zweiter Leser).

Verankert an den Textstellen des gepinnten Stands; fehlt eine, bricht der Build ab (Pin-Wechsel: pruefen, ob
upstream es behoben hat — dann diese Datei samt Aufrufen entfernen). Ein zweiter Aufruf aendert nichts.
"""
import sys

MARK = "# patch_search_owner:"

INIT_OLD = """        self.stop_lock = asyncio.Lock()
"""
INIT_NEW = """        self.stop_lock = asyncio.Lock()
        # patch_search_owner: Zustand der Suche an der Engine, nicht im Upload-Generator.
        self.io_lock = asyncio.Lock()
        self.reader: asyncio.Task | None = None
        self.searching = False
        self.owner: object | None = None
"""

ISREADY_OLD = """    async def isready(self) -> None:
        await self.send("isready")
        while True:
            command, _ = await self.recv()
            if command == "readyok":
                break
"""
ISREADY_NEW = """    async def isready(self) -> None:
        # patch_search_owner: nur unter io_lock aufgerufen (analyse), liest ueber den einen Leser.
        await self.send("isready")
        while True:
            command, _ = await self._read_line()
            if command == "readyok":
                break
"""

ANALYSE_OLD = """    async def analyse(self, job, job_started: asyncio.Event):
        work = job["work"]
        read_task: asyncio.Task | None = None

        try:
            if work["sessionId"] != self.session_id:
                self.session_id = work["sessionId"]
                await self.send("ucinewgame")
                await self.isready()

            options_changed = False
            if self.threads != work["threads"]:
                await self.setoption("Threads", work["threads"])
                self.threads = work["threads"]
                options_changed = True
            if self.hash != work["hash"]:
                await self.setoption("Hash", work["hash"])
                self.hash = work["hash"]
                options_changed = True
            if self.multi_pv != work["multiPv"]:
                await self.setoption("MultiPV", work["multiPv"])
                self.multi_pv = work["multiPv"]
                options_changed = True
            if self.uci_variant != work["variant"]:
                await self.setoption("UCI_Variant", work["variant"])
                self.uci_variant = work["variant"]
                options_changed = True
            if options_changed:
                await self.isready()

            await self.send(f"position fen {work['initialFen']} moves {' '.join(work['moves'])}")

            for key in ("movetime", "depth", "nodes"):
                if key in work:
                    await self.send(f"go {key} {work[key]}")
                    break

            job_started.set()
            last_ping = time.monotonic()

            read_task = asyncio.create_task(self.recv())

            while True:
                done, _ = await asyncio.wait({read_task}, timeout=max(0, 15 + last_ping - time.monotonic()))
                self.last_used = time.monotonic()
                if not done:
                    # To support long searches, a provider must send '{"keepalive":true}\\n' in the
                    # same streamed work submission every 15 seconds.

                    yield b'{"keepalive":true}\\n'
                    last_ping = time.monotonic()
                    continue

                command, params = read_task.result()
                read_task = None

                if command == "bestmove":
                    yield f"{command} {params}\\n".encode()
                    return

                read_task = asyncio.create_task(self.recv())

                if command == "info":
                    if "score" in params:
                        yield f"{command} {params}\\n".encode()
                else:
                    logging.warning("Unexpected engine command: %s", command)
        finally:
            if read_task is not None and self.alive():
                try:
                    await self.stop()
                    await self.drain_to_bestmove(read_task)
                except EOFError:
                    pass

            self.last_used = time.monotonic()

    async def drain_to_bestmove(self, read_task: asyncio.Task) -> None:
        command, _ = await read_task
        while command != "bestmove" and self.alive():
            command, _ = await self.recv()
"""
ANALYSE_NEW = """    async def analyse(self, job, job_started: asyncio.Event):
        # patch_search_owner: der Auftrag uebernimmt die Engine, raeumt eine ausstehende Suche ab und
        # liest nur, solange sie ihm gehoert. Sein finally raeumt nur als Besitzer auf.
        work = job["work"]
        token = object()
        self.owner = token

        try:
            async with self.io_lock:
                await self._settle()

                if work["sessionId"] != self.session_id:
                    self.session_id = work["sessionId"]
                    await self.send("ucinewgame")
                    await self.isready()

                options_changed = False
                if self.threads != work["threads"]:
                    await self.setoption("Threads", work["threads"])
                    self.threads = work["threads"]
                    options_changed = True
                if self.hash != work["hash"]:
                    await self.setoption("Hash", work["hash"])
                    self.hash = work["hash"]
                    options_changed = True
                if self.multi_pv != work["multiPv"]:
                    await self.setoption("MultiPV", work["multiPv"])
                    self.multi_pv = work["multiPv"]
                    options_changed = True
                if self.uci_variant != work["variant"]:
                    await self.setoption("UCI_Variant", work["variant"])
                    self.uci_variant = work["variant"]
                    options_changed = True
                if options_changed:
                    await self.isready()

                await self.send(f"position fen {work['initialFen']} moves {' '.join(work['moves'])}")

                for key in ("movetime", "depth", "nodes"):
                    if key in work:
                        self.searching = True
                        await self.send(f"go {key} {work[key]}")
                        break

            job_started.set()
            last_ping = time.monotonic()

            while True:
                line = await self.read_line(token, timeout=max(0, 15 + last_ping - time.monotonic()))
                self.last_used = time.monotonic()
                if line is SUPERSEDED:
                    logging.warning("Search superseded by a newer job")
                    return
                if line is None:
                    # To support long searches, a provider must send '{"keepalive":true}\\n' in the
                    # same streamed work submission every 15 seconds.

                    yield b'{"keepalive":true}\\n'
                    last_ping = time.monotonic()
                    continue

                command, params = line

                if command == "bestmove":
                    yield f"{command} {params}\\n".encode()
                    return

                if command == "info":
                    if "score" in params:
                        yield f"{command} {params}\\n".encode()
                else:
                    logging.warning("Unexpected engine command: %s", command)
        finally:
            if self.owner is token and self.searching and self.alive():
                try:
                    async with self.io_lock:
                        if self.owner is token:
                            await self._settle()
                except EOFError:
                    pass

            self.last_used = time.monotonic()

    async def read_line(self, token, timeout=None):
        \"\"\"Naechste Zeile fuer den Auftrag `token`; None nach `timeout`, SUPERSEDED, wenn die Engine
        inzwischen einem anderen Auftrag gehoert (dann wird NICHTS gelesen).\"\"\"
        async with self.io_lock:
            if self.owner is not token:
                return SUPERSEDED
            return await self._read_line(timeout)

    async def _read_line(self, timeout=None):
        # Nur unter io_lock. Der Lese-Task gehoert der Engine: bricht der Aufrufer ab (Timeout,
        # Abbruch des Uploads), bleibt er stehen und liefert seine Zeile dem naechsten Leser.
        if self.reader is None:
            self.reader = asyncio.create_task(self.recv())
        done, _ = await asyncio.wait({self.reader}, timeout=timeout)
        if not done:
            return None
        task, self.reader = self.reader, None
        command, params = task.result()
        if command == "bestmove":
            self.searching = False
        return command, params

    async def _settle(self) -> None:
        # Nur unter io_lock. Steht eine Suche aus: stop, bis zu ihrem bestmove lesen und verwerfen.
        if not self.searching:
            return
        if not self.alive():
            self.searching = False
            self.reader = None
            return
        await self.stop()
        deadline = time.monotonic() + SETTLE_SECONDS
        while self.searching:
            if await self._read_line(timeout=max(0, deadline - time.monotonic())) is None:
                logging.error("Engine sent no bestmove within %d s after stop, restarting it", SETTLE_SECONDS)
                self.searching = False
                self.reader = None
                await self.terminate()
                raise EOFError()
"""

CONST_OLD = """class Engine:
"""
CONST_NEW = """SUPERSEDED = object()  # patch_search_owner: Engine gehoert inzwischen einem neueren Auftrag
SETTLE_SECONDS = 30    # patch_search_owner: so lange darf eine Engine nach `stop` fuer ihr bestmove brauchen


class Engine:
"""

STEPS = [("Konstanten", CONST_OLD, CONST_NEW), ("Engine.__init__", INIT_OLD, INIT_NEW),
         ("Engine.isready", ISREADY_OLD, ISREADY_NEW), ("Engine.analyse", ANALYSE_OLD, ANALYSE_NEW)]

path = sys.argv[1] if len(sys.argv) > 1 else "/opt/provider.py"
src = open(path, encoding="utf-8").read()
if MARK in src:
    print(f"{path}: bereits gepatcht (search_owner)")
    sys.exit(0)
for name, old, _new in STEPS:
    n = src.count(old)
    if n != 1:
        print(f"FEHLER: Textstelle {name} {n}x gefunden (erwartet 1x) in {path} — Provider-Stand pruefen, "
              f"Patch anpassen oder entfernen.", file=sys.stderr)
        sys.exit(1)
for _name, old, new in STEPS:
    src = src.replace(old, new)
open(path, "w", encoding="utf-8").write(src)
print(f"{path}: Suche gehoert der Engine (ein Leser, Aufraeumen vor jedem Auftrag)")

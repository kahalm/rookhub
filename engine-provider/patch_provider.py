#!/usr/bin/env python3
"""Patcht den OFFIZIELLEN Lichess-Provider (/opt/provider.py) zur Bauzeit um EINE Sache:
ein Lebenszeichen im Analyse-Stream.

Warum: der Provider schickt nur `info`-Zeilen MIT `score` an den Broker. Im MultiPV-Modus
liegen zwischen zwei fertigen Iterationen bei grosser Tiefe Minuten — der Upload schweigt
so lange komplett, und der Broker (bzw. das CDN davor) schliesst die stumme Verbindung.
Beim Empfaenger sieht das aus wie „response ended prematurely": ein Hintergrund-Auftrag
verhungerte damit reproduzierbar kurz vor der Zieltiefe, weil jeder Neustart wieder von
Tiefe 1 hochrechnen muss und nie ueber die Zeit zwischen zwei Abbruechen hinauskam.

Statt einer Kopie des Providers im Repo (die still von upstream abdriften wuerde) werden
hier exakte Textstellen ersetzt. Fehlt eine, bricht der BUILD ab — dann hat sich upstream
geaendert und der Eingriff gehoert geprueft, nicht stillschweigend uebersprungen.
"""
import sys

PATH = sys.argv[1] if len(sys.argv) > 1 else "/opt/provider.py"

# (Beschreibung, vorher, nachher)
PATCHES = [
    ("Import queue",
     "import os\nimport requests\n",
     "import os\nimport queue\nimport requests\n"),

    ("Heartbeat-Konstante",
     "_LOG_LEVEL_MAP = {",
     "# RookHub: Sekunden Schweigen, nach denen ein Lebenszeichen in den Upload geht (0 = aus).\n"
     "ROOKHUB_HEARTBEAT_SECONDS = float(os.environ.get(\"HEARTBEAT_SECONDS\", \"15\")) or None\n"
     "\n"
     "_LOG_LEVEL_MAP = {"),

    ("Leser-Thread starten",
     "        self.stop_lock = threading.Lock()\n",
     "        self.stop_lock = threading.Lock()\n"
     "        # RookHub: EIN Leser-Thread schaufelt die Engine-Ausgabe in eine Queue, damit `recv`\n"
     "        # eine Zeitschranke bekommt. Direkt auf stdout.readline() ginge das nicht.\n"
     "        self.rookhub_lines = queue.Queue()\n"
     "        self.rookhub_stopping = False\n"
     "        threading.Thread(target=self._rookhub_read_loop, daemon=True).start()\n"),

    ("Leser-Thread",
     "    def send(self, command):\n",
     "    def _rookhub_read_loop(self):\n"
     "        try:\n"
     "            for line in self.process.stdout:\n"
     "                self.rookhub_lines.put(line)\n"
     "        finally:\n"
     "            self.rookhub_lines.put(None)   # EOF-Marke; bleibt fuer alle weiteren Leser liegen\n"
     "\n"
     "    def send(self, command):\n"),

    ("recv mit Zeitschranke",
     "    def recv(self):\n"
     "        while True:\n"
     "            line = self.process.stdout.readline()\n"
     "            if line == \"\":\n"
     "                self.alive = False\n"
     "                raise EOFError()\n",
     "    def recv(self, timeout=None):\n"
     "        while True:\n"
     "            line = self.rookhub_lines.get(timeout=timeout)   # queue.Empty = Zeitschranke\n"
     "            if line is None:\n"
     "                self.rookhub_lines.put(None)\n"
     "                self.alive = False\n"
     "                raise EOFError()\n"),

    ("Abbruch merken",
     "    def stop(self):\n"
     "        if self.alive:\n",
     "    def stop(self):\n"
     "        self.rookhub_stopping = True   # ab jetzt nur noch auf bestmove warten (siehe stream())\n"
     "        if self.alive:\n"),

    ("Lebenszeichen im Stream",
     "        job_started.set()\n"
     "\n"
     "        def stream():\n"
     "            while True:\n"
     "                command, params = self.recv()\n",
     "        job_started.set()\n"
     "        self.rookhub_stopping = False\n"
     "\n"
     "        def stream():\n"
     "            while True:\n"
     "                try:\n"
     "                    command, params = self.recv(timeout=ROOKHUB_HEARTBEAT_SECONDS)\n"
     "                except queue.Empty:\n"
     "                    # Nach `stop` laeuft nur noch das Leerraeumen bis bestmove — dabei braucht\n"
     "                    # niemand mehr ein Lebenszeichen (der Upload ist da schon zu).\n"
     "                    if self.rookhub_stopping:\n"
     "                        continue\n"
     "                    yield b\"\\n\"   # Lebenszeichen: haelt die stumme Verbindung offen\n"
     "                    continue\n"),
]

with open(PATH, encoding="utf-8") as f:
    src = f.read()

for name, old, new in PATCHES:
    if src.count(old) != 1:
        sys.exit(f"FEHLER: Textstelle '{name}' {src.count(old)}x gefunden (erwartet: 1x) — "
                 f"upstream-Provider hat sich geaendert, Patch pruefen.")
    src = src.replace(old, new, 1)

with open(PATH, "w", encoding="utf-8") as f:
    f.write(src)
print("provider.py gepatcht: Lebenszeichen im Analyse-Stream aktiv.")

#!/usr/bin/env python3
"""Testet cpu_pressure.py: Entscheidung (Tabelle), Messung (Fixture-Dateien), Konfiguration und den CLI-Takt.

Aufruf: python3 test/cpu_pressure.test.py  — Exit 0 = alles gut. Nur Standardbibliothek.
Die Aufsicht im entrypoint.sh mit Stub-Providern prüft test/cpu_pressure.test.sh.
"""
import importlib.util
import json
import os
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
SCRIPT = os.path.join(HERE, "..", "cpu_pressure.py")
spec = importlib.util.spec_from_file_location("cpu_pressure", SCRIPT)
cp = importlib.util.module_from_spec(spec)
sys.modules["cpu_pressure"] = cp   # dataclasses sucht das Modul dort
spec.loader.exec_module(cp)

fails = 0


def check(name, cond, detail=""):
    global fails
    if cond:
        print(f"ok   {name}")
    else:
        fails += 1
        print(f"FAIL {name} {detail}")


H, L, M, U = cp.HIGH, cp.LOW, cp.MID, cp.UNKNOWN
CFG = cp.Config()

# ------------------------------------------------------------------------------------- decide (Tabelle)
# (Beschreibung, levels seit der letzten Änderung, limit, cap, total, cfg, now, last_down, erwartet)
OFF = cp.Config(mode="off")
MIN0 = cp.Config(min_background=0)
MIN1 = cp.Config(min_background=1)
MIN3 = cp.Config(min_background=3)
CASES = [
    ("2 Druck-Messungen: eine weniger",          [H, H],          5, 5, 5, CFG, 100, None, 4),
    ("1 Druck-Messung: noch nichts",             [H],             5, 5, 5, CFG, 100, None, 5),
    ("Druck unterbrochen: Folge zählt neu",      [H, M, H],       5, 5, 5, CFG, 100, None, 5),
    ("unbekannte Messung bricht die Folge",      [H, U, H],       5, 5, 5, CFG, 100, None, 5),
    ("Abkühlzeit: Senkung vor 30 s — warten",    [H, H],          4, 5, 5, CFG, 100, 70, 4),
    ("Abkühlzeit vorbei (60 s): weiter senken",  [H, H],          4, 5, 5, CFG, 130, 70, 3),
    ("viele Druck-Messungen: trotzdem nur -1",   [H] * 9,         5, 5, 5, CFG, 100, None, 4),
    ("Vorgabe MIN=0: bis auf die Live-Engine",   [H, H],          2, 5, 5, CFG, 100, None, 1),
    ("Vorgabe MIN=0: Live-Engine bleibt immer",  [H, H],          1, 5, 5, CFG, 100, None, 1),
    ("MIN=0 ausdruecklich wie Vorgabe",          [H, H],          2, 5, 5, MIN0, 100, None, 1),
    ("MIN=1: Live + 1 bleibt",                   [H, H],          2, 5, 5, MIN1, 100, None, 2),
    ("von 0 auf 1: 9 ruhige reichen nicht",      [L] * 9,         1, 5, 5, CFG, 100, None, 1),
    ("von 0 auf 1: nach 10 ruhigen +1",          [L] * 10,        1, 5, 5, CFG, 100, None, 2),
    ("MIN über der Zahl: gedeckelt",             [H, H],          3, 3, 3, MIN3, 100, None, 3),
    ("9 ruhige Messungen: noch nicht anheben",   [L] * 9,         3, 5, 5, CFG, 100, None, 3),
    ("10 ruhige Messungen: eine mehr",           [L] * 10,        3, 5, 5, CFG, 100, None, 4),
    ("ruhig, aber eine mittlere dazwischen",     [L] * 5 + [M] + [L] * 9, 3, 5, 5, CFG, 100, None, 3),
    ("ruhig und schon alle: bleibt",             [L] * 10,        5, 5, 5, CFG, 100, None, 5),
    ("mittlerer Bereich: nichts (Hysterese)",    [M] * 20,        3, 5, 5, CFG, 100, None, 3),
    ("Zeitplan-Deckel: senkt vom Zeitplan aus",  [H, H],          5, 3, 5, CFG, 100, None, 2),
    ("Zeitplan-Deckel: nicht über den Zeitplan", [L] * 10,        3, 3, 5, CFG, 100, None, 3),
    ("Zeitplan 0 %: nichts zu entscheiden",      [H, H],          5, 0, 5, CFG, 100, None, 5),
    ("Zeitplan nur Live: nichts zu senken",      [H, H],          5, 1, 5, CFG, 100, None, 5),
    ("Modus off: immer alle",                    [H, H],          2, 5, 5, OFF, 100, None, 5),
    ("eine einzige Engine: nichts zu tun",       [H, H],          1, 1, 1, CFG, 100, None, 1),
    ("eigene Zähler: HIGH_COUNT=3",              [H, H],          5, 5, 5, cp.Config(high_count=3), 100, None, 5),
    ("eigene Zähler: LOW_COUNT=2",               [L, L],          3, 5, 5, cp.Config(low_count=2), 100, None, 4),
]
for desc, levels, limit, cap, total, cfg, now, last_down, want in CASES:
    got = cp.decide(levels, limit, cap, total, cfg, now, last_down)
    check(f"decide: {desc}", got == want, f"— erwartet {want}, bekommen {got}")

# ------------------------------------------------------------------------------------- classify
def sample(psi_c=None, psi_s=None, load=None, cores=8.0):
    return cp.Sample(psi_cgroup=psi_c, psi_system=psi_s, load1=load, cores=cores)

check("classify: PSI 25 % = Druck",                 cp.classify(sample(25.0, 1.0), CFG) == H)
check("classify: höherer PSI-Wert zählt (System)",  cp.classify(sample(0.0, 30.0), CFG) == H)
check("classify: PSI 24,9 % = Mitte",               cp.classify(sample(24.9, None), CFG) == M)
check("classify: PSI 9,9 % = ruhig",                cp.classify(sample(9.9, 2.0), CFG) == L)
check("classify: mit PSI zählt die Load nicht",     cp.classify(sample(1.0, 1.0, load=100.0), CFG) == L)
check("classify: ohne PSI Load/Kern 1,5 = Druck",   cp.classify(sample(load=12.0, cores=8.0), CFG) == H)
check("classify: ohne PSI Load/Kern 1,0 = Mitte",   cp.classify(sample(load=8.0, cores=8.0), CFG) == M)
check("classify: ohne PSI Load/Kern 0,5 = ruhig",   cp.classify(sample(load=4.0, cores=8.0), CFG) == L)
check("classify: gar nichts messbar = unbekannt",   cp.classify(sample(cores=None), CFG) == U)

# ------------------------------------------------------------------------------------- Messung mit Fixtures
def fixture(files):
    root = tempfile.mkdtemp()
    for path, text in files.items():
        full = os.path.join(root, path.lstrip("/"))
        os.makedirs(os.path.dirname(full), exist_ok=True)
        with open(full, "w") as fh:
            fh.write(text)
    return root

PSI_CG = "some avg10=50.00 avg60=41.20 avg300=12.00 total=123\nfull avg10=0.00 avg60=0.00 avg300=0.00 total=0\n"
PSI_SYS = "some avg10=3.53 avg60=3.21 avg300=12.54 total=17809150387\nfull avg10=0.00 avg60=0.00 avg300=0.00 total=0\n"
LOADAVG = "23.50 15.30 24.08 6/6706 4010833\n"

s = cp.measure(fixture({"/sys/fs/cgroup/cpu.pressure": PSI_CG, "/proc/pressure/cpu": PSI_SYS,
                        "/proc/loadavg": LOADAVG, "/sys/fs/cgroup/cpu.max": "800000 100000\n", "/nproc": "16\n"}))
check("Messung: PSI des cgroups gelesen (avg60)", s.psi_cgroup == 41.2, f"— {s.psi_cgroup}")
check("Messung: PSI systemweit gelesen",          s.psi_system == 3.21, f"— {s.psi_system}")
check("Messung: es zählt der höhere PSI",         s.psi == 41.2)
check("Messung: 1-min-Load gelesen",              s.load1 == 23.5)
check("Messung: Quota 800000/100000 = 8 Kerne",   s.cores == 8.0, f"— {s.cores}")
check("Messung: Load je Kern",                    abs(s.load_per_core - 23.5 / 8) < 1e-9)
check("Messung: Logsatz nennt beide Zahlen",      "41.2 %" in cp.describe(s) and "2.94" in cp.describe(s), cp.describe(s))

s = cp.measure(fixture({"/proc/loadavg": LOADAVG, "/sys/fs/cgroup/cpu.max": "max 100000\n", "/nproc": "16\n"}))
check("Messung ohne PSI: Rückfall auf die Load",  s.psi is None and s.load1 == 23.5)
check("Messung: cpu.max 'max' = keine Quota → nproc", s.cores == 16.0, f"— {s.cores}")
check("Messung ohne PSI: Fehler je Quelle notiert", len(s.errors) == 2, f"— {s.errors}")

s = cp.measure(fixture({"/proc/loadavg": LOADAVG, "/nproc": "4\n"}))
check("Messung: ohne cpu.max gilt nproc",         s.cores == 4.0)
s = cp.measure(fixture({"/proc/loadavg": LOADAVG, "/sys/fs/cgroup/cpu.max": "50000 100000\n", "/nproc": "4\n"}))
check("Messung: halbe CPU als Quota bleibt 0,5",  s.cores == 0.5, f"— {s.cores}")
s = cp.measure(fixture({"/proc/loadavg": LOADAVG, "/sys/fs/cgroup/cpu.max": "1600000 100000\n", "/nproc": "4\n"}))
check("Messung: Quota über den Kernen → Kerne",   s.cores == 4.0, f"— {s.cores}")
s = cp.measure(fixture({"/proc/pressure/cpu": "kaputt\n", "/proc/loadavg": LOADAVG, "/nproc": "8\n"}))
check("Messung: unlesbare PSI-Zeile → Rückfall",  s.psi is None and cp.classify(s, CFG) == H)
check("parse_psi: ohne some-Zeile → None",        cp.parse_psi("full avg60=1.0\n") is None)

# ------------------------------------------------------------------------------------- tick (Zustand, Abfolge)
def run_sequence(levels_psi, total=5, cap=5, cfg=CFG, step=30.0):
    """Spielt eine Folge von PSI-Werten durch; gibt die Höchstzahl nach jeder Messung und die Logzeilen zurück."""
    st, out, logs = {}, [], []
    for n, psi in enumerate(levels_psi):
        st, limit, log = cp.tick(st, cap, total, cfg, now=1000 + n * step, sample_fn=lambda p=psi: sample(p, p))
        st = json.loads(json.dumps(st))      # wie über die Zustandsdatei: muss JSON-fähig sein
        out.append(limit)
        if log:
            logs.append(log)
    return out, logs

seq, logs = run_sequence([40] * 10)
check("tick: Dauerdruck senkt Schritt für Schritt bis nur Live (Vorgabe MIN=0)",
      seq == [5, 4, 4, 3, 3, 2, 2, 1, 1, 1], f"— {seq}")
check("tick: jede Senkung eine Logzeile mit Messwerten",
      len(logs) == 4 and all(l.startswith("CPU-Druck: PSI some avg60 40.0 %") for l in logs), f"— {logs}")
seq, _ = run_sequence([40, 40] + [5] * 10 + [5] * 10)
check("tick: nach Druck erst 10 ruhige Messungen, dann je +1", seq[1] == 4 and seq[11] == 5 and seq[10] == 4, f"— {seq}")
seq, _ = run_sequence([40] * 8 + [5] * 9 + [5])
check("tick: von nur Live zurück auf 1 Hintergrund erst nach 10 ruhigen", seq[7] == 1 and seq[16] == 1 and seq[17] == 2, f"— {seq}")
seq, _ = run_sequence([40, 40, 40, 40] + [15] * 30)
check("tick: zwischen den Schwellen bleibt es (kein Flattern)", seq[-1] == 3 and len(set(seq[4:])) == 1, f"— {seq}")
seq, _ = run_sequence([40, 40] + [5] * 10, step=10.0)
check("tick: Messung nur alle INTERVAL s — Takt 10 s zählt nicht dreifach", seq[1] == 5, f"— {seq}")
seq, logs = run_sequence([40] * 6, cfg=OFF)
check("tick: Modus off — alle, kein Log", seq == [5] * 6 and not logs)
st, limit, log = cp.tick({}, 5, 5, CFG, 0, sample_fn=lambda: cp.Sample(errors=["/proc/loadavg: FileNotFoundError"]))
check("tick: nichts messbar — Zahl bleibt, einmal Hinweis", limit == 5 and log and "keine Messung" in log, f"— {log}")
st, limit, log = cp.tick(st, 5, 5, CFG, 30, sample_fn=lambda: cp.Sample())
check("tick: Hinweis nur einmal", log is None)

# ------------------------------------------------------------------------------------- Konfiguration
check("Config: Vorgaben (MIN=0)", cp.config_from_env({}) == cp.Config() and cp.Config().min_background == 0)
c = cp.config_from_env({"ENGINE_CPU_PRESSURE": "OFF", "ENGINE_CPU_PRESSURE_MIN": "2", "ENGINE_CPU_PRESSURE_HIGH_PSI": "30,5"})
check("Config: off, MIN=2, Dezimalkomma", c.mode == "off" and c.min_background == 2 and c.high_psi == 30.5)
for env, desc in (({"ENGINE_CPU_PRESSURE": "an"}, "Modus Unsinn"),
                  ({"ENGINE_CPU_PRESSURE_MIN": "-1"}, "MIN negativ"),
                  ({"ENGINE_CPU_PRESSURE_LOW_COUNT": "zehn"}, "Zähler keine Zahl"),
                  ({"ENGINE_CPU_PRESSURE_HIGH_COUNT": "0"}, "Zähler 0"),
                  ({"ENGINE_CPU_PRESSURE_LOW_PSI": "30"}, "untere über oberer Schwelle")):
    try:
        cp.config_from_env(env)
        check(f"Config: {desc} abgelehnt", False)
    except cp.ConfigError:
        check(f"Config: {desc} abgelehnt", True)

# ------------------------------------------------------------------------------------- CLI wie aus entrypoint.sh
root = fixture({"/proc/pressure/cpu": "some avg10=60 avg60=60.00 avg300=5 total=1\n", "/proc/loadavg": LOADAVG, "/nproc": "8\n"})
state = os.path.join(tempfile.mkdtemp(), "state.json")
env = dict(os.environ, CPU_PRESSURE_ROOT=root)
lines = []
for now in (0, 30, 60):
    r = subprocess.run([sys.executable, SCRIPT, "tick", "--state", state, "--cap", "4", "--total", "4", "--now", str(now)],
                       capture_output=True, text=True, env=env)
    lines.append(r.stdout.splitlines())
check("CLI: erste Zeile = Höchstzahl", [l[0] for l in lines] == ["4", "3", "3"], f"— {lines}")
check("CLI: Logzeile nur bei Änderung", len(lines[1]) == 2 and "senke auf 3 von 4" in lines[1][1] and len(lines[2]) == 1, f"— {lines}")
r = subprocess.run([sys.executable, SCRIPT, "check"], capture_output=True, text=True,
                   env=dict(os.environ, ENGINE_CPU_PRESSURE_MIN="x"))
check("CLI check: Fehler mit FEHLER-Satz, Exit 1", r.returncode == 1 and r.stderr.startswith("FEHLER: ENGINE_CPU_PRESSURE_MIN"), r.stderr)

print()
print("ALLE TESTS OK" if fails == 0 else f"{fails} FEHLER")
sys.exit(1 if fails else 0)

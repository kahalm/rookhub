#!/usr/bin/env python3
"""CPU-Druck messen und daraus die Zahl der laufenden Engines ableiten (entrypoint.sh, Verwaltungsschleife).

Wozu: Am 09.11.2026 liefen auf dem Server fünf Engines neben Builds und anderen Lasten — Load bis 200, 50 GB
Swap. Jede Engine bekam rund anderthalb Kerne, ein Tiefe-30-Auftrag kam kaum voran, und die Engines bremsten
nebenbei den Rest der Maschine. Mehr Engines als freie Kerne bringen keinen Durchsatz, nur Wartezeit für alle.
Deshalb senkt der Provider die Zahl der HINTERGRUND-Engines bei anhaltendem Druck selbst und hebt sie wieder an,
wenn es ruhig wird. Die Live-Engine (Platz 1) bleibt immer — dort wartet ein Mensch auf eine Stellung.

Messung (je Aufruf von `tick`, höchstens einmal je ENGINE_CPU_PRESSURE_INTERVAL):
  * PSI „some avg60" — der Anteil der letzten Minute, in dem mindestens ein Prozess auf die CPU warten musste.
    Gelesen aus dem eigenen cgroup (/sys/fs/cgroup/cpu.pressure: warten UNSERE Engines?) und systemweit
    (/proc/pressure/cpu: wartet IRGENDWER auf der Maschine?). Es zählt der höhere Wert — beides ist ein Grund
    zurückzustecken: verhungern die eigenen Engines, rechnen sie ohnehin kaum; verhungern die anderen, bremsen wir.
  * Rückfall ohne PSI (älterer Kernel, PSI abgeschaltet): 1-min-Load aus /proc/loadavg je verfügbarem Kern
    (Quota aus cpu.max, sonst die erlaubten Kerne des Prozesses).
  Die eigene Last der Engines zählt mit, das ist gewollt: 17 Engines auf 16 Kernen SIND Druck.

Entscheidung (`decide`, reine Funktion, test/cpu_pressure.test.py):
  * senken um 1, wenn die letzten ENGINE_CPU_PRESSURE_HIGH_COUNT Messungen seit der letzten Änderung über der
    oberen Schwelle lagen und die letzte Senkung ENGINE_CPU_PRESSURE_COOLDOWN Sekunden her ist;
  * anheben um 1, wenn die letzten ENGINE_CPU_PRESSURE_LOW_COUNT Messungen unter der unteren Schwelle lagen;
  * nie unter 1 + ENGINE_CPU_PRESSURE_MIN (Live + Mindestzahl Hintergrund), nie über die Sollzahl des Zeitplans.
  Nach jeder Änderung beginnt die Zählung von vorn — so flattert nichts: zwischen den Schwellen bleibt es, wie es ist.

Aufruf aus entrypoint.sh:  python cpu_pressure.py tick --state <datei> --cap <soll laut zeitplan> --total <alle>
  Ausgabe: erste Zeile die neue Höchstzahl laufender Engines (Live eingeschlossen), danach höchstens eine Logzeile.
  `python cpu_pressure.py check` prüft die Umgebungsvariablen (Exit 1 mit FEHLER-Satz), `measure` zeigt die Messung.
Für Tests: CPU_PRESSURE_ROOT legt die Pfade /sys/fs/cgroup und /proc unter ein Fixture-Verzeichnis, --now die Uhr.
"""
from __future__ import annotations

import argparse
import json
import os
import sys
import time
from dataclasses import dataclass, field

HIGH, LOW, MID, UNKNOWN = "high", "low", "mid", "unknown"


@dataclass(frozen=True)
class Config:
    mode: str = "auto"            # auto | off
    interval: float = 30.0        # Sekunden zwischen zwei Messungen
    high_psi: float = 25.0        # PSI some avg60 in % — ab hier Druck
    low_psi: float = 10.0         # darunter ruhig
    high_load: float = 1.5        # Load/Kern (nur ohne PSI) — ab hier Druck
    low_load: float = 0.9         # darunter ruhig
    high_count: int = 2           # so viele Druck-Messungen in Folge → eine Engine weniger
    low_count: int = 10           # so viele ruhige Messungen in Folge → eine Engine mehr
    cooldown: float = 60.0        # frühestens alle so viele Sekunden senken
    min_background: int = 0       # so viele Hintergrund-Engines bleiben mindestens (Vorgabe 0 = nur Live)


# Name der Umgebungsvariable → (Feld, Typ, kleinster erlaubter Wert)
ENV = {
    "ENGINE_CPU_PRESSURE_INTERVAL":  ("interval", float, 1),
    "ENGINE_CPU_PRESSURE_HIGH_PSI":  ("high_psi", float, 0),
    "ENGINE_CPU_PRESSURE_LOW_PSI":   ("low_psi", float, 0),
    "ENGINE_CPU_PRESSURE_HIGH_LOAD": ("high_load", float, 0),
    "ENGINE_CPU_PRESSURE_LOW_LOAD":  ("low_load", float, 0),
    "ENGINE_CPU_PRESSURE_HIGH_COUNT": ("high_count", int, 1),
    "ENGINE_CPU_PRESSURE_LOW_COUNT": ("low_count", int, 1),
    "ENGINE_CPU_PRESSURE_COOLDOWN":  ("cooldown", float, 0),
    "ENGINE_CPU_PRESSURE_MIN":       ("min_background", int, 0),
}


class ConfigError(ValueError):
    pass


def config_from_env(env=None) -> Config:
    env = os.environ if env is None else env
    values = {}
    mode = (env.get("ENGINE_CPU_PRESSURE") or "auto").strip().lower()
    if mode not in ("auto", "off"):
        raise ConfigError(f"ENGINE_CPU_PRESSURE muss 'auto' oder 'off' sein (ist '{env.get('ENGINE_CPU_PRESSURE')}').")
    values["mode"] = mode
    for name, (fld, typ, minimum) in ENV.items():
        raw = (env.get(name) or "").strip()
        if not raw:
            continue
        try:
            value = typ(raw.replace(",", ".")) if typ is float else typ(raw)
        except ValueError:
            raise ConfigError(f"{name} muss eine {'ganze ' if typ is int else ''}Zahl sein (ist '{raw}').") from None
        if value < minimum:
            raise ConfigError(f"{name} darf nicht kleiner als {minimum} sein (ist '{raw}').")
        values[fld] = value
    cfg = Config(**values)
    if cfg.low_psi > cfg.high_psi or cfg.low_load > cfg.high_load:
        raise ConfigError("ENGINE_CPU_PRESSURE_LOW_* muss unter ENGINE_CPU_PRESSURE_HIGH_* liegen.")
    return cfg


# ------------------------------------------------------------------------------------------------- Messung
@dataclass
class Sample:
    psi_cgroup: float | None = None     # some avg60 des eigenen cgroups, %
    psi_system: float | None = None     # some avg60 systemweit, %
    load1: float | None = None          # 1-min-Load (systemweit)
    cores: float | None = None          # verfügbare Kerne (Quota oder erlaubte CPUs)
    errors: list = field(default_factory=list)

    @property
    def psi(self) -> float | None:
        vals = [v for v in (self.psi_cgroup, self.psi_system) if v is not None]
        return max(vals) if vals else None

    @property
    def load_per_core(self) -> float | None:
        if self.load1 is None or not self.cores:
            return None
        return self.load1 / self.cores


def _root_path(root: str, path: str) -> str:
    return os.path.join(root, path.lstrip("/")) if root else path


def parse_psi(text: str) -> float | None:
    """'some avg10=1.00 avg60=2.50 avg300=… total=…' → 2.5 (Prozent)."""
    for line in text.splitlines():
        parts = line.split()
        if parts and parts[0] == "some":
            for p in parts[1:]:
                if p.startswith("avg60="):
                    return float(p[len("avg60="):])
    return None


def parse_cpu_max(text: str) -> float | None:
    """cgroup v2 cpu.max: 'max 100000' → None (keine Quota), '400000 100000' → 4.0 Kerne."""
    parts = text.split()
    if len(parts) < 1 or parts[0] == "max":
        return None
    quota = float(parts[0])
    period = float(parts[1]) if len(parts) > 1 else 100000.0
    return quota / period if period > 0 and quota > 0 else None


def _read(path: str) -> str:
    with open(path, encoding="ascii", errors="replace") as fh:
        return fh.read()


def available_cores(root: str = "") -> float:
    """Quota aus cpu.max (cgroup v2), sonst die erlaubten CPUs des Prozesses (wie `nproc`)."""
    cores = None
    if root:
        nproc_file = _root_path(root, "/nproc")        # nur Fixtures: die Zahl, die `nproc` meldete
        if os.path.exists(nproc_file):
            cores = float(_read(nproc_file).strip())
    if cores is None:
        try:
            cores = float(len(os.sched_getaffinity(0)))
        except (AttributeError, OSError):
            cores = float(os.cpu_count() or 1)
    try:
        quota = parse_cpu_max(_read(_root_path(root, "/sys/fs/cgroup/cpu.max")))
    except (OSError, ValueError):
        quota = None
    if quota is not None:
        cores = min(cores, quota)            # cpus: 0.5 ergibt 0,5 Kerne — kein Aufrunden auf 1
    return max(cores, 0.01)


def measure(root: str | None = None) -> Sample:
    root = os.environ.get("CPU_PRESSURE_ROOT", "") if root is None else root
    s = Sample()
    for attr, path in (("psi_cgroup", "/sys/fs/cgroup/cpu.pressure"), ("psi_system", "/proc/pressure/cpu")):
        try:
            setattr(s, attr, parse_psi(_read(_root_path(root, path))))
        except (OSError, ValueError) as exc:    # fehlt, oder PSI im Kernel aus (EOPNOTSUPP beim Lesen)
            s.errors.append(f"{path}: {exc.__class__.__name__}")
    try:
        s.load1 = float(_read(_root_path(root, "/proc/loadavg")).split()[0])
    except (OSError, ValueError, IndexError) as exc:
        s.errors.append(f"/proc/loadavg: {exc.__class__.__name__}")
    s.cores = available_cores(root)
    return s


def classify(sample: Sample | None, cfg: Config) -> str:
    """Eine Messung als high/low/mid einordnen. Mit PSI entscheidet PSI allein — die Load zählt auch Prozesse,
    die auf die PLATTE warten (Swap, Builds), und ist auf dem Host gemessen; sie ist nur der Rückfall."""
    if sample is None:
        return UNKNOWN
    psi = sample.psi
    if psi is not None:
        if psi >= cfg.high_psi:
            return HIGH
        return LOW if psi < cfg.low_psi else MID
    lpc = sample.load_per_core
    if lpc is not None:
        if lpc >= cfg.high_load:
            return HIGH
        return LOW if lpc < cfg.low_load else MID
    return UNKNOWN


# ------------------------------------------------------------------------------------------------- Entscheidung
def _trailing(levels, what) -> int:
    n = 0
    for lv in reversed(levels):
        if lv != what:
            break
        n += 1
    return n


def decide(levels, limit: int, cap: int, total: int, cfg: Config, now: float, last_down: float | None) -> int:
    """Neue Höchstzahl laufender Engines (Live eingeschlossen) — REINE Funktion.

    levels    Einordnung der Messungen SEIT DER LETZTEN ÄNDERUNG (älteste zuerst): high/low/mid/unknown
    limit     bisherige Höchstzahl durch den CPU-Druck (1..total)
    cap       Sollzahl laut Zeitplan (0..total) — mehr läuft nie, 0 heißt alles aus
    total     alle Engines des Containers (1 Live + n Hintergrund)
    last_down Zeitpunkt der letzten Senkung (None = noch nie)
    Zielzahl der laufenden Engines ist danach min(cap, Rückgabe).
    """
    if cfg.mode == "off" or total <= 1:
        return total
    limit = max(1, min(limit, total))
    floor = min(total, 1 + cfg.min_background)
    effective = min(limit, cap)
    if cap <= 0:
        return limit                      # Zeitplan hält ohnehin alles an — nichts zu entscheiden
    if (_trailing(levels, HIGH) >= cfg.high_count and effective > floor
            and (last_down is None or now - last_down >= cfg.cooldown)):
        return effective - 1
    if _trailing(levels, LOW) >= cfg.low_count and limit < min(cap, total):
        return limit + 1
    return limit


# ------------------------------------------------------------------------------------------------- Zustand + CLI
def describe(sample: Sample) -> str:
    def num(v, digits=1):
        return "–" if v is None else f"{v:.{digits}f}"
    parts = [f"PSI some avg60 {num(sample.psi)} % (cgroup {num(sample.psi_cgroup)}, System {num(sample.psi_system)})"]
    parts.append(f"Load {num(sample.load1, 2)} / {num(sample.cores)} Kerne = {num(sample.load_per_core, 2)}")
    return ", ".join(parts)


def tick(state: dict, cap: int, total: int, cfg: Config, now: float, sample_fn=measure):
    """Ein Takt der Verwaltungsschleife: misst, wenn eine Messung fällig ist, und entscheidet.
    Gibt (neuer Zustand, Höchstzahl, Logzeile oder None) zurück."""
    st = dict(state)
    st.setdefault("limit", total)
    st.setdefault("levels", [])
    st.setdefault("last_down", None)
    st.setdefault("last_measure", None)
    limit = max(1, min(int(st["limit"]), total))
    if cfg.mode == "off" or total <= 1:
        st["limit"] = total
        return st, total, None
    # Toleranz von einer Sekunde: ein Takt von 15 s soll bei einem Intervall von 30 s jede zweite Runde messen,
    # auch wenn der Schlaf ein paar Millisekunden zu kurz war.
    if st["last_measure"] is not None and now - st["last_measure"] < cfg.interval - 1:
        return st, limit, None
    sample = sample_fn()
    st["last_measure"] = now
    level = classify(sample, cfg)
    log = None
    if level == UNKNOWN and not st.get("warned_unknown"):
        st["warned_unknown"] = True
        log = ("CPU-Druck: keine Messung möglich (" + "; ".join(sample.errors or ["keine Quelle"]) +
               ") — die Zahl der Engines bleibt, wie sie ist.")
    levels = (st["levels"] + [level])[-max(cfg.high_count, cfg.low_count):]
    new = decide(levels, limit, cap, total, cfg, now, st["last_down"])
    if new != limit:
        before = min(limit, cap)
        after = min(new, cap)
        if new < limit:
            st["last_down"] = now
            why = f"{cfg.high_count} Messungen in Folge über der Schwelle"
            verb = "senke"
        else:
            why = f"{cfg.low_count} Messungen in Folge ruhig"
            verb = "hebe"
        log = (f"CPU-Druck: {describe(sample)} — {verb} auf {after} von {total} Engine(s) "
               f"(vorher {before}; {why}).")
        levels = []
    st["levels"] = levels
    st["limit"] = new
    st["last"] = {"psi": sample.psi, "load_per_core": sample.load_per_core, "level": level}
    return st, new, log


def _load_state(path: str) -> dict:
    try:
        with open(path, encoding="utf-8") as fh:
            data = json.load(fh)
        return data if isinstance(data, dict) else {}
    except (OSError, ValueError):
        return {}


def _save_state(path: str, state: dict) -> None:
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8") as fh:
        json.dump(state, fh)
    os.replace(tmp, path)


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n", 1)[0])
    sub = ap.add_subparsers(dest="cmd", required=True)
    t = sub.add_parser("tick")
    t.add_argument("--state", required=True)
    t.add_argument("--cap", type=int, required=True)
    t.add_argument("--total", type=int, required=True)
    t.add_argument("--now", type=float)
    sub.add_parser("check")
    sub.add_parser("measure")
    args = ap.parse_args(argv)
    try:
        cfg = config_from_env()
    except ConfigError as exc:
        print(f"FEHLER: {exc}", file=sys.stderr)
        return 1
    if args.cmd == "check":
        return 0
    if args.cmd == "measure":
        s = measure()
        print(f"{describe(s)} → {classify(s, cfg)}")
        return 0
    now = time.monotonic() if args.now is None else args.now
    state, limit, log = tick(_load_state(args.state), args.cap, args.total, cfg, now)
    _save_state(args.state, state)
    print(limit)
    if log:
        print(log)
    return 0


if __name__ == "__main__":
    sys.exit(main())

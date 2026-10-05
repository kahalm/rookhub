#!/opt/venv/bin/python3
"""Zwischenschalter zwischen dem Lichess/RookHub-Provider und Lc0.

Der Provider spricht UCI so, wie Stockfish es versteht. Lc0 ist bei drei Dingen strenger bzw. anders,
und ohne diese Übersetzung stünde in jedem Auftrag eine Fehlerzeile:

* ``setoption name Hash`` — Lc0 hat keinen Hash. Unbekannte Optionen beantwortet Lc0 mit
  ``error Unknown option``. Der Hash-Wert (MB) wird deshalb VERWORFEN; Lc0s eigener Zwischenspeicher
  (NNCacheSize) wird über LC0_ARGS eingestellt, nicht pro Auftrag.
* ``UCI_AnalyseMode`` und ``UCI_Variant`` kennt Lc0 nicht (es spielt nur Standardschach) — verworfen.
* ``setoption name Threads`` — bei Lc0 sind das SUCH-Threads, die die GPU füttern, nicht Rechenkerne.
  Mehr als zwei bis drei bringen nichts; der Wert wird auf LC0_MAX_THREADS gedeckelt (Vorgabe 2).

Alles andere geht unverändert durch, in beide Richtungen.
"""
import os
import shlex
import subprocess
import sys
import threading

DROP = {"hash", "uci_analysemode", "uci_variant"}


def build_command() -> list[str]:
    engine = os.environ.get("LC0_BINARY", "/opt/lc0/lc0")
    cmd = [engine, f"--weights={os.environ.get('LC0_WEIGHTS', '/opt/lc0/net/default.pb.gz')}",
           f"--backend={os.environ.get('LC0_BACKEND', 'cuda-fp16')}"]
    if os.environ.get("LC0_BACKEND_OPTS"):
        cmd.append(f"--backend-opts={os.environ['LC0_BACKEND_OPTS']}")
    cmd += shlex.split(os.environ.get("LC0_ARGS", ""))
    return cmd


def rewrite(line: str, max_threads: int) -> str | None:
    """Zeile vom Provider → Zeile an Lc0 (None = verwerfen)."""
    parts = line.split()
    if len(parts) >= 3 and parts[0] == "setoption" and parts[1] == "name":
        try:
            vi = parts.index("value")
        except ValueError:
            vi = len(parts)
        name = " ".join(parts[2:vi])
        value = " ".join(parts[vi + 1:])
        if name.lower() in DROP:
            return None
        if name.lower() == "threads":
            try:
                n = max(1, min(int(value), max_threads))
            except ValueError:
                return None
            return f"setoption name Threads value {n}"
    return line


def main() -> int:
    max_threads = int(os.environ.get("LC0_MAX_THREADS", "2"))
    proc = subprocess.Popen(build_command(), stdin=subprocess.PIPE, stdout=sys.stdout, stderr=sys.stderr,
                            text=True, bufsize=1)

    def pump() -> None:
        try:
            for raw in sys.stdin:
                out = rewrite(raw.rstrip("\n"), max_threads)
                if out is None:
                    continue
                proc.stdin.write(out + "\n")
                proc.stdin.flush()
                if out.strip() == "quit":
                    break
        except (BrokenPipeError, ValueError):
            pass
        finally:
            try:
                proc.stdin.close()
            except Exception:
                pass

    threading.Thread(target=pump, daemon=True).start()
    return proc.wait()


if __name__ == "__main__":
    sys.exit(main())

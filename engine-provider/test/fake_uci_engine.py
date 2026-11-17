#!/usr/bin/env python3
"""Schein-UCI-Engine fuer test/provider_offset.test.py — verhaelt sich in den fuer den Provider wichtigen
Punkten wie Stockfish:

- `go …` startet eine Suche in einem eigenen Faden: je Tiefe eine `info`-Zeile mit `score`, am Ende
  (Tiefe erreicht oder `stop`) genau ein `bestmove`. Laeuft noch eine Suche, wartet `go` auf deren Ende
  (Stockfish: `wait_for_search_finished`), ohne sie abzubrechen.
- `stop` ausserhalb einer Suche bleibt ohne Antwort (kein streunendes `bestmove`).
- `isready` wird SOFORT beantwortet, auch waehrend einer Suche.
- `ucinewgame` wartet auf das Ende der laufenden Suche (Stockfish: `search_clear`), bricht sie nicht ab.

Die Zeilen machen die Stellung erkennbar, zu der sie gehoeren: das erste Feld der `pv` (und des
`bestmove`) ist ein Feld, auf dem eine Figur der Seite am Zug steht — der Test prueft das gegen die FEN
des eigenen Auftrags. Dauer je Tiefe: die ersten FAKE_UCI_BURST (200) Tiefen ohne Pause, danach FAKE_UCI_STEP Sekunden (0.05).
"""
import os
import sys
import threading
import time

STEP = float(os.environ.get("FAKE_UCI_STEP", "0.05"))
# Die ersten Tiefen kommen wie bei Stockfish im Schwall (ohne Pause, in einem Stueck in der Pipe): dann
# liegt beim Provider immer schon die naechste Zeile bereit, und ein Abriss trifft ihn beim SCHREIBEN
# einer Zeile statt beim Warten auf die naechste — genau der Fall, in dem nichts mehr aufraeumt.
BURST = int(os.environ.get("FAKE_UCI_BURST", "200"))
out_lock = threading.Lock()


def say(line):
    with out_lock:
        sys.stdout.write(line + "\n")
        sys.stdout.flush()


def own_square(fen):
    """Erstes Feld (a8 … h1) mit einer Figur der Seite am Zug."""
    board, side = fen.split()[0], fen.split()[1]
    for r, row in enumerate(board.split("/")):
        f = 0
        for ch in row:
            if ch.isdigit():
                f += int(ch)
                continue
            if (ch.isupper() and side == "w") or (ch.islower() and side == "b"):
                return "abcdefgh"[f] + str(8 - r)
            f += 1
    return "a1"


class Search:
    def __init__(self, square, depth):
        self.stop = threading.Event()
        self.thread = threading.Thread(target=self.run, args=(square, depth), daemon=True)
        self.thread.start()

    def run(self, square, depth):
        move = square + square           # absichtlich kein legaler Zug: nur das Feld zaehlt
        for d in range(1, depth + 1):
            if d > BURST and self.stop.wait(STEP):
                break
            if self.stop.is_set():
                break
            say(f"info depth {d} seldepth {d} multipv 1 score cp {d} nodes {d * 100} nps 1000 time {d} pv {move}")
        say(f"bestmove {move}")


def main():
    fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1"
    search = None
    while True:
        line = sys.stdin.readline()
        if not line:
            break
        cmd = line.strip()
        if cmd == "uci":
            say("id name FakeUCI")
            say("option name MultiPV type spin default 1 min 1 max 500")
            say("uciok")
        elif cmd == "isready":
            say("readyok")
        elif cmd == "ucinewgame":
            if search:
                search.thread.join()
        elif cmd.startswith("position fen "):
            parts = cmd[len("position fen "):].split(" moves")[0]
            fen = parts.strip()
        elif cmd.startswith("go"):
            if search:
                search.thread.join()
            words = cmd.split()
            depth = 1000
            if "depth" in words:
                depth = int(words[words.index("depth") + 1])
            elif "movetime" in words:
                depth = max(1, int(int(words[words.index("movetime") + 1]) / 1000 / STEP))
            search = Search(own_square(fen), depth)
        elif cmd == "stop":
            if search and search.thread.is_alive():
                search.stop.set()
        elif cmd == "quit":
            break
    if search:
        search.stop.set()
        search.thread.join()


if __name__ == "__main__":
    main()

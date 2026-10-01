#!/usr/bin/env python3
"""Tests fuer scripts/prep-import.py — ohne Server und ohne Docker (Fake-urlopen, Zeilen aus dem Speicher).

Prueft die Zusagen des Skripts:
  * Partiegrenzen wie auf dem Server (CRLF + BOM der Megabase, wiederholter Kopf ohne Zuege, „[" im offenen Kommentar)
  * nur die gebrauchten Kopfzeilen gehen mit
  * Paketnummern haengen nur an der Partienummer — Stichprobe (--every/--offset) und Fortsetzen aendern sie nicht
  * schon eingespielte Pakete werden uebersprungen, eine andere Paketgroesse bricht ab
  * 429 wartet (Retry-After), 5xx versucht erneut, 409 bricht ab, 401 meldet sich neu an; gedrosselt wird je Anfrage

    python3 scripts/tests/test_prep_import.py
"""
import gzip
import importlib.util
import io
import json
import os
import sys
import unittest
import urllib.error

HERE = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location("prep_import", os.path.join(HERE, "..", "prep-import.py"))
pi = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(pi)


def game(white, black, moves="1. e4 e5 2. Nf3 Nc6 1-0", extra=""):
    return (f'[Event "Open"]\n[Site "Schwaz"]\n[Date "2024.05.17"]\n[White "{white}"]\n[Black "{black}"]\n'
            f'[Result "1-0"]\n[GameId "12345"]\n[SourceTitle "Mega"]\n{extra}\n{moves}\n\n')


def lines(text):
    return io.BytesIO(text.encode("utf-8")).readlines()


def args(**kw):
    base = ["x.pgn", "--source", "Mega", "--api", "http://api"]
    a = pi.parse_args(base)
    for k, v in kw.items():
        setattr(a, k, v)
    return a


class SplitTests(unittest.TestCase):
    def test_crlf_and_bom_two_games(self):
        text = ("﻿" + game("A, B", "C, D") + game("E, F", "G, H")).replace("\n", "\r\n")
        got = list(pi.split_games(lines(text), lambda i: True))
        self.assertEqual([0, 1], [i for i, _ in got])
        heads, moves = got[0][1]
        self.assertEqual(b'[Event "Open"]', heads[0])
        self.assertEqual([b"1. e4 e5 2. Nf3 Nc6 1-0"], moves)

    def test_only_needed_headers(self):
        heads, _ = next(pi.split_games(lines(game("A, B", "C, D", extra='[WhiteFideId "990001"]\n')), lambda i: True))[1]
        keys = [h[1:h.index(b" ")] for h in heads]
        self.assertIn(b"WhiteFideId", keys)
        self.assertNotIn(b"GameId", keys)
        self.assertNotIn(b"SourceTitle", keys)

    def test_repeated_header_without_moves_starts_next_game(self):
        text = '[Event "leer"]\n[White "A"]\n\n' + game("A, B", "C, D")
        got = list(pi.split_games(lines(text), lambda i: True))
        self.assertEqual(2, len(got))

    def test_bracket_line_inside_open_comment_is_not_a_header(self):
        text = '[White "A"]\n[Black "B"]\n\n1. e4 {Kommentar\n[Event "kein Kopf"]\nweiter} e5 1-0\n\n' + game("E, F", "G, H")
        got = list(pi.split_games(lines(text), lambda i: True))
        self.assertEqual(2, len(got))
        self.assertEqual([b"1. e4", b"e5 1-0"], got[0][1][1])      # Kommentar samt „Kopfzeile" darin ist weg

    def test_brace_inside_comment_does_not_nest_lumbra(self):
        # Lumbra, Zeile 31 859 533: wer Klammern zählt, hält danach jede Kopfzeile der Datei für Kommentar.
        text = ('[White "Gregory, Keith D F"]\n[Black "Goodfellow, Russell Robert"]\n\n'
                '44. Qb4 Qd7 45. Qb1 { {time | (time } 1-0\n\n') + game("Chilla, Jan Eric", "Pfleger, Sascha") + game("E, F", "G, H")
        got = list(pi.split_games(lines(text), lambda i: True))
        self.assertEqual(3, len(got))
        self.assertEqual([b"44. Qb4 Qd7 45. Qb1   1-0"], got[0][1][1])

    def test_unterminated_comment_ends_at_header_after_blank_line(self):
        text = '[White "A"]\n[Black "B"]\n\n1. e4 {nie zu 1-0\n\n' + game("E, F", "G, H") + game("I, J", "K, L")
        got = list(pi.split_games(lines(text), lambda i: True))
        self.assertEqual(3, len(got))
        self.assertEqual([b"1. e4"], got[0][1][1])

    def test_strip_comments(self):
        self.assertEqual((b"1. e4   e5", False), pi.strip_comments(b"1. e4 {gut} e5", False))
        self.assertEqual((b"1. e4  ", True), pi.strip_comments(b"1. e4 {offen", False))
        self.assertEqual((b" e5", False), pi.strip_comments(b"weiter} e5", True))
        self.assertEqual((b"1. e4 ", False), pi.strip_comments(b"1. e4 ; Rest {der Zeile", False))
        self.assertEqual((b"1. d4", False), pi.strip_comments(b"1. d4", False))

    def test_unwanted_games_counted_not_kept(self):
        text = "".join(game(f"P{i}, X", "Q, Y") for i in range(5))
        got = list(pi.split_games(lines(text), lambda i: i == 3))
        self.assertEqual([None, None, None], [g for _, g in got[:3]])
        self.assertIsNotNone(got[3][1])
        self.assertEqual(5, len(got))


class ChunkTests(unittest.TestCase):
    def chunks(self, n, size, selected):
        text = "".join(game(f"P{i}, X", "Q, Y") for i in range(n))
        return [(no, first, len(g)) for no, first, g in pi.Chunker(size, selected).chunks(lines(text))]

    def test_numbering_and_last_partial_chunk(self):
        self.assertEqual([(0, 0, 4), (1, 4, 4), (2, 8, 2)], self.chunks(10, 4, lambda n: True))

    def test_sample_every_k_keeps_numbers(self):
        self.assertEqual([(1, 3, 3), (3, 9, 3)], self.chunks(12, 3, lambda n: n % 2 == 1))

    def test_chunk_text_round_trip(self):
        _, _, games = next(pi.Chunker(5, lambda n: True).chunks(lines(game("A, B", "C, D"))))
        self.assertEqual(b'[Event "Open"]\n[Site "Schwaz"]\n[Date "2024.05.17"]\n[White "A, B"]\n[Black "C, D"]\n'
                         b'[Result "1-0"]\n\n1. e4 e5 2. Nf3 Nc6 1-0\n\n', pi.chunk_text(games))


class FakeResponse:
    def __init__(self, body):
        self.body = json.dumps(body).encode()

    def __enter__(self):
        return self

    def __exit__(self, *a):
        return False

    def read(self):
        return self.body


def http_error(code, body="", headers=None):
    return urllib.error.HTTPError("http://api", code, "x", headers or {}, io.BytesIO(body.encode()))


class FakeServer:
    """Antwortet wie /api/prep/admin/*; `script` liefert je Aufruf eine Ausnahme statt der Antwort."""

    def __init__(self, done=None, script=None):
        self.done = dict(done or {})
        self.script = list(script or [])
        self.calls = []

    def __call__(self, req, timeout=None):
        self.calls.append((req.get_method(), req.full_url, dict(req.header_items())))
        if self.script:
            e = self.script.pop(0)
            if e is not None:
                raise e
        url = req.full_url
        if "/api/auth/login" in url:
            return FakeResponse({"token": "jwt-neu"})
        if "/api/prep/admin/imports" in url:
            return FakeResponse({"chunks": [{"chunk": c, "first": f} for c, f in self.done.items()]})
        q = dict(p.split("=") for p in url.split("?", 1)[1].split("&"))
        n = gzip.decompress(req.data).count(b"[White ")
        return FakeResponse({"read": n, "added": n, "duplicates": 0, "discarded": 0, "millis": 5, "already": False,
                             "chunk": int(q["chunk"]), "first": int(q["first"])})


class ApiTests(unittest.TestCase):
    def api(self, server, **kw):
        self.slept = []
        return pi.Api("http://api", token="jwt", sleep=self.slept.append, opener=server, **kw)

    def test_429_waits_retry_after(self):
        server = FakeServer(script=[http_error(429, headers={"Retry-After": "7"})])
        self.api(server).done_chunks("Mega")
        self.assertIn(7, self.slept)
        self.assertEqual(2, len(server.calls))

    def test_5xx_retries_then_gives_up(self):
        server = FakeServer(script=[http_error(502)] * 3)
        with self.assertRaises(pi.ApiError):
            self.api(server, retries=2).done_chunks("Mega")
        self.assertEqual(3, len(server.calls))

    def test_409_aborts_at_once(self):
        server = FakeServer(script=[http_error(409, '{"reason":"chunkMismatch"}')])
        with self.assertRaises(pi.ApiError) as ctx:
            self.api(server).post_chunk("Mega", 1, 5000, gzip.compress(b""))
        self.assertIn("409", str(ctx.exception))
        self.assertEqual(1, len(server.calls))

    def test_401_logs_in_again(self):
        server = FakeServer(script=[http_error(401)])
        api = self.api(server, user="admin", password="pw")
        api.done_chunks("Mega")
        self.assertEqual("jwt-neu", api.token)
        self.assertEqual("Bearer jwt-neu", server.calls[-1][2]["Authorization"])

    def test_throttle_between_requests(self):
        server = FakeServer()
        api = self.api(server, rate=6)          # eine Anfrage je 10 s
        api.done_chunks("Mega")
        api.done_chunks("Mega")
        self.assertTrue(any(s > 9 for s in self.slept))

    def test_post_sends_gzip_and_chunk_numbers(self):
        server = FakeServer()
        self.api(server).post_chunk("Lumbra", 3, 15000, gzip.compress(b'[White "A"]\n\n1. e4 *\n\n'))
        method, url, headers = server.calls[-1]
        self.assertEqual("POST", method)
        self.assertIn("source=Lumbra&chunk=3&first=15000", url)
        self.assertEqual("gzip", headers["Content-encoding"])


class RunTests(unittest.TestCase):
    def run_script(self, server, n=10, **kw):
        a = args(chunk_size=3, **kw)
        api = pi.Api("http://api", token="jwt", sleep=lambda s: None, opener=server, rate=0)
        text = "".join(game(f"P{i}, X", "Q, Y") for i in range(n))
        out = io.StringIO()
        totals = pi.run(a, api, lines=lines(text), out=out)
        posted = [c[1] for c in server.calls if c[0] == "POST"]
        return totals, posted

    def test_resume_skips_done_chunks(self):
        totals, posted = self.run_script(FakeServer(done={0: 0, 1: 3}))
        self.assertEqual(2, totals["chunks"])
        self.assertTrue(posted[0].endswith("chunk=2&first=6"))
        self.assertTrue(posted[1].endswith("chunk=3&first=9"))
        self.assertEqual(4, totals["added"])

    def test_other_chunk_size_on_server_aborts(self):
        with self.assertRaises(pi.ApiError):
            self.run_script(FakeServer(done={1: 5000}))

    def test_sample_and_max_chunks(self):
        totals, posted = self.run_script(FakeServer(), n=30, every=3, offset=1, max_chunks=2)
        self.assertEqual(2, totals["chunks"])
        self.assertIn("chunk=1&first=3", posted[0])
        self.assertIn("chunk=4&first=12", posted[1])


if __name__ == "__main__":
    unittest.main()

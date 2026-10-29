#!/usr/bin/env python3
"""Tests fuer tools/DiagramWorker/worker.py — ohne Server und ohne Docker (Fake-HTTP, Fake-rookhub-diagrams).

Prueft die Zusagen des Workers:
  * Aufruf von rookhub-diagrams mit den Rollen des Auftrags (primary/reading/epub, Ergaenzen, Threads)
  * Pausen nach /claim: 204 kurz, Token-/Versionsfehler eine Stunde, sonst 5 min
  * Token aus Datei (Docker-Secret) vor Umgebungsvariable
  * Erfolg: Ergebnis an /result; Programmfehler: Buch mit Fehlertext zurueckgeben; Ausnahme: Buch ohne Fehler freigeben
  * Bretter (0.874.0): Brett-Auftraege gehen vor; Upload als cells.npz/prep.json/crops.zip im Format des Laptop-Workers

    python3 scripts/tests/test_diagram_worker.py
"""
import importlib.util
import io
import json
import os
import stat
import sys
import tempfile
import types
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))

# requests ist auf dem CI-Runner nicht garantiert — der Worker braucht davon nur Session/Exceptions, die Tests ersetzen rh().
if 'requests' not in sys.modules:
    try:
        import requests  # noqa: F401
    except ImportError:
        fake = types.ModuleType('requests')
        class RequestException(Exception): pass
        class HTTPError(RequestException):
            def __init__(self, *a, response=None):
                super().__init__(*a); self.response = response
        class Session:
            def __init__(self): self.headers = {}
        fake.RequestException, fake.HTTPError, fake.Session = RequestException, HTTPError, Session
        sys.modules['requests'] = fake

_spec = importlib.util.spec_from_file_location('diagram_worker', os.path.join(HERE, '..', '..', 'tools', 'DiagramWorker', 'worker.py'))
w = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(w)


class Resp:
    def __init__(self, status=200, body=None, data=b''):
        self.status_code, self._body, self._data, self.text = status, body, data, json.dumps(body) if body else ''
    def json(self): return self._body
    def raise_for_status(self): pass
    def iter_content(self, n): yield self._data
    def __enter__(self): return self
    def __exit__(self, *a): return False


def fake_cli(dirname, exit_code, result=None):
    """Kleines Shell-Programm statt rookhub-diagrams: schreibt (optional) --out und endet mit exit_code."""
    path = os.path.join(dirname, 'fake-cli.sh')
    body = '#!/bin/sh\nout=""\nwhile [ $# -gt 0 ]; do [ "$1" = --out ] && out="$2"; shift; done\n'
    if result is not None:
        body += f"printf '%s' '{json.dumps(result)}' > \"$out\"\n"
    body += f'echo "Protokoll"\nexit {exit_code}\n'
    with open(path, 'w') as f: f.write(body)
    os.chmod(path, os.stat(path).st_mode | stat.S_IEXEC)
    return path


JOB = {'jobId': 'j1', 'ebookId': 42, 'title': 'Buch', 'heartbeatSeconds': 600, 'supplement': False,
       'files': [{'fileId': 1, 'role': 'primary', 'format': 'pdf', 'url': 'api/diagram-worker/jobs/j1/files/1'}]}


class WorkerTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.mkdtemp()
        self.calls = []
        self._rh, self._cli, self._work, self._sleep = w.rh, w.CLI, w.WORK, w.time.sleep
        w.WORK = os.path.join(self.tmp, 'work')
        w.time.sleep = lambda s: None

    def tearDown(self):
        w.rh, w.CLI, w.WORK, w.time.sleep = self._rh, self._cli, self._work, self._sleep

    def fake_rh(self, result_status=200):
        def rh(method, path, **kw):
            self.calls.append((method, path, kw.get('json')))
            if path.endswith('/result'): return Resp(result_status, {'diagramCount': 7, 'state': 'Done'})
            if '/files/' in path: return Resp(200, data=b'%PDF-1.4')
            return Resp(204)
        return rh

    def test_command_carries_every_role_and_supplement(self):
        job = dict(JOB, supplement=True)
        paths = {'primary': ('/w/primary.pdf', 'pdf'), 'reading': ('/w/reading.pdf', 'pdf'), 'epub': ('/w/epub.epub', 'epub')}
        cmd = w.build_command(job, paths, '/w/result.json')
        self.assertEqual(cmd[:5], [w.CLI, '--ebook-id', '42', '--primary', '/w/primary.pdf'])
        for flag, value in [('--format', 'pdf'), ('--reading', '/w/reading.pdf'), ('--epub', '/w/epub.epub'), ('--out', '/w/result.json'), ('--threads', str(w.THREADS))]:
            self.assertEqual(cmd[cmd.index(flag) + 1], value)
        self.assertIn('--supplement', cmd)
        self.assertNotIn('--supplement', w.build_command(JOB, {'primary': ('/p.pdf', 'pdf')}, '/r'))

    def test_pauses_after_claim(self):
        self.assertIsNone(w.pause_for(200))
        self.assertEqual(w.pause_for(204), w.IDLE)
        for status in (401, 403, 404):
            self.assertEqual(w.pause_for(status), 3600)
        self.assertEqual(w.pause_for(500), 300)

    def test_token_file_wins_over_environment(self):
        path = os.path.join(self.tmp, 'token')
        with open(path, 'w') as f: f.write('rkh_aus_datei\n')
        old = dict(os.environ)
        try:
            os.environ['ROOKHUB_TOKEN'] = 'rkh_aus_env'
            os.environ['ROOKHUB_TOKEN_FILE'] = path
            self.assertEqual(w.token(), 'rkh_aus_datei')
            del os.environ['ROOKHUB_TOKEN_FILE']
            self.assertEqual(w.token(), 'rkh_aus_env')
        finally:
            os.environ.clear(); os.environ.update(old)

    def test_success_posts_the_result(self):
        w.CLI = fake_cli(self.tmp, 0, {'ebookId': 42, 'diagrams': []})
        w.rh = self.fake_rh()
        w.run_job(JOB, '0.873.0')
        posted = [c for c in self.calls if c[1].endswith('/result')]
        self.assertEqual(len(posted), 1)
        self.assertFalse([c for c in self.calls if c[1].endswith('/release')])
        self.assertFalse(os.path.exists(os.path.join(w.WORK, 'd42')))   # Arbeitsordner aufgeraeumt

    def test_cli_failure_returns_the_book_with_the_error(self):
        w.CLI = fake_cli(self.tmp, 2)
        w.rh = self.fake_rh()
        w.run_job(JOB, '0.873.0')
        released = [c for c in self.calls if c[1].endswith('/release')]
        self.assertEqual(len(released), 1)
        self.assertIn('rookhub-diagrams exit 2', released[0][2]['error'])
        self.assertFalse([c for c in self.calls if c[1].endswith('/result')])

    def test_unexpected_error_frees_the_book_without_blaming_it(self):
        w.CLI = os.path.join(self.tmp, 'gibt-es-nicht')
        w.rh = self.fake_rh()
        w.run_job(JOB, '0.873.0')
        released = [c for c in self.calls if c[1].endswith('/release')]
        self.assertEqual(released, [('POST', 'api/diagram-worker/jobs/j1/release', {})])


try:
    import numpy as np
except ImportError:
    np = None


class BoardTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.mkdtemp()
        self._rh, self._work, self._sleep = w.rh, w.WORK, w.time.sleep
        w.WORK = os.path.join(self.tmp, 'work')
        w.time.sleep = lambda s: None

    def tearDown(self):
        w.rh, w.WORK, w.time.sleep = self._rh, self._work, self._sleep

    def test_boards_go_first_then_diagrams(self):
        order = []
        old_b, old_d = w.board_once, w.diagram_once
        try:
            w.board_once = lambda: order.append('board') or False
            w.diagram_once = lambda v: order.append('diagram') or False
            (w.board_once() or w.diagram_once('x'))
        finally:
            w.board_once, w.diagram_once = old_b, old_d
        self.assertEqual(order, ['board', 'diagram'])

    def test_board_claim_without_work_does_nothing(self):
        w.rh = lambda m, p, **kw: Resp(204)
        self.assertFalse(w.board_once())

    @unittest.skipIf(np is None, 'numpy fehlt')
    def test_upload_parts_have_the_laptop_format(self):
        import zipfile
        work = os.path.join(self.tmp, 'b1'); os.makedirs(os.path.join(work, 'crops'))
        open(os.path.join(work, 'crops', '11.png'), 'wb').write(b'png')
        res = [(np.zeros((64, 32, 32), np.uint8), 11, '8/8/8/8/8/8/8/K6k w - - 0 1', 0.9, (3, 0.1, 0.1, 0.3, 0.3))]
        files = w.cut_files(res, {'diagrams': 1, 'source': '/lib/b.pdf'}, 1, work)
        d = np.load(io.BytesIO(files['cells'][1]))
        self.assertEqual(d['cells'].shape, (1, 64, 32, 32))
        self.assertEqual(d['cells'].dtype, np.uint8)
        self.assertEqual(list(d['ids']), [11])
        self.assertEqual(d['pos'].shape, (1, 5))
        self.assertEqual(json.loads(files['prep'][1])['cut'], 1)
        self.assertEqual(zipfile.ZipFile(io.BytesIO(files['crops'][1])).namelist(), ['11.png'])
        empty = np.load(io.BytesIO(w.cut_files([], {'diagrams': 3}, 2, work)['cells'][1]))
        self.assertEqual(empty['cells'].shape, (0, 64, 32, 32))


if __name__ == '__main__':
    unittest.main()

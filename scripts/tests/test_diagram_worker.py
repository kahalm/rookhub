#!/usr/bin/env python3
"""Tests fuer tools/DiagramWorker/worker.py — ohne Server und ohne Docker (Fake-HTTP, Fake-rookhub-diagrams).

Prueft die Zusagen des Workers:
  * Aufruf von rookhub-diagrams mit den Rollen des Auftrags (primary/reading/epub, Ergaenzen, Threads)
  * Pausen nach /claim: 204 kurz, Token-/Versionsfehler eine Stunde, sonst 5 min
  * Token aus Datei (Docker-Secret) vor Umgebungsvariable
  * Erfolg: Ergebnis an /result; Programmfehler: Buch mit Fehlertext zurueckgeben; Ausnahme: Buch ohne Fehler freigeben

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


if __name__ == '__main__':
    unittest.main()

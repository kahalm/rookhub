#!/usr/bin/env python3
"""Tests fuer scripts/es_log_retention.py — gegen ein Fake-urlopen, kein ES noetig.

Prueft die Kernzusagen des Skripts:
  * request() wirft bei HTTP-Status != 2xx (EsError) statt still weiterzulaufen
  * Erfolg wird erst NACH dem Zuruecklesen (Policy/Template/Stream-Settings) gemeldet
  * ein PUT ohne Wirkung (Readback zeigt die Policy nicht) => Exit-Code != 0
  * ein Fehlstatus beim Listen der Templates => Exit-Code != 0 (frueher: leise Erfolg)
  * --dry-run schreibt garantiert nichts (kein einziger PUT)
  * „nichts gefunden“ ist kein Erfolg: kein Sink-Template, kein Log-Data-Stream oder ein
    Stream auf einem Template ohne Policy => Exit-Code != 0 (fuer den monatlichen Timer)

    python3 scripts/tests/test_es_log_retention.py
    ES_LOG_RETENTION_SCRIPT=/pfad/zu/alt.py python3 scripts/tests/test_es_log_retention.py
"""
import importlib.util
import io
import json
import os
import sys
import unittest
import urllib.error
from contextlib import redirect_stderr, redirect_stdout
from unittest import mock

HERE = os.path.dirname(os.path.abspath(__file__))
SCRIPT = os.environ.get("ES_LOG_RETENTION_SCRIPT",
                        os.path.join(HERE, "..", "es_log_retention.py"))
_spec = importlib.util.spec_from_file_location("es_log_retention", SCRIPT)
elr = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(elr)

ES = "http://es.test:9200"
POLICY = "rookhub-logs-retention"
TPL = "rookhub-logs-generic-8.11.0"
STREAM = "rookhub-logs-generic-default"


class _Resp:
    def __init__(self, status, body):
        self.status = status
        self._body = json.dumps(body).encode()

    def read(self):
        return self._body

    def __enter__(self):
        return self

    def __exit__(self, *a):
        return False


def _fake_urlopen(routes, calls):
    """urlopen-Ersatz: (METHOD, PFAD) -> (status, body); nicht-2xx wirft HTTPError."""
    def fake(req, timeout=None):
        method = req.get_method()
        path = req.full_url[len(ES):] or "/"
        calls.append((method, path))
        status, body = routes.get((method, path), (404, {"error": f"no route {method} {path}"}))
        if not 200 <= status < 300:
            raise urllib.error.HTTPError(req.full_url, status, "err", None,
                                         io.BytesIO(json.dumps(body).encode()))
        return _Resp(status, body)
    return fake


def _happy_routes(stream_linked=True):
    """Kompletter Erfolgsfall; stream_linked=False simuliert 'PUT ok, aber ohne Wirkung'."""
    stream_policy = POLICY if stream_linked else "irgendwas-anderes"
    return {
        ("GET", "/"): (200, {"version": {"number": "8.17.0"}}),
        ("PUT", f"/_ilm/policy/{POLICY}"): (200, {"acknowledged": True}),
        ("GET", f"/_ilm/policy/{POLICY}"): (200, {POLICY: {"policy": {"phases": {
            "hot": {"actions": {"rollover": {"max_age": "7d"}}},
            "delete": {"min_age": "90d", "actions": {"delete": {}}},
        }}}}),
        ("GET", "/_index_template"): (200, {"index_templates": [
            {"name": TPL, "index_template": {"template": {"settings": {}}}},
        ]}),
        ("PUT", f"/_index_template/{TPL}"): (200, {"acknowledged": True}),
        ("GET", f"/_index_template/{TPL}"): (200, {"index_templates": [
            {"name": TPL, "index_template": {"template": {"settings":
                {"index": {"lifecycle": {"name": POLICY}}}}}},
        ]}),
        # Wie ES 8: jeder Stream nennt das Template, aus dem sein naechster Backing-Index entsteht.
        ("GET", "/_data_stream"): (200, {"data_streams": [{"name": STREAM, "template": TPL}]}),
        ("PUT", f"/{STREAM}/_settings"): (200, {"acknowledged": True}),
        ("GET", f"/{STREAM}/_settings"): (200, {".ds-x-000001": {"settings":
            {"index": {"lifecycle": {"name": stream_policy}}}}}),
    }


def _run_main(routes, calls, argv=None):
    out = io.StringIO()
    with mock.patch("urllib.request.urlopen", _fake_urlopen(routes, calls)), \
         mock.patch.object(sys, "argv", ["es_log_retention.py", "--es-url", ES] + (argv or [])), \
         redirect_stdout(out), redirect_stderr(out):
        rc = elr.main()
    return rc, out.getvalue()


class EsLogRetentionTests(unittest.TestCase):
    def test_request_raises_on_http_error(self):
        """request() darf einen Fehlstatus nicht als normales Ergebnis zurueckgeben."""
        calls = []
        with mock.patch("urllib.request.urlopen",
                        _fake_urlopen({("GET", "/"): (500, {"error": "boom"})}, calls)):
            with self.assertRaises(elr.EsError):
                elr.request("GET", ES)

    def test_happy_path_verifies_by_readback(self):
        """Erfolg (Exit 0) nur mit Readback-GETs nach jedem PUT."""
        calls = []
        rc, out = _run_main(_happy_routes(), calls)
        self.assertEqual(rc, 0, out)
        # Alle drei PUTs passiert ...
        self.assertIn(("PUT", f"/_ilm/policy/{POLICY}"), calls)
        self.assertIn(("PUT", f"/_index_template/{TPL}"), calls)
        self.assertIn(("PUT", f"/{STREAM}/_settings"), calls)
        # ... und jeweils zurueckgelesen (genau das fehlte frueher):
        self.assertIn(("GET", f"/_ilm/policy/{POLICY}"), calls)
        self.assertIn(("GET", f"/_index_template/{TPL}"), calls)
        self.assertIn(("GET", f"/{STREAM}/_settings"), calls)

    def test_put_without_effect_fails(self):
        """PUT antwortet 200, aber das Readback zeigt die Policy nicht -> Exit != 0."""
        calls = []
        rc, out = _run_main(_happy_routes(stream_linked=False), calls)
        self.assertNotEqual(rc, 0, out)

    def test_template_listing_error_exits_nonzero(self):
        """Fehlstatus beim Template-Listing darf nicht wie 'nichts zu tun' aussehen."""
        routes = _happy_routes()
        routes[("GET", "/_index_template")] = (500, {"error": "kaputt"})
        calls = []
        rc, out = _run_main(routes, calls)
        self.assertNotEqual(rc, 0, out)

    def test_dry_run_makes_no_writes(self):
        """--dry-run liest nur — kein einziger PUT geht raus."""
        calls = []
        rc, out = _run_main(_happy_routes(), calls, argv=["--dry-run"])
        self.assertEqual(rc, 0, out)
        self.assertFalse([c for c in calls if c[0] != "GET"], f"Schreibzugriffe im dry-run: {calls}")

    def test_no_sink_template_exits_nonzero(self):
        """Kein Template nach dem Sink-Schema (z. B. umbenannt) -> Exit != 0 statt stiller Erfolg."""
        routes = _happy_routes()
        routes[("GET", "/_index_template")] = (200, {"index_templates": [
            {"name": "fremdes-template", "index_template": {"template": {"settings": {}}}},
        ]})
        routes[("GET", "/_data_stream")] = (200, {"data_streams": [{"name": STREAM}]})
        calls = []
        rc, out = _run_main(routes, calls)
        self.assertNotEqual(rc, 0, out)
        self.assertFalse([c for c in calls if c[1].startswith("/_index_template/")],
                         f"fremdes Template angefasst: {calls}")

    def test_no_log_stream_exits_nonzero(self):
        """Kein Log-Data-Stream -> Exit != 0: ohne Stream laesst sich keine Loeschfrist zusichern."""
        routes = _happy_routes()
        routes[("GET", "/_data_stream")] = (200, {"data_streams": [{"name": "fremd-default"}]})
        calls = []
        rc, out = _run_main(routes, calls)
        self.assertNotEqual(rc, 0, out)
        rc, out = _run_main(routes, [], argv=["--dry-run"])
        self.assertNotEqual(rc, 0, out)

    def test_stream_on_template_without_policy_exits_nonzero(self):
        """Stream benutzt ein Template ausserhalb des Sink-Schemas (Sink-Update) -> Exit != 0,
        die aktuellen Backing-Indices werden trotzdem verknuepft."""
        routes = _happy_routes()
        routes[("GET", "/_data_stream")] = (200, {"data_streams": [
            {"name": STREAM, "template": "rookhub-logs-generic-neu"}]})
        calls = []
        rc, out = _run_main(routes, calls)
        self.assertNotEqual(rc, 0, out)
        self.assertIn("rookhub-logs-generic-neu", out)
        self.assertIn(("PUT", f"/{STREAM}/_settings"), calls)


if __name__ == "__main__":
    unittest.main(verbosity=2)

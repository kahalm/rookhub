#!/usr/bin/env python3
"""Prüft VOR dem Start, ob der Lichess-Token taugt — und sagt im Klartext, was fehlt.

Ohne diese Prüfung endet ein falscher Token in einem rohen Python-Stacktrace
(`401 Client Error` aus dem Provider), der unter `restart: unless-stopped` endlos
weiterläuft. Der häufigste Fehler ist ein Token, dem `engine:write` fehlt: Lesen
klappt, aber die REGISTRIERUNG der Engine nicht — genau das benennt die Prüfung.

Exit-Codes: 0 = Token in Ordnung ODER nicht prüfbar (Netz weg → der Provider hat
eigenes Backoff, ein harter Abbruch wäre hier falsch), 1 = Token definitiv unbrauchbar.
"""
import http.client
import json
import os
import sys
import urllib.error
import urllib.request

REQUIRED_SCOPES = ("engine:read", "engine:write")
TOKEN_TEST_URL = "https://lichess.org/api/token/test"


def scopes_of(info):
    """Lichess liefert die Scopes als kommaseparierten String; defensiv auch Listen zulassen."""
    raw = info.get("scopes") or ""
    if isinstance(raw, list):
        return {str(s).strip() for s in raw}
    return {s.strip() for s in str(raw).split(",") if s.strip()}


def main():
    token = os.environ.get("LICHESS_API_TOKEN", "").strip()
    if not token:
        return 0   # Das Fehlen prüft bereits das entrypoint-Skript mit eigener Meldung.

    # Gegen den konfigurierten Lichess-Server prüfen (Phase 2/Selbstbetrieb), sonst lichess.org.
    base = os.environ.get("LICHESS_URL", "").strip().rstrip("/")
    url = f"{base}/api/token/test" if base else TOKEN_TEST_URL

    request = urllib.request.Request(url, data=token.encode(), method="POST")
    request.add_header("Content-Type", "text/plain")
    try:
        with urllib.request.urlopen(request, timeout=15) as response:
            payload = json.load(response)
    except (urllib.error.URLError, TimeoutError, json.JSONDecodeError, OSError,
            http.client.HTTPException, UnicodeDecodeError, ValueError) as err:
        # Netz/Server-Problem: NICHT abbrechen — der Provider versucht es ohnehin mit Backoff.
        # `http.client.HTTPException` (abgeschnittene Antwort, kaputte Header) ist bewusst
        # mitgefangen: sie ist KEIN OSError und entkäme sonst als Stacktrace — also genau das,
        # was diese Vorabprüfung verhindern soll.
        print(f"Hinweis: Token konnte nicht vorab geprüft werden ({err}). Starte trotzdem.", file=sys.stderr)
        return 0

    if not isinstance(payload, dict):
        # Antwort in unerwarteter Form (Zwischen-Proxy, Fehlerseite): nicht urteilen, durchlassen.
        print("Hinweis: Unerwartete Antwort auf die Token-Prüfung. Starte trotzdem.", file=sys.stderr)
        return 0

    info = payload.get(token)
    if info is None:
        print("FEHLER: Lichess kennt diesen Token nicht (ungültig, widerrufen oder abgelaufen).", file=sys.stderr)
        print("        Neuen Token anlegen — die URL steht in der .env.example.", file=sys.stderr)
        return 1

    if not isinstance(info, dict):
        print("Hinweis: Unerwartete Antwort auf die Token-Prüfung. Starte trotzdem.", file=sys.stderr)
        return 0

    missing = [s for s in REQUIRED_SCOPES if s not in scopes_of(info)]
    if missing:
        print(f"FEHLER: Dem Token fehlt der Scope {', '.join(missing)}.", file=sys.stderr)
        print("        Der Provider REGISTRIERT die Engine auf deinem Konto — dafür wird", file=sys.stderr)
        print("        engine:write gebraucht, engine:read allein genügt nur RookHub selbst.", file=sys.stderr)
        print("        Neuen Token mit BEIDEN Scopes anlegen (URL in der .env.example).", file=sys.stderr)
        return 1

    print(f"Token in Ordnung (Lichess-Konto: {info.get('userId', '?')}).")
    return 0


if __name__ == "__main__":
    sys.exit(main())

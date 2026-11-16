#!/usr/bin/env python3
"""Trainingsstunden (JSON je Datum, Schema 1/2) einem RookHub-Konto zuordnen und einlesen.

Schickt jede Datei an POST /api/admin/training-videos/import?user=<konto>&dryRun=… — über den gedrosselten
Zugang ~/bin/rookhub-prod-api (Anmeldung als Admin-Konto aus ~/.config/rookhub/{prod,dev}-claude.env).

VORGABE IST DER PROBELAUF: ohne --apply wird nur geprüft und gezählt (nichts geschrieben). Prod-Schreiben nur auf
Zuruf des Users, vorher ein Probelauf.

  tools/trainingvideos/import.py /srv/media/claude-sessions/schachvideos/aufbereitet --user kahalm --dev
  tools/trainingvideos/import.py /srv/media/claude-sessions/schachvideos/aufbereitet --user kahalm            # Prod, Probelauf
  tools/trainingvideos/import.py /srv/media/claude-sessions/schachvideos/aufbereitet --user kahalm --apply    # Prod, echt
  tools/trainingvideos/import.py …/2026-10-27.json --user 5 --dev --apply                                   # eine Datei

Re-Import ist idempotent: dieselbe Stunde (Konto + Datum) wird ersetzt, ihre Id und der Übungsstand bleiben.
"""
import argparse, json, os, sys, urllib.parse

sys.path.insert(0, os.path.expanduser('~/bin'))
from rookhub_prod_api import Client  # noqa: E402


def files_of(path):
    if os.path.isdir(path):
        return sorted(os.path.join(path, f) for f in os.listdir(path) if f.endswith('.json'))
    return [path]


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('path', help='Ordner mit <datum>.json oder eine Datei')
    ap.add_argument('--user', required=True, help='Zielkonto: Benutzername oder Id')
    ap.add_argument('--dev', action='store_true', help='Dev-API statt Prod')
    ap.add_argument('--apply', action='store_true', help='wirklich schreiben (sonst Probelauf)')
    args = ap.parse_args()

    client = Client('dev' if args.dev else 'prod', quiet=True)
    query = urllib.parse.urlencode({'user': args.user, 'dryRun': 'false' if args.apply else 'true'})
    failed = 0
    for f in files_of(args.path):
        with open(f, encoding='utf-8') as fh:
            body = json.load(fh)
        status, data = client.request('POST', f'/api/admin/training-videos/import?{query}', body)
        if status != 200 or not isinstance(data, dict):
            failed += 1
            print(f'{os.path.basename(f)}: HTTP {status} {data}')
            continue
        mark = 'OK ' if data.get('ok') else 'FEHLER'
        print(f"{mark} {data.get('date')}: {data.get('segments')} Segmente, {data.get('games')} Partien, "
              f"{data.get('questions')} Fragen, {data.get('notes')} Notizen, ungültige FEN {data.get('invalidFens')}, "
              f"Video {'ja' if data.get('videoFound') else 'nein'}, {'ersetzt' if data.get('replaced') else 'neu'}"
              f"{'' if args.apply else ' (Probelauf)'}")
        for w in data.get('warnings') or []:
            print(f'    Warnung: {w}')
        for e in data.get('errors') or []:
            print(f'    Fehler: {e}')
        if not data.get('ok'):
            failed += 1
    sys.exit(1 if failed else 0)


if __name__ == '__main__':
    main()

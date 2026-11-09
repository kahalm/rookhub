#!/usr/bin/env python3
"""Buch-Übersetzung durch einen externen Übersetzer (Claude-Agent) — Gegenstück zu
/api/admin/ebooks/translations/external (siehe README.md daneben und docs/api/ebooks.md).

Die API-Aufrufe laufen über ~/bin/rookhub_prod_api.py (gemeinsames Budget aller Prozesse, 429-Warten, Token-Cache).
Die Übersetzung selbst macht der Agent: er liest segments-NNN.json und schreibt translated-NNN.json.

  uebersetzen.py [--dev] claim <ebookId> <lang> [--translator NAME]
  uebersetzen.py [--dev] pull <id> <ordner> [--all]        # segments-NNN.json zu je 100 Texten (Vorgabe: nur offene)
  uebersetzen.py [--dev] push <id> <ordner>                # translated-NNN.json → PUT in Paketen ≤ 500, zeigt Ablehnungen
  uebersetzen.py [--dev] assemble <id> [--allow-missing] [--title "…"]
  uebersetzen.py [--dev] status <id>
  uebersetzen.py [--dev] release <id>
  uebersetzen.py [--dev] list
"""
import argparse, glob, json, os, sys

sys.path.insert(0, os.path.expanduser('~/bin'))
from rookhub_prod_api import Client  # noqa: E402

BASE = '/api/admin/ebooks/translations/external'
PAGE = 500          # höchstens so viele je Abruf / Rückgabe (Grenze der API)
FILE_SIZE = 100     # Texte je segments-NNN.json


def call(c, method, path, body=None):
    status, data = c.request(method, path, body)
    if not 200 <= status < 300:
        msg = data.get('message') if isinstance(data, dict) else data
        print(f'FEHLER {status} {method} {path}: {msg}', file=sys.stderr)
        if isinstance(data, dict) and data.get('details'):
            print(json.dumps(data['details'], ensure_ascii=False, indent=1), file=sys.stderr)
        sys.exit(1)
    return data


def show(info):
    keep = {k: v for k, v in info.items() if k not in ('systemPrompt', 'instructions')}
    print(json.dumps(keep, ensure_ascii=False, indent=1))


def cmd_claim(c, a):
    info = call(c, 'POST', f'{BASE}/claim', {'ebookId': a.ebook, 'language': a.lang, 'translator': a.translator})
    show(info)
    print(f"\nÜbersetzungs-Id {info['translationId']} — weiter mit: pull {info['translationId']} <ordner>")


def cmd_pull(c, a):
    os.makedirs(a.folder, exist_ok=True)
    info = call(c, 'GET', f'{BASE}/{a.id}')
    with open(os.path.join(a.folder, 'auftrag.md'), 'w', encoding='utf-8') as f:
        f.write(f"# Buch {info['ebookId']}: {info['title']} ({info.get('sourceLanguage') or '?'} → {info['language']})\n\n")
        f.write('## Systemprompt der Spark (gilt wörtlich)\n\n' + (info.get('systemPrompt') or '') + '\n\n')
        f.write('## Anleitung\n\n' + (info.get('instructions') or '') + '\n\n')
        if info.get('notes'):
            f.write('## Hinweise\n\n' + '\n'.join('- ' + n for n in info['notes']) + '\n\n')
        f.write('## Dateien\n\nJe `segments-NNN.json` (`[{index, source, document}]`) eine `translated-NNN.json` '
                'schreiben: `[{"index": …, "target": "…"}]` — Platzhalter <tN>, </tN>, <tN/> unverändert und genau einmal.\n')
    only_open = 'false' if a.all else 'true'
    items, offset = [], 0
    while True:
        # onlyOpen ändert sich während des Abrufs nicht (es wird nichts zurückgegeben), Versatz über die gefilterte Liste
        page = call(c, 'GET', f'{BASE}/{a.id}/segments?offset={offset}&limit={PAGE}&onlyOpen={only_open}')
        items += page['items']
        offset += len(page['items'])
        if not page['items'] or offset >= page['total']:
            break
    for old in glob.glob(os.path.join(a.folder, 'segments-*.json')):
        os.remove(old)
    files = 0
    for start in range(0, len(items), FILE_SIZE):
        chunk = [{'index': s['index'], 'source': s['source'], 'document': s['document']} for s in items[start:start + FILE_SIZE]]
        with open(os.path.join(a.folder, f'segments-{start // FILE_SIZE:03d}.json'), 'w', encoding='utf-8') as f:
            json.dump(chunk, f, ensure_ascii=False, indent=1)
        files += 1
    chars = sum(len(s['source']) for s in items)
    print(f'{len(items)} Texte ({chars} Zeichen) in {files} Datei(en) unter {a.folder}; Auftrag in auftrag.md')


def cmd_push(c, a):
    items = []
    for path in sorted(glob.glob(os.path.join(a.folder, 'translated-*.json'))):
        with open(path, encoding='utf-8') as f:
            data = json.load(f)
        items += [{'index': int(x['index']), 'target': x['target']} for x in data]
    if not items:
        print('keine translated-*.json gefunden', file=sys.stderr)
        sys.exit(1)
    accepted, rejected, last = 0, [], None
    for start in range(0, len(items), PAGE):
        last = call(c, 'PUT', f'{BASE}/{a.id}/segments', items[start:start + PAGE])
        accepted += last['accepted']
        rejected += last['rejected']
    print(f"{accepted} übernommen, {len(rejected)} abgelehnt — Stand {last['segmentsDone']}/{last['segmentsTotal']}")
    for r in rejected:
        print(f"  #{r['index']}: {r['reason']} — {r['message']}")
    if rejected:
        with open(os.path.join(a.folder, 'rejected.json'), 'w', encoding='utf-8') as f:
            json.dump(rejected, f, ensure_ascii=False, indent=1)
        print(f"Ablehnungen in {os.path.join(a.folder, 'rejected.json')} — korrigieren und erneut push (überschreibt).")


def cmd_assemble(c, a):
    body = {'allowMissing': a.allow_missing}
    if a.title:
        body['title'] = a.title
    print(json.dumps(call(c, 'POST', f'{BASE}/{a.id}/assemble', body), ensure_ascii=False, indent=1))


def cmd_status(c, a):
    show(call(c, 'GET', f'{BASE}/{a.id}'))


def cmd_release(c, a):
    call(c, 'DELETE', f'{BASE}/{a.id}')
    print(f'Übersetzung {a.id} freigegeben (wieder wartend, die Spark darf sie nehmen).')


def cmd_list(c, a):
    for info in call(c, 'GET', BASE):
        print(f"{info['translationId']:>6}  Buch {info['ebookId']:>6}  {info['state']:<8} {info['segmentsDone']:>6}/{info['segmentsTotal']:<6} "
              f"{info.get('translator') or '-':<16} {info['title'][:60]}")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--dev', action='store_true', help='Dev-API statt Prod')
    sub = ap.add_subparsers(dest='cmd', required=True)
    p = sub.add_parser('claim'); p.add_argument('ebook', type=int); p.add_argument('lang'); p.add_argument('--translator', default='claude')
    p = sub.add_parser('pull'); p.add_argument('id', type=int); p.add_argument('folder'); p.add_argument('--all', action='store_true')
    p = sub.add_parser('push'); p.add_argument('id', type=int); p.add_argument('folder')
    p = sub.add_parser('assemble'); p.add_argument('id', type=int); p.add_argument('--allow-missing', action='store_true'); p.add_argument('--title')
    p = sub.add_parser('status'); p.add_argument('id', type=int)
    p = sub.add_parser('release'); p.add_argument('id', type=int)
    sub.add_parser('list')
    a = ap.parse_args()
    c = Client('dev' if a.dev else 'prod')
    globals()['cmd_' + a.cmd](c, a)


if __name__ == '__main__':
    main()

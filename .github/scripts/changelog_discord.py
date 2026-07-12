#!/usr/bin/env python3
"""Postet neue Changelog-Einträge aus changelog.ts in den Discord-Changelog-Channel.

Läuft im GitHub-Actions-Workflow ``changelog-discord.yml`` nach jedem Push auf
master, der changelog.ts ändert. Ermittelt aus dem Push-Diff (``BEFORE..AFTER``)
die NEU hinzugekommenen Versions-Einträge und postet jeden einzeln (älteste
zuerst, englischer Text) als „silent message" (keine Push-Benachrichtigung).

RookHubs changelog.ts ist die Single Source of Truth für den ganzen Stack —
Crawler-/piratechess-/log-watcher-/RepCheck-Änderungen stehen ebenfalls hier.

Bewusst nur Stdlib. Fehlt das Secret ``DISCORD_CHANGELOG_WEBHOOK``, beendet
sich das Script mit Exit 0 (Workflow bleibt grün, bis das Secret gesetzt ist).

ENV: WEBHOOK, BEFORE, AFTER, REPO_LABEL (Default "RookHub").
"""

import json
import os
import re
import subprocess
import sys
import time
import urllib.request

CHANGELOG = 'src/frontend/app/src/environments/changelog.ts'
_ENTRY_RE = re.compile(
    r'\{\s*version:\s*"(?P<version>[^"]+)",\s*date:\s*"(?P<date>[^"]+)"')
_EN_RE = re.compile(r'\ben:\s*"(?P<en>(?:[^"\\]|\\.)*)"')
_DISCORD_LIMIT = 2000
_TRUNCATE_AT = 1900
# SUPPRESS_NOTIFICATIONS ("silent message"): kein Push/Desktop-Ping.
_SILENT_FLAG = 4096


def parse_entries(text: str) -> dict[str, tuple[str, list[str]]]:
    """changelog.ts → {version: (date, [en-Texte])}, Reihenfolge wie in der Datei."""
    entries: dict[str, tuple[str, list[str]]] = {}
    matches = list(_ENTRY_RE.finditer(text))
    for i, m in enumerate(matches):
        start = m.end()
        end = matches[i + 1].start() if i + 1 < len(matches) else len(text)
        block = text[start:end]
        en_texts = [d.group('en').replace('\\"', '"') for d in _EN_RE.finditer(block)]
        entries[m.group('version')] = (m.group('date'), en_texts)
    return entries


def added_versions(diff_text: str) -> list[str]:
    """Im Push-Diff NEU hinzugefügte Versions-Einträge, älteste zuerst."""
    versions = [m.group('version')
                for line in diff_text.splitlines()
                if line.startswith('+')
                and (m := _ENTRY_RE.search(line[1:]))]
    return list(reversed(versions))  # Datei ist neueste-zuerst → chronologisch drehen


def build_message(label: str, version: str, date: str, en_texts: list[str]) -> str:
    body = '\n'.join(f'• {t}' for t in en_texts) or '(kein Text)'
    msg = f'**{label} v{version}** ({date})\n{body}'
    if len(msg) > _DISCORD_LIMIT:
        msg = msg[:_TRUNCATE_AT].rstrip() + ' …'
    return msg


def _post(webhook: str, content: str) -> None:
    payload = {'content': content, 'flags': _SILENT_FLAG}
    req = urllib.request.Request(
        webhook, data=json.dumps(payload).encode('utf-8'),
        # Expliziter User-Agent: Discords Cloudflare blockt Pythons
        # urllib-Default-UA mit 403 (error code 1010).
        headers={'Content-Type': 'application/json',
                 'User-Agent': 'rookhub-changelog-webhook/1.0'}, method='POST')
    with urllib.request.urlopen(req, timeout=15) as resp:
        resp.read()


def main() -> int:
    webhook = os.environ.get('WEBHOOK', '').strip()
    if not webhook:
        print('DISCORD_CHANGELOG_WEBHOOK nicht gesetzt — überspringe Announce.')
        return 0

    label = os.environ.get('REPO_LABEL', 'RookHub')
    before = os.environ.get('BEFORE', '')
    after = os.environ.get('AFTER', 'HEAD')

    with open(CHANGELOG, encoding='utf-8') as f:
        entries = parse_entries(f.read())
    if not entries:
        print('Keine Changelog-Einträge gefunden.')
        return 0

    versions: list[str] = []
    if before and not set(before) <= {'0'}:
        try:
            diff = subprocess.run(
                ['git', 'diff', f'{before}..{after}', '--', CHANGELOG],
                capture_output=True, text=True, check=True).stdout
            versions = [v for v in added_versions(diff) if v in entries]
        except subprocess.CalledProcessError as e:
            print(f'git diff fehlgeschlagen ({e}) — Fallback auf neuesten Eintrag.')
    if not versions:
        versions = [next(iter(entries))]  # neuester Eintrag (Datei ist neueste-zuerst)

    for v in versions:
        date, en_texts = entries[v]
        _post(webhook, build_message(label, v, date, en_texts))
        print(f'Gepostet: {label} v{v}')
        time.sleep(1)
    return 0


if __name__ == '__main__':
    sys.exit(main())

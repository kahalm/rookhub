# Externe Buch-Übersetzung (Claude-Agenten)

Ein Agent übersetzt ein E-Book, das die Spark noch nicht übersetzt hat — über dieselben Texte, Prüfungen und denselben
EPUB-Schreiber wie der Worker. API: `/api/admin/ebooks/translations/external` (`ebooks.manage`), Beschreibung in
`docs/api/ebooks.md`, Abschnitt „Externe Übersetzer“.

`uebersetzen.py` (Python 3, ohne Zusatzpakete) spricht die API über `~/bin/rookhub_prod_api.py` (gemeinsames Budget aller
Prozesse, 429-Warten, Token-Cache des Kontos `claude-prod` bzw. mit `--dev` `claude-dev`). Schreiben auf Prod nur auf Zuruf.

## Ablauf

```bash
T=tools/EbookTranslateExternal/uebersetzen.py
python3 $T claim 1234 en --translator claude-opus      # → Übersetzungs-Id, z. B. 77
python3 $T pull 77 /srv/media/claude-sessions/ebook-uebersetzung/77
#   Agent liest auftrag.md (Systemprompt der Spark + Anleitung) und je segments-NNN.json die Texte,
#   schreibt dazu translated-NNN.json: [{"index": 0, "target": "…"}, …]
python3 $T push 77 /srv/media/claude-sessions/ebook-uebersetzung/77   # Ablehnungen → rejected.json, korrigieren, erneut push
python3 $T pull 77 <ordner>                             # holt nur noch Offenes (z. B. Abgelehntes)
python3 $T assemble 77 --title "English title"          # alles übersetzt → EPUB, Zustand done
python3 $T assemble 77 --allow-missing                  # Rest im Original lassen
python3 $T status 77 · list · release 77                # release: Anspruch zurück, die Spark darf weitermachen
```

## Regeln für die Übersetzung

- `auftrag.md` enthält den Systemprompt der Spark wörtlich — er gilt (Prosa vollständig, Notation/Namen bleiben,
  Anführungszeichen ‘…’ fürs Englische).
- Jeder Platzhalter `<tN>`, `</tN>`, `<tN/>` genau einmal und unverändert, Paare um dieselben Wörter, nicht über Kreuz.
- Figurenbuchstaben NICHT selbst umstellen — das macht das Zusammenbauen (`EbookNotationLetters`), auch in Zuglisten.
- Texte, die nur aus Namen bestehen, dürfen unverändert zurück.
- Geprüft wird wie bei der Spark: Platzhalter + Verschachtelung, nicht leer, kein offenes Zitat am Ende, ab 150 Zeichen
  mindestens 70 % der Quelllänge und Zielsprache erkennbar. Gültiges landet auch im Zwischenspeicher
  (`converted/<id>.<lang>.cache.jsonl`): nach `release` übersetzt die Spark nur den Rest.
- Große Arbeitsordner unter `/srv/media/claude-sessions/<thema>/`, nicht auf der Systemplatte.

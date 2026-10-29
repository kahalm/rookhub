# E-Book-Bibliothek (auth, 0.764.0) — Bücher lesen, hochladen, je Buch freigeben

> Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md. Bereich `Ebooks` (`Services/Ebooks/`,
> `Controllers/Ebooks/`, `DTOs/Ebooks/`, `tests/…/Ebooks/`); Oberfläche `features/ebooks/` (`/ebooks`, `/ebooks/:id`).

Ein **E-Book** ist ein Titel mit einer oder mehreren Dateien (dasselbe Buch als PDF + EPUB + DJVU), Metadaten (Titel,
Autor, Jahr, Schlagworte, Beschreibung, Ziel-Elo) und einer **Gruppen-Freigabe je Buch** (`EbookGroupAccess`, dasselbe
Muster wie `BookGroupAccess` bei den Kursen). **Jeder Angemeldete darf hochladen** und steuert die Sichtbarkeit seines
Buchs selbst — mit seinen eigenen Gruppen und „Everyone" (= alle Angemeldeten); Admins bzw. `ebooks.manage` sehen und
verwalten alles. Sichtbar ist ein Buch für Besitzer, Verwalter und Mitglieder einer freigegebenen Gruppe; unsichtbar
und unbekannt sehen gleich aus (404). Regel: `Services/Ebooks/EbookAccess.cs` (eine Stelle).

**Zwei Wurzeln auf der Platte** (`EbookOptions`, ENV `Ebooks__Root` / `Ebooks__LibraryRoot`): `Root` (Volume, beschreibbar)
nimmt Uploads unter `files/JJJJ/MM/<guid>.<ext>` und Titelbilder unter `covers/<id>.cover`; `LibraryRoot` ist die vorhandene
Bibliothek des Hosts (28 GB, nur lesend eingehängt) — **„Bibliothek einlesen"** legt nur Datenbankzeilen an, die Dateien
bleiben, wo sie sind (`Ebook.Source = Library`). Gespeichert wird immer ein RELATIVER Pfad; `EbookStorage.ResolvePath`
weist alles ab, was die Wurzel verlässt. Ohne `Root` ist Hochladen aus (400 `ebooks_not_configured`), ohne `LibraryRoot`
das Einlesen; Übersicht und Leser laufen trotzdem. Gelöscht werden nur Uploads und Titelbilder, nie Bibliotheksdateien.

**Grenzen:** 1 GiB je Datei (`EbookOptions.MaxFileBytes`), Anfrage bis `EbookService.MaxUploadRequestBytes` (1,1 GB; nginx
`location ^~ /api/ebooks` hält dieselbe Grenze ungepuffert, `DeploymentConfigTests`), Kontingent 5 GiB Uploads je Nutzer
(`UserQuotaBytes`, 0 = aus; Verwalter ohne), Formate `pdf epub djvu cbz cbr mobi azw3` (`EbookFormats.Allowed`), höchstens
20 Schlagworte à 40 Zeichen. Eine zweite Datei mit demselben SHA-256 → 409 `ebook_duplicate_file` (die Datei wird wieder
gelöscht). Dasselbe Buch in einer ANDEREN Datei erkennt seit 0.863.0 die Dublettenprüfung (ISBN, Text-Fingerabdruck,
Titel + Autor) — siehe „Upload-Dublettenprüfung“ unten.

**Titelbilder** rendert der `EbookCoverWorker` (alle 20 s, 4 Bücher je Durchlauf) nachträglich: PDF über `pdftoppm` (die erste
NICHT leere der ersten 8 Seiten — ≥ 0,2 % dunkle Pixel, `EbookCoverRenderer.MinCoverInk`; 0.775.3, alte Bücher und Scans
beginnen oft mit leeren Vorsatzblättern; 480 px, dazu die Seitenzahl aus `pdfinfo`), DJVU über `ddjvu` → PDF der ersten 8
Seiten → `pdftoppm`; Migration `EbookCoverSkipBlankPages` rendert vorhandene PDF-/DJVU-Titelbilder neu, EPUB/CBZ als Bild aus dem Zip
(OPF-Cover bzw. erstes Bild). Werkzeuge liegen im API-Image (`poppler-utils`, `djvulibre-bin`); fehlen sie, bleibt das Buch
auf `CoverState = Failed` (kein weiterer Versuch) und die Karte zeigt den Titel statt eines Bilds.

**Datei-Endpunkte sind `[AllowAnonymous]` mit Ausweis**: `<img>`, Download-Links und der PDF-Leser (Bereichsanfragen) können
keinen Bearer schicken. `GET /api/ebooks/access-token` gibt einen 12-h-Ausweis (`EbookAccessTokens`: DataProtection-
versiegelte Nutzer-Id), der als `?t=` mitgeht; jede Anfrage prüft die Sichtbarkeit des Buchs für genau diesen Nutzer gegen
die Datenbank (Admin-Flag ebenfalls aus der DB, nicht aus dem Ausweis). Ohne Ausweis UND ohne Bearer: 401. Eingetragen in
`EndpointAuthInventoryTests.ExpectedAnonymous`.

| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/ebooks?search=&tag=&mine=&withDiagrams=&minElo=&maxElo=&language=&translation=&sort=&page=&pageSize=` | Auth | Sichtbare Bücher, eine Seite (`pageSize` ≤ 100, Vorgabe 24). `language` (0.877.0): Sprache des Buchtexts (`Ebook.Language`, ISO 639-1), `unknown` = nicht bestimmt; `translation`: nur Bücher mit FERTIGER Übersetzung in diese Sprache. Rahmen zusätzlich `languages[]` und `translations[]` (`{ language, count }` über alle sichtbaren, unabhängig vom Filter; `language: null` = unbestimmt), `bookCount` (Bücher zur Filterung — jede Sammlung mit all ihren passenden Büchern, `total` zählt sie als einen Ordner) und `fileCounts[]` (`{ format, count }`, Dateien dieser Bücher ohne abgeleitete, Format klein). `minElo`/`maxElo` (0.822.0): Mindest-Elo des Buchs (`TargetMinElo`, 0 = Anfänger) in der Spanne, mit einer Grenze fallen Bücher ohne Elo heraus; die Oberfläche zeigt dafür „Elo von – bis“ statt des früheren Schalters „Mit Diagrammen“ (fast alle Bücher haben welche) und „ab N Elo“ auf jeder Karte. Rahmen: `tags[]` (Schlagwort-Facette über ALLE sichtbaren, unabhängig vom Filter), `uploadEnabled`, `quotaUsedBytes`/`quotaLimitBytes` (null = unbegrenzt), `maxFileBytes`, `canManageAll`, `canImportLibrary`. `sort`: `title` (Vorgabe) / `newest` / `recent` (zuletzt gelesen zuerst). `tag` trifft nur ganze Schlagworte. Je Buch `canManage`, `groupIds` (nur wenn verwaltbar), `files[]`, `primaryFormat` (PDF vor allem anderen), `progress` (Lesestand des Aufrufers), `hasCover` |
| GET | `/api/ebooks/groups` | Auth | Gruppen, die der Aufrufer freigeben darf: Verwalter alle, sonst die eigenen + „Everyone" (`isEveryone`). Literal vor `{id}` |
| GET | `/api/ebooks/access-token` | Auth | `{ token, expiresAt }` — der Ausweis für `…/file` und `…/cover` |
| POST | `/api/ebooks` | Auth | Hochladen: Multipart `file` + `title?` (sonst aus dem Dateinamen), `author?`, `year?`, `tags?` (CSV), `description?`, `targetMinElo?`, `groupIds?` (CSV), `forceNew?` (0.863.0) → 201 + Buch (ggf. mit `possibleDuplicates`). 400 `ebook_format_unsupported` / `ebook_file_too_large` / `ebook_file_empty` / `ebook_quota_exceeded` / `ebooks_not_configured`, 409 `ebook_duplicate_file` / `ebook_duplicate_book` (je mit `details.books`, siehe „Upload-Dublettenprüfung“). Gruppen außerhalb der erlaubten werden still verworfen. Gesperrt in der Impersonation |
| PUT | `/api/ebooks/groups` | Auth | Freigabe vieler Bücher auf einmal `{ ebookIds[], groupIds[] }` (ersetzt die bisherige; nicht verwaltbare Bücher werden übersprungen) → `{ updated }`. Literal vor `{id}` |
| GET | `/api/ebooks/{id:int}` | Auth | Ein Buch (unsichtbar → 404) |
| PUT | `/api/ebooks/{id:int}` | Besitzer/Verwalter | Metadaten + Freigabe; nur gesetzte Felder wirken (`year: 0` = keine Angabe, `groupIds: []` = nur ich). Leerer Titel → 400; fremd → 403; unsichtbar → 404 |
| DELETE | `/api/ebooks/{id:int}` | Besitzer/Verwalter | Löscht Buch, Freigaben, Lesestände und die HOCHGELADENE Datei + Titelbild (Bibliotheksdateien bleiben) → 204 |
| GET/PUT | `/api/ebooks/{id:int}/progress` | Auth | Lesestand `{ page, pageCount? }` (1-basiert; `pageCount` bleibt, wenn keiner mitkommt). Unsichtbar → 404 |
| GET | `/api/ebooks/{id:int}/file?t=&fileId=&download=` | **AllowAnonymous** (Ausweis oder Bearer) | Die Hauptdatei bzw. `fileId`, mit Bereichsanfragen (`PhysicalFile(enableRangeProcessing)`) — der PDF-Leser holt nur die Seiten, die er zeigt. `download=true` → Anhang „<Titel>.<format>“ (Dateisystem-sichere Zeichen). Datei auf der Platte weg → 404 `ebook_file_missing` |
| GET | `/api/ebooks/{id:int}/epub/{token}/{**path}` | **AllowAnonymous** (Ausweis im PFAD oder Bearer), Rate-Limit `ebook-entries` | EIN Eintrag aus dem EPUB-ZIP (`EbookEpubEntries`) für den EPUB-Leser „als Ordner“. Ausweis im Pfad, damit epub.js relative Adressen (Kapitel, Bilder, CSS) ohne Query auflösen kann; Sichtbarkeit wie `…/file`, gewählt wird die EPUB-Datei des Buchs (kein EPUB → 404). Pfad nur als ZIP-Eintrag (nie Dateisystem), `..`/absolut/Backslash → 404, Schreibweise wie im ZIP, Content-Type nach Endung, `Cache-Control: private, max-age=86400`, Einträge > 64 MB → 404. Eigener Deckel 600/min je Adresse statt des globalen 100/min-Topfs (ein Kapitel mit 50 Diagrammbildern = 50+ Anfragen) |
| GET | `/api/ebooks/{id:int}/cover?t=` | **AllowAnonymous** (Ausweis oder Bearer) | Titelbild (JPEG/PNG, Typ an den Bytes erkannt) oder 404, solange keines da ist; `Cache-Control: private, max-age=86400` |
| GET | `/api/admin/ebooks/library` | `ebooks.manage` | `{ configured, uploadsEnabled, imported, coversPending }` |
| POST | `/api/admin/ebooks/{id:int}/reflow` | `ebooks.manage` | Fließtext-EPUB aus dem Text-PDF bauen (0.802.0, Prototyp, synchron ~10–20 s je 400 Seiten) → `{ ebookId, fileId, sizeBytes, pages, chapters, paragraphs, diagrams, diagramsLinked }`. Ersetzt ein vorhandenes. 400 `ebook_reflow_unsupported`: kein Original-PDF, schon ein echtes EPUB, Diagramme noch nicht erkannt, keine Textebene (Scan). Siehe „Fließtext-EPUB aus Text-PDFs“ |
| POST | `/api/admin/ebooks/library/import` | `ebooks.manage` | Bibliothek einlesen (synchron, Sekunden): gleichnamige Dateien eines Ordners (`Buch.pdf` + `Buch.djvu`) werden EIN Buch; Beiblatt `Buch.json` (`title`, `author` als Text oder Liste, `year`, `tags[]`, `targetMinElo`) liefert die Metadaten, sonst Ordner „Nachname, Vorname" + Dateiname (Ordner `AAA…`, `Unknown`, `.…` gelten nicht als Autor). Sammelordner `Gemeinfrei`: Autor ist der Unterordner wörtlich („V. M. N. Portilla“; „(…)“ = kein Autor), ein Namensende „[Autor, Jahr]“ wird abgetrennt und liefert das Jahr (d3, Titel kam vorher bis zum letzten Punkt gekürzt an: „Chess Problems [V. M. N“). Idempotent: bekannte Pfade werden übersprungen, ein neues Format neben einem bekannten Buch angehängt. Eingelesene Bücher gehören dem Admin und sind zunächst NUR für Verwalter sichtbar — Freigabe danach in der Übersicht (Auswahl → „Sichtbarkeit setzen"). Antwort `{ configured, filesSeen, added, filesAdded, skipped, unreadable }` |

**Oberfläche** (`features/ebooks/`): Übersicht `/ebooks` (Suche mit 300 ms Verzögerung, Schlagwort-Chips, Sortierung,
„Meine Uploads", 24 je Seite; Karte mit Titelbild 2:3, Formaten, Seitenzahl, Lesestand-Balken; Hauptaktion „Lesen" bei PDF,
sonst „Herunterladen"; ⋮ mit Download je Format, Bearbeiten/Sichtbarkeit/Löschen für Verwaltbare; Auswahlmodus für die
Sichtbarkeit vieler Bücher; Verwalter: „Bibliothek vom Server einlesen"). Leser `/ebooks/:id` (`PdfViewerComponent`): pdf.js
kommt zur Laufzeit aus `/assets/pdfjs/` (angular.json kopiert `pdf.min.mjs` + Worker aus dem **LEGACY-Build** `legacy/build/`
sowie `cmaps`, `standard_fonts`, `wasm`, `iccs` aus `node_modules/pdfjs-dist`) — bewusst NICHT im Bundle, sonst läge ~1 MB in jedem
Vorab-Cache des Service Workers. Legacy, weil der Standard-Build von pdf.js 6 `Promise.try`/`Promise.withResolvers` ohne Rückfall
nutzt: in Browsern ohne diese (Chrome < 128, Safari < 18.2) hing der Leser still am Ladebalken (0.764.1). Gezeichnet
werden nur sichtbare Seiten ± eine Sichtfeldhöhe (Leinwände weiter weg werden geleert), Bereichsanfragen à 256 KB
(`disableAutoFetch`), „Breite füllen" als Vorgabe, Zoomstufen in `reader-progress.util.ts`; der Lesestand geht 1,5 s nach dem
letzten Seitenwechsel an den Server und wird beim nächsten Öffnen angesprungen. EPUB/DJVU/CBZ/… zeigen eine Download-Karte
(ein EPUB-Leser im Browser ist offen, siehe TODO).

## Upload-Dublettenprüfung (0.863.0)

Byte-Gleichheit (SHA-256) fing nur exakt dieselbe Datei. Ein anderer Export desselben Buchs (zweites PDF, EPUB statt PDF)
wurde ein zweites Buch. Jetzt bekommt jede `EbookFile` einen **Fingerabdruck** (`Services/Ebooks/EbookFingerprint.cs`,
Spalten `Isbns`, `TextFingerprint`, `FingerprintedAt`, Migration `EbookFileFingerprints`):

- **ISBN** (`Isbns`, ISBN-13 kommagetrennt): EPUB aus dem OPF (`dc:identifier`, `urn:isbn:`) und dem Text, PDF aus Info/XMP
  und dem Text der ersten 10 Seiten. ISBN-10 → ISBN-13, nur mit gültiger Prüfziffer; ohne „ISBN“ davor zählt nur eine
  13-stellige 978/979-Zahl. Mehr als 4 verschiedene im Text = Verlagsliste → nur die Metadaten zählen.
- **Text-MinHash** (`TextFingerprint`, 64 × uint32 Base64): Wortstrom normalisiert (klein, ohne Akzente, nur Buchstaben/
  Ziffern, reine Zahlen wie Seitenzahlen weg, Trennstriche am Zeilenende zusammengezogen), Fenster von 60 000 Wörtern nach
  den ersten 1 000 (Titelei), Wort-5-Gramme. PDF: höchstens 400 Seiten bzw. 45 s; hat der Text nach 20 Seiten keine 200
  Wörter (Scan), kein Fingerabdruck. Unter 2 000 Wörtern ebenfalls keiner.
- **Titel + Autor** (ohne Spalte, `NormalizeTitle`/`AuthorsOverlap`): Haupttitel vor „:“/„ - “, ohne Artikel/Satzzeichen;
  gemeinsamer Nachname (oder eine Seite ohne Autor).

**Beim Hochladen** (`EbookService.UploadAsync` → `EbookDuplicateFinder`), NUR unter den Büchern, die der Hochladende sehen
darf (`EbookAccess.Visible` — ein verstecktes Buch verrät sich nie):

| Treffer | Stufe | Folge |
|---------|-------|-------|
| gleiche ISBN | sicher | 409 `ebook_duplicate_book`, Datei wird gelöscht |
| Text-Jaccard ≥ 0,8 (`CertainSimilarity`) | sicher | 409 `ebook_duplicate_book` |
| Text-Jaccard 0,4–0,8 (`ProbableSimilarity`) | wahrscheinlich | Buch angelegt, `possibleDuplicates` in der Antwort |
| gleicher Haupttitel + Autor | wahrscheinlich | wie oben |

Gemessen an synthetischen Büchern (`EbookFingerprintTests`): PDF-Layout mit 2 500 Wörtern längerer Titelei gegen EPUB 0,89,
zwei PDF-Exporte 0,92, Neuauflage mit jedem vierten Absatz neu 0,58, fremde Bücher 0,00. `forceNew=true` überspringt
ISBN/Text/Titel (Byte-Prüfung bleibt). Der 409 trägt `details.books[{ id, title, author, year, formats[], reason
(file|isbn|text|title), similarity }]` (`IApiErrorDetails` an `ConflictException`, `ApiErrorResponses.Body` hängt `details`
nur an, wenn welche da sind); auch `ebook_duplicate_file` nennt das vorhandene Buch, wenn es sichtbar ist.

**Oberfläche** (`ebook-edit-dialog` + `ebook-duplicate-dialog`): 409 → „Dieses Buch ist schon in der Bibliothek“ mit den
Büchern, „Zum vorhandenen Buch“ und „Trotzdem als neues Buch hochladen“ (nicht bei gleicher Datei); wahrscheinlich → „Ist
das eines dieser Bücher?“, „Ja, das vorhandene nehmen“ löscht den eben hochgeladenen Upload und öffnet das gewählte,
„Nein, behalten“ schließt.

**Bestand**: `EbookFingerprintWorker` (alle 20 s 3 Dateien, Start nach 2 min, pausierbar) holt alle Dateien mit
`FingerprintedAt = null` nach — Bibliothek, alte Uploads, abgeleitete PDFs (DJVU → PDF) und Fließtext-EPUBs. Formate ohne
Textweg (djvu, cbz, mobi, azw3) werden nur als erledigt markiert. ~2 500 Bücher ≈ 7 000 Dateien ≈ 13 h.

**Noch nicht**: Scans ohne Textebene haben nur ISBN (selten) und Titel; ein Bildvergleich (pHash der Seiten) bzw. ein Abgleich
über die erkannten Diagramm-FENs wäre der nächste Schritt.

## Diagramm-Erkennung (Stufe 1, 0.766.0)

Digital gesetzte Bücher zeichnen Diagramme als TEXT in einem Schach-Font — jedes Feld ein Buchstabe. `EbookDiagramWorker`
(alle 30 s, 2 Bücher je Durchlauf; Reihenfolge `QueueOrder`: Uploads von Nutzern zuerst, dann erstmals eingelesene
Bibliotheksbücher ohne Diagramme, zuletzt Ergänzungen/Neuerkennungen, je Gruppe Riesenbände > 1 000 Seiten ans Ende — vorher
strikt nach Id, ein Upload wartete am 23.10. 10 h hinter 92 Neuerkennungen) liest jedes PDF mit **PdfPig** (`EbookDiagramExtractor`): Buchstaben eines bekannten
Diagramm-Fonts (`ChessDiagramFonts`) werden Felder, `DiagramGridBuilder` macht daraus Bretter (Gruppen, die genau 8 × 8 Zellen
überspannen; fehlende Zellen = leer, weil PDF-Bibliotheken Leerzeichen unterschlagen; Lage über Grundlinie + Vorschub, nicht
über das je Figur verschiedene Tintenrechteck). Ergebnis in `EbookDiagrams` (Seite, Lage als Seitenanteil 0–1, FEN, Nummer,
Überschrift, `SideKnown`), Stand am Buch (`DiagramState` pending/done/failed/unsupported, `DiagramCount`).

- **Font-Belegungen** (nie raten, nur am Seitenbild bzw. gegen Kurs verifizierte): Marroquin-Familie (Chess Merida, ChessCases,
  CHESSBOARD …: klein = helles Feld, groß = dunkles, `pnbrqk` weiß, `omvtwl` schwarz), ChessBase DiagramTT + UDiagramm (groß
  weiß, klein schwarz, Läufer `L`, Schraffur-Overlays `s t v w z m` ignoriert), Hastings/Linares (helles Feld `PNBRQK`/`pnbrqk`,
  dunkles `) H G $ ! I` / `0 h g 4 1 i`, leer `W w D d`). Nicht lesbar: Figurinen-Schriften (LinaresFigurine, CAChess,
  FigurineCB), umkodierte Untermengen ohne ToUnicode (Dejascacchi), gedrehte Seiten (Ponentz). Abdeckung 14.10.: rund 50 der
  58 Font-Bücher liefern Diagramme (Woodpecker 1 140, Grooten 992, Cook 970, Baird 764 …). Nach einer neuen Belegung:
  `POST /api/admin/ebooks/diagrams/rescan` oder eine Daten-Migration (0.767.0 setzt Unsupported/Failed zurück auf Pending).
- **Nummer**: Zahl links der Oberkante (Kreis, Quality Chess) oder ALLEIN auf der Zeile darüber (sonst wären Jahreszahlen
  Nummern). **Zugrecht**: Wingdings3-Dreieck rechts neben DIESEM Brett (U+F072 Weiß, U+F071 Schwarz), sonst „White/Black to
  move“ direkt darüber/darunter, sonst Weiß mit `SideKnown=false`. **Rochade** geschätzt (König + Turm auf Ausgangsfeldern).
- Prüfung „The Woodpecker Method“ gegen Chessable-Kurs 88: 1 140 Diagramme, 1 128 nummeriert, Brett 1 118 gleich (die 10
  übrigen weichen im KURS ab, visuell geprüft), Zugrecht 1 127, Rochade 1 114. Test `EbookDiagramExtractionTests` liest das
  echte Buch, wenn der Rechner es hat (CI überspringt).

| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/ebooks/{id:int}/diagrams` | Auth | `{ state, items[{ id, page, x, y, w, h, fen, label, caption, sideKnown }] }` in Lesereihenfolge; leer, solange nicht `done`. Unsichtbar → 404 |
| POST | `/api/admin/ebooks/diagrams/rescan` | `ebooks.manage` | alle PDF-Bücher neu vormerken → `{ queued }` |
| POST | `/api/admin/ebooks/{id:int}/diagrams/side-from-captions` | `ebooks.manage` | Zugrecht aus „White/Black to move“ unter den Brettern nachtragen (0.823.0, `EbookSideFromCaptions`): nur Diagramme ohne Zugrecht mit PDF-Lage, Seitenwechsel liest die Buchlösung neu, das Buch gilt NICHT als kuratiert → `{ ebookId, checked, set, changed }`, unbekannt 404. Die Scan-Erkennung liest die Unterschrift seither selbst (`ScanCaptionSide`: Zeile ohne Leerzeichen, weil die OCR „B l ack to move“ zerreißt; Unterschrift darunter vor der darüber, waagrecht über dem Brett ± 2 %, bis 8 % Seitenhöhe darunter bzw. 5 % darüber) |
| POST | `/api/admin/ebooks/{id:int}/diagrams/supplement` | `ebooks.manage` | EIN Buch „ergänzen statt ersetzen“ (0.811.0, auch kuratierte) → 202 `{ ebookId, queued }`, unbekannt 404 — siehe unten |
| POST / DELETE | `/api/ebooks/{id:int}/diagrams/{diagramId:int}/report` | Auth | „Diagramm falsch“ melden (`{ note? }`, idempotent) bzw. eigene Meldung zurücknehmen → `{ reported }`. Tabelle `EbookDiagramReports` (Seite + FEN mitgeschrieben, `DiagramId` ohne FK — überlebt eine Neuerkennung, die die Diagramm-Zeilen ersetzt; unique je Nutzer + Diagramm). Im Diagramm-DTO `reportedByMe`, `reportCount` (nur Verwalter) |
| GET | `/api/admin/ebooks/diagram-reports` | `ebooks.manage` | Meldungen, neueste zuerst (≤ 500) mit Buchtitel, Seite, FEN, Font-Familie, Hinweis, Nutzer, `diagramExists` |

**Leser**: hat das Buch Diagramme, wird er zweispaltig — Buch links, Brett rechts (`clamp(300px, 32vw, 460px)`). Über jedem
Diagramm liegt eine Zone (`PdfViewerComponent.zones`, Prozent der Seite → zoomfest); Doppelklick (oder Fokus + Enter) holt die
Stellung aufs Brett, Schwarz am Zug dreht es. ‹/› gehen alle Diagramme durch und scrollen das Buch mit, „Analysieren“ öffnet
`/analysis?fen=…&from=/ebooks/:id`, FEN kopieren. Unter 900 px öffnet das Brett nach dem ersten Doppelklick als Vollbild über dem Buch (0.780.2, vorher Leiste unten mit
höchstens 62 % Höhe); Kopf mit ‹ › und Schließen bleibt oben stehen, Escape schließt.

**Filter-Komponente, mehrsprachige Schlagwörter, Gesamtzeile** (0.877.0, Nutzerwunsch 29.10.): Schlagwörter, Sprache
(Buchsprache + „Übersetzung: English“) und Elo-Spanne stehen auf jeder Breite in `EbookFilterPanelComponent` hinter
„Filter ▾“ (Zahl = aktive Filter, zugeklappt aktive Filter als Chips); Sortierung und „Meine Uploads“ bleiben ab 600 px in
der Zeile. Schlagwörter bleiben als DEUTSCHES Wort gespeichert (kanonischer Wert für `?tag=`, Suche, Bearbeiten-Dialog,
Einlese-Werkzeuge, keine Datenmigration); nur die Anzeige wird übersetzt: `EBOOK_TAG_KEYS` in `features/ebooks/ebook-tags.ts`
ordnet Schlagwort → `ebooks.tags.<slug>` (en/de/hr/hu gepflegt, die übrigen Sprachen fallen aufs Englische zurück),
`ebookTagLabel`/`EbookTagLabelPipe` zeigen es — ohne Eintrag (freie Schlagwörter, Eigennamen wie „Top 100“, „ChessDojo“,
„ECF Book of the Year“) wörtlich. Aufgeklappt sortiert die Schlagwort-Gruppe nach der angezeigten Beschriftung; zugeklappt
die 8 häufigsten. Neues Schlagwort mit Übersetzung = Zeile in `EBOOK_TAG_KEYS` + Text in allen vier Sprachdateien
(`ebook-tags.spec.ts` prüft beides, de = gespeichertes Wort). Sprachnamen kommen aus `Intl.DisplayNames` in der
Oberflächensprache. Über dem Regal steht „1 234 Bücher · 3 456 Dateien (PDF … · EPUB …)“ aus `bookCount`/`fileCounts`
derselben gefilterten Abfrage (Formate am Handy ausgeblendet). Zeitschriften haben noch keinen eigenen Begriff (nur das
Schlagwort „Zeitschrift“ und Sammlungen) — deshalb keine eigene Zahl dafür. Sprache/Übersetzung stehen wie die übrigen
Filter in Adresse (`?lang=&tr=`) und Sitzung (`rh.ebooks.listState`).

**Übersicht merkt sich die Ansicht** (0.768.0): Suche, Schlagwort, Sortierung, „Meine Uploads“, „Mit Diagrammen“ und Seite
stehen in der Adresse (`?q=&tag=&sort=&mine=1&diagrams=1&page=`, `replaceUrl`) und in der Sitzung (`rh.ebooks.listState`) —
der Zurück-Pfeil im Leser führt auf das nackte `/ebooks` und landet trotzdem in derselben Ansicht.

**Brett spielbar** (0.769.0): Hand-Knopf neben dem Brett schaltet `playable` ein; gespielte Züge (SAN + FEN) stehen unter dem
Brett mit Zugnummern ab der Buchstellung, Zurücknehmen / Buchstellung, „Analysieren“ und „FEN kopieren“ nehmen die aktuelle
Stellung. Regelwidrige Buchstellungen (chess.js lehnt ab, z. B. Studie ohne König) bleiben nur zum Ansehen.

## EPUB-Leser, DJVU → PDF, Trenner (0.770.0)

- **EPUB** (`EpubViewerComponent`, epub.js 0.3.93 als Lazy-Chunk): öffnet das Buch „als Ordner“ unter
  `…/epub/{token}/` (`EbookService.epubBaseUrl`, `openAs: 'directory'`, `replacements: 'none'`) — geladen werden nur
  container.xml, OPF, Navigation, das gezeigte Kapitel und dessen Bilder/CSS (Dvoretsky's Endgame Manual, 84 MB: erste Seite
  nach ~18 kleinen Anfragen statt nach dem ganzen Archiv). Scheitert das (20 s Deckel), wie früher die ganze Datei als
  ArrayBuffer über `…/file`. Das Vermessen für genaue Prozent (`locations.generate`, lädt jedes Kapitel einmal, ohne Bilder)
  startet erst 4 s nach der ersten Seite. Paginiert, ‹ › / Pfeiltasten / Wischen, Inhaltsverzeichnis im Kopf. Lesestand: `EbookProgress.Locator` = CFI, `Page` =
  Prozent (1–100) bei `PageCount` 100 — so funktioniert der Fortschrittsbalken der Karten unverändert. Die CSP des
  Frontend-nginx erlaubt dafür `blob:` bei `style-src` und `font-src` (epub.js reicht Buch-CSS und -Schriften als blob: ins
  iframe; vorher: Buch ungestaltet, CSP-Fehler). Buch-Skripte bleiben aus (epub.js-Vorgabe).
  **Zugtabellen** (0.875.0, `compactMoveTables`, Content-Hook): Tabellen, deren Zellen alle kurzer Text (≤ 10 Zeichen, kein
  Bild) sind, bekommen `rh-moves` → `width:auto`, kleine Ränder. Scribd-Umwandlungen setzen jede Zugzeile als Tabelle mit
  `width:100%; margin:5% auto` (#2250: Züge über die ganze Breite, große Lücken). Andere Tabellen bleiben unverändert.
- **DJVU → PDF** (`EbookDjvuConverter`, alle 30 s ein Buch, `ddjvu -format=pdf`, Deckel 30 min): Bücher NUR mit DJVU
  (`EbookFormats.NeedsPdfConversion`) bekommen `converted/<id>.pdf` unter `Ebooks:Root` als `EbookFile` mit `Derived = true`
  (liegt immer unter der beschreibbaren Wurzel, wird mit dem Buch gelöscht; `EbookStorage.ResolveFile` wählt die Wurzel).
  Stand am Buch `ConversionState` (NotNeeded/Pending/Done/Failed); Upload/Import/Migration setzen Pending. Gemessen: 227
  Seiten in 13 s, ~6× die DJVU-Größe (Bibliothek: 66 Bücher nur als DJVU). Danach Titelbild + Diagramme neu vorgemerkt.
  Seit 0.837.5 mit `-scale=150 -quality=60` (`ReadingDpi`/`DjvuQuality`): vorher schrieb `ddjvu` die Scan-Auflösung fast
  unkomprimiert (Euwe's Best Games 28 MB DJVU → 622 MB PDF, bis 1,3 GB; der Leser lud minutenlang), jetzt ~1/7. `-mode=black`
  wäre noch kleiner, verliert aber die Diagramme (liegen in der Hintergrundebene). Hat ein Buch schon eine Lesefassung aus
  der DJVU und steht wieder auf Pending, wird sie am selben Pfad neu erzeugt (nur Datei + Größe; Titelbild/Diagramme
  bleiben). Migration `EbookDjvuReadingPdfSmaller` stellt alle so umgewandelten Bücher einmal auf Pending (Prod: 88).
- **Lesefassung übergroßer Scans** (0.775.4): ein PDF mit mehr als 1,5 MB je Seite (`EbookFormats.IsHeavyPdf`, z. B. tiff2pdf
  mit 600-dpi-Farb-JPEGs, ~30 MP je Seite — pdf.js scheitert daran) bekommt eine abgeleitete Lesefassung: aus der beiliegenden
  DJVU (`ddjvu`), sonst neu gerendert (`pdftoppm` 150 dpi JPEG q80, `ImagePdfWriter` bettet die JPEGs unverändert ein).
  `EbookFormats.Primary` nimmt bei gleichem Format die abgeleitete Datei — der Leser zeigt die Lesefassung, der Download bietet
  beide. Ausgelöst vom Titelbild-Worker (erst dann ist die Seitenzahl bekannt); Migration `EbookHeavyPdfReadingCopy` reiht
  die vorhandenen ein (Dev 14.10.: 21 Bücher, u. a. „'64' Chess Review“ 296 MB → 16 MB in 43 s).
- **Übersicht** sortiert ab 0.775.4 standardmäßig „Neueste zuerst“ — seit 0.778.1 nach Erscheinungsjahr (`Ebook.Year`, ohne
  Jahr ans Ende, Gleichstand nach Aufnahmedatum).
- **Trenner** zwischen Buch und Brett (`.rd-split`, `role=separator`): ziehen (Pointer-Capture), ←/→ um 24 px, Doppelklick =
  Standardbreite; 260 px bis 70 % des Fensters, je Gerät in `localStorage` `rh.ebooks.panelWidth`. Unter 900 px ausgeblendet.

## Lösungen aus dem Buch (0.771.0)

`EbookSolutionFinder` läuft im Diagramm-Durchgang mit (`EbookDiagramExtractor` liest dafür den Text JEDER Seite als Zeilen —
gruppiert über die Grundlinie, nicht das Wortrechteck, sonst rutschen fette Züge/Figurinen in die Nachbarzeile; Spalte =
Seitenhälfte). Zwei Wege: (1) nummerierter Absatz („31. “ mit Leerzeichen; „31.Txh4“ ist ein Zug) auf einer ANDEREN Seite als
das Diagramm, Lösungsteil zuerst; (2) Text unter dem Brett in derselben Spalte + nächste Seite, bis „X to move“ des nächsten
Diagramms. Figurinen (SP-Fonts `¢£¦¥¤`, U+F0xx gefaltet, Unicode, RookHub U+E024…) → `KQRBN`, `†`→`+`, `‡`→`#`.
Hauptlinie: erster Zug mit passender Nummer/Seite („34...“ = Schwarz; mit `SideKnown=false` bestimmt die Nummer das Zugrecht),
ein angezweifelter Startzug (`?`, `?!`) wird übersprungen und nur dieselbe Zugnummer zählt danach; Prosa und Nebenvarianten
(falsche Nummer, Klammern) werden übersprungen, weiter geht es bei der erwarteten Nummer; Gera.Chess spielt jeden Zug — der
erste muss legal sein, sonst KEINE Lösung. Gespeichert: `SolutionUci`, `SolutionSan`, `SolutionText` (Absatz, ≤3000),
`SolutionPage`; die FEN bekommt Zugrecht + Zugnummer der Lösung. Migration setzt erkannte Bücher zurück auf Pending.
Gemessen 14.10.: Woodpecker 1 117 von 1 128 mit Lösung, erster Zug = Kurs 88 bei 1 113; Grooten 824 von 995 (Weg 2).
**Leser**: „Lösen“ (Hauptaktion, wenn eine Lösung da ist) — Züge gegen die Linie geprüft, Antworten nach 450 ms, falscher Zug
600 ms sichtbar, dann zurück; „Lösung zeigen“ mit Linie, Absatz und Sprung zur Lösungsseite.

## Stockfish-Prüfung + Engine-Lösungen (0.772.0)

`EbookSolutionCheckWorker` (alle 30 s ein Buch mit `DiagramState = Done` und `SolutionCheckState = Pending`; die Erkennung setzt
Pending neu) startet EINE Stockfish-Sitzung je Buch (`UciEngineSession`: Threads 1, Hash 64 MB, niedrige Priorität, Pfad wie der
Tipp-Generator, `Ebooks__StockfishPath`) und prüft jedes regelgerechte Diagramm (`EbookSolutionChecker`, Bedenkzeit
`Ebooks__EngineMoveTimeMs` = 400 ms, Engine-Lösungen ×3; `Ebooks__EngineCheck=false` schaltet ab):
- **Buchlösung:** erster Zug = bester oder höchstens 120 cp schlechter oder gewinnt noch ≥ 400 → `Confirmed`. Sonst die übrigen
  Startzüge derselben Nummer aus dem Lösungsabsatz (`EbookSolutionFinder.TryParseAll`) → der erste vertretbare ersetzt die Linie
  (`Replaced`); keiner → `Doubtful` (Linie bleibt, Leser warnt).
- **Keine Buchlösung:** Bewertung ≥ 200 cp oder Matt → Engine-Hauptvariante wird die Lösung (`SolutionSource = engine`, bis zum
  Matt bzw. 7 Halbzüge, `Engine`); sonst `NoPuzzle`. Studien ohne König u. Ä. (Gera.Chess lehnt ab) bleiben `Unchecked`.
- Feld `EngineEval` (cp für die Seite am Zug, Matt in n = ±(10000 − n)) im Diagramm-DTO, ebenso `solutionSource`, `solutionCheck`.
Gemessen 14.10. (300 ms): Woodpecker 1 129 bestätigt, 3 ersetzt (u. a. Nr. 631 jetzt = Kurs), 8 Engine-Lösungen, erster Zug = Kurs
1 118 von 1 124; Baird 755 Engine-Lösungen von 764 (Matt in 2/3), 7,7 min für beide Bücher.

**Nur ungeprüfte Diagramme** (0.811.1): Ein Prüflauf nimmt nur lesbare Diagramme mit `SolutionCheck = Unchecked` (0)
(`EbookSolutionCheckWorker.ToCheck`). Geprüfte (`Confirmed`/`Doubtful`/`Replaced`/`Engine`/`NoPuzzle`) behalten Lösung und
`EngineEval` — vorher rechnete jedes Einreihen das ganze Buch neu und Lösungen wechselten. Ungeprüft sind neue Diagramme
(Erkennung/Ersetzen-Neulauf = alle, Ergänzen = die neuen), per Editor korrigierte und per SQL zurückgesetzte (extern korrigiert:
`SolutionCheck=0, EngineEval=NULL, SolutionUci/San/Text/Source=NULL`). Ohne prüfbares ungeprüftes Diagramm startet keine Engine,
das Buch wird trotzdem `Done`; unprüfbare Stellungen (kein König, Gegner im Schach) bleiben `Unchecked`. Die Log-Zeile zählt
über das ganze Buch. **Vollprüfung** (z. B. andere Bedenkzeit) gibt es nur per SQL: Engine-Lösungen verwerfen
(`SolutionUci/San/Source = NULL where SolutionSource = 'engine'`, sonst gälten sie als Buchlösung), alle Diagramme des Buchs
`SolutionCheck = 0, EngineEval = NULL`, dann `SolutionCheckState = 0` (Pending), `SolutionCheckQueuedAt = now()`.

**Fertig-Meldung** (0.774.0): `EbookProcessing.IsProcessing` (Titelbild, DJVU→PDF, Erkennung, Stockfish-Prüfung — letztere nur
mit Diagrammen und eingeschalteter Prüfung) → DTO `processing` (Karte „Wird verarbeitet …“). Jeder Worker ruft nach seinem
Schritt `NotifyIfReadyAsync`: bei Uploads (nicht bei Bibliotheksimporten) genau EINE Glocken-Meldung `ebook_ready`
(`title`, `diagrams`, Link `/ebooks/{id}`), Merker `Ebook.ReadyNotifiedAt`. Push-Kategorie „other“.

**Admins hören von fremden Uploads** (0.788.0, Wunsch 15.10.2026): lädt jemand OHNE E-Book-Verwaltung (Nutzer ohne
`ebooks.manage` oder ohne Anmeldung) ein Buch hoch, bekommen alle messages.admin-Konten die Glocken-Meldung `ebook_uploaded`
(`title`, `username` — leer = ohne Anmeldung → Text `ebook_uploaded_anonymous`; Link `/ebooks/{id}`; Push-Kategorie
„admin“). Der Hochladende selbst bekommt keine. Best-effort in `EbookService.NotifyAdminsOfUploadAsync`: scheitert die
Meldung, bleibt der Upload trotzdem gespeichert (Warnung im Log).

## EPUB-Diagramme aus Bildern (0.775.0)

In EPUBs sind Diagramme BILDER (Bibliothek 14.10.: ~3 100 Brettbilder in 27 EPUBs). Der `EbookDiagramWorker` nimmt bei Büchern
ohne PDF die EPUB-Datei und ruft `EbookEpubDiagramExtractor`:
- **Lesen** (`EbookEpubContent`): `container.xml` → OPF → Spine; je Kapitel Absätze (Block-Tags trennen, Inline-Tags nicht —
  „33.<span>♖</span>d1“ bleibt ein Zug) und Bilder (`<img src>`, SVG `<image href>`) als ZIP-Pfad, in Lesereihenfolge. Ohne
  XML-Parser für Kapitel (viele EPUBs sind kein wohlgeformtes XHTML).
- **Brett finden + Felder** (`EbookBoardImage`): Grau (0,299 R + 0,587 G + 0,114 B, Alpha auf Weiß), größte dunkle Komponente
  (&lt; 235/255, 8er-Nachbarschaft), annähernd quadratisch, Rahmen + weißen Rand abschälen (scheitert das, zweiter Versuch mit
  geschlossenen Lücken bis 2 px — rahmenlose Bretter, deren dunkle Felder sich nur an unscharfen Ecken berühren, 0.812.0); 8 × 8 Felder mit 6 % Rand,
  Flächenmittel auf 32 × 32 (`BoardImageMath`). Hintergrund je Feldposition = Median der LEEREN Felder des Buchs (höchstens
  240 Bretter, gleichmäßig verteilt) — ein Buch hat einen Brettstil, darum erst alle Bretter sammeln, dann erkennen.
- **Besetzt?** Abweichung vom Hintergrund nach Gauß σ 1,6: &gt; 6 % der Innenpixel über 0,18 = sicher besetzt; sonst Spitze
  &gt; 0,12 = Kandidat, den Modell 2 („leer“ ≤ 0,5) bestätigen muss. **Welche Figur?** Modell 1 auf dem Vordergrund, zentriert
  auf den Tintenschwerpunkt, 24 × 24.
- **Modelle** (`EbookBoardClassifier`, `Assets/ebook-board-model.bin`, je eine verdeckte Schicht 128 ReLU + Softmax): trainiert
  auf den Feldern der Font-Bücher (Stellung bekannt, Seite als Bild gerendert, 10 Bücher, ~1 000 Bretter). Training + Export
  laufen außerhalb des Repos in Python (`ref.py` = dieselben Operationen; Abweichungen in `BoardImageMath` verschieben die
  Merkmale gegenüber dem Training — keine Bibliotheks-Resize/Blur verwenden). Synthetische Bretter aus den App-Figurensätzen
  verschlechterten die echten Bücher (100 Endgame Studies 91 → 79 %) und sind nicht drin.
- **Nur plausible Stellungen** (je ein König, keine Bauern auf 1./8. Reihe) werden Diagramme. **Zugrecht** aus „White/Black to
  move|play“ / „Weiß/Schwarz am Zug“ im Absatz davor/danach, sonst aus der Lösung (Zugnummer). **Nummer**: kurzer Absatz aus nur
  einer Zahl (auch „Exercise 12“) bis drei Absätze vor dem Bild. **Lösung**: `EbookSolutionFinder` über die Absätze (Kapitel =
  „Seite“, Absatz-Reihenfolge = „Höhe“); die Stockfish-Prüfung läuft danach wie beim PDF.
- **Ablage**: `EbookDiagram.Page` = Kapitel (Spine, 1-basiert), `ImageRef` = Bildpfad im EPUB (DTO `imageRef`), Lage 0,
  `FontFamily = image`. Die Migration `EbookDiagramImageRef` setzt EPUB-Bücher ohne PDF von `Unsupported` zurück auf Pending.
- **Leser**: `EpubViewerComponent` markiert erkannte Bilder im Kapitel (`rendition.hooks.content`; epub.js ersetzt die
  Adressen im Archiv-Modus durch blob:-URLs → Rückübersetzung über `book.resources`; als Ordner wird die relative Adresse
  gegen die `<base>` des Kapitels aufgelöst und die Buch-Basis abgeschnitten — `epubImagePath`; zur Not über den Dateinamen); Doppelklick/Enter holt die
  Stellung aufs Brett, ‹/› springt kapitelweise zum Bild (`revealImage`, CFI des Elements). Brett-Leiste wie beim PDF.
- **Zugnummern ohne Punkt** (0.775.1): `EbookSolutionFinder` nimmt auch „1 Kc5 Kc7 2 Ke6“ und „1…Kc7“ — eine nackte Zahl gilt
  nur, wenn direkt ein vollständiger SAN-Zug folgt. Dvoretsky 3 → 937 Lösungen, Swindler 720 → 754, PDF-Bücher unverändert.
- Gemessen (Python-Referenz, plausibel erkannt): Swindler 905/906, Kuljasevic 456/459, Srokovski 490/501, Dvoretsky 513/544,
  100 Endgame Studies ~290/321, Perpetual Chess ~85/103; erster Buchzug legal bei Srokovski 96 %. Scans (DJVU, Scan-PDFs) =
  Stufe 2, offen.

## Diagramme aus Scans (Stufe 2, 0.776.0)

Findet `EbookDiagramExtractor` im PDF keinen Schach-Font, übernimmt `EbookScanDiagramExtractor` (`Ebooks__ScanDiagrams=false`
schaltet ab): Seiten mit `pdftoppm -r 300 -gray` als PGM (zehnmal schneller als PNG), 2 × 2 gemittelt, Gauß σ 1 — DJVU-Scans
sind schwarz-weiß und pdftoppm glättet Bilder nicht; ohne den Weichzeichner zerfällt die Schraffur der dunklen Felder in lose
Striche (Beim: 85 statt 166 Bretter). Gerendert wird die leichte Lesefassung, wenn es eine gibt (`EbookFormats.ReadingPdf`).
- **Bretter je Seite** (`EbookBoardImage.FindBoards`): jede dunkle Komponente ab 100 px, annähernd quadratisch, abgeschält;
  fehlt eine Rahmenlinie, wird auf ein Quadrat ergänzt — auf der Seite, deren Schachbrettmuster am besten passt (`GridFit`).
  Nur `GridScore` ≥ 52 (Randzone von ≥ 52 der 64 Felder näher am eigenen als am fremden Paritäts-Hintergrund) zählt — das
  verwirft Ausschnitte aus 3 × 3 Feldern, Fotos, Tabellen. Liegt der Wert darunter, wird das Rechteck schrittweise bis 3 % der
  Brettbreite nach innen genommen (dicker Rahmen verschiebt sonst das Raster, 0.813.0).
- **Rechteck bis an den Rahmen** (0.815.3, `EbookBoardImage.TrimToFrame`): gescannte Bretter sind oft nicht quadratisch; die
  Brettsuche ergänzt zum Quadrat und nahm einen Streifen Weiß/Bildunterschrift mit → jede Stellung eine Reihe verschoben
  (Flear „Open Ruy Lopez“ 7 → 35 von 40). Je Kante nach innen (≤ 16 %) die erste über 70 % dunkle Linie suchen; gekürzt wird
  nur, wenn der Streifen davor höchstens 25 % dunkel ist. Prüfsatz 19 Bücher: sonst keine Änderung.
- **Kleine Seiten, getöntes Papier** (0.813.0): Seiten, deren lange Seite bei 300 dpi unter 2 400 px bliebe (Google-Vorschau-
  Auszüge, 2 × 3 Zoll), werden feiner gerendert (`MinPagePixels`, gedeckelt durch `MaxPagePixels`). Ist der häufigste Grauwert
  der Seite (Papier) dunkler als 0,96 (bis 0.823 0,9; Farbscans von Internet Archive / Google Books, Papier 155–233), wird
  die Seite durch ihn geteilt, bis das Papier weiß ist (`NormalizePaper`) — sonst liegt die ganze Seite unter der Schwelle der
  Brettsuche (235/255 ≈ 0,92; Papier zwischen 0,9 und 0,92 fiel bis 0.824.0 durch die Lücke).
- **Rahmenlose Bretter** (0.824.0, Gambit-Scan Kislik „Applying Logic in Chess“: 2 statt ~400 Bretter): ohne Rahmen und mit
  hauchdünn schraffierten dunklen Feldern berühren sich die Felder nur an den Ecken, keine Komponente erreicht Brettgröße.
  Findet `FindBoards` auf einer Seite nichts, verdickt ein dritter Durchgang die dunkle Maske um `FrameLessDilate` = 3 px
  (Arbeitsbild ≈ 150 dpi; 4 px ließ Textblöcke verschmelzen) und schrumpft das Rechteck danach zurück; Textblöcke fallen über
  `GridScore` heraus. Gegenprobe 18 Bücher mit Opus-Lesungen: unverändert; Kislik-Probe 48 Seiten: 0 → 23 Bretter (19 sicher).
- **Brett im grauen Passepartout** (0.876.3, Prod #1292 Polgar „Amazing Artist – Dangerous Tactician“: Text-PDF ohne
  Schachfont, hellblaue JPEG-Bretter in einem breiten grauen Rand mit a–h/1–8; 1 statt ~150 Bretter): der Rand hat den
  Grauwert der dunklen Felder (~0,68), gehört zur selben dunklen Komponente und ist weder Rahmenlinie noch weiß — `Peel`
  lässt ihn stehen, das Raster lag quer über Rand und Koordinaten (`GridScore` −58, helle und dunkle Felder gleich).
  Scheitert `GridScore` (auch nach dem Hineinnehmen), nimmt `EbookBoardImage.LightExtent` das Rechteck bis an die hellen
  Felder: längster Lauf von Zeilen/Spalten mit ≥ 5 % hellen Pixeln (> 235/255, `LightLineShare`), Lücken bis ½ Feld
  (Gitterlinien, Figurenreihen) überbrückt — die Eckfelder a8/h1 sind immer hell, der Rand hat keine. Nur wenn das um
  ≥ 5 % schrumpft (sonst nur Rahmenlinien) und ≥ 52 erreicht, gilt das neue Rechteck (`SquareUp` ergänzt zum Quadrat).
  Der Zugrecht-Marker wird dann am äußeren Rechteck gesucht, zusätzlich (nur für solche Bretter — bei Flear traf es sonst
  Rahmenreste) ein Dreieck rechts NEBEN der Unterkante
  (`EbookBoardImage.BelowRightTriangle`, △ = Weiß, ▼ = Schwarz; nur eine Form, deren Zeilenbreite oben/unten um mehr als
  die Hälfte abweicht, und nicht mehr als zwei zeichengroße Teile im Suchfeld — Fließtext). Bretter, die vorher schon
  gefunden wurden, ändern sich nicht (Vergleich alt/neu je 70 Seiten Stean, Kislik, Yusupov, Beim, Flear: identisch);
  das Netz liest die hellblauen Bretter ohne Nachtraining (Kontrast 0,95/0,66).
  Gemessen Polgar: 1 → 155 Bretter (153 plausibel; jedes Brettbild des Buchs, auch Doppelbilder), Zugrecht 90 w / 55 b /
  10 offen; Stichprobe 6 Stellungen + Zugrecht richtig.
- **Hintergrund je Brett** (Median der eigenen hellen/dunklen Felder), nicht je Buch: die Schraffur liegt in jedem Scan anders.
- **Zwei Scan-Modelle**: `ebook-board-model-scan.bin` (`EbookBoardClassifier.Scan`, Font-Diagramme „gescannt“: weichgezeichnet,
  verrauscht, meist binarisiert — Gambit/Batsford-Figuren, Beim 95 % plausibel) und `ebook-board-model-scan2.bin` (`ScanAlt`,
  zusätzlich synthetische Bretter aus den App-Figurensätzen, doppelt fein gerendert und wie die Pipeline verkleinert — besser
  bei fremden dünnen Figuren, Buckley 10 → 28 %, sonst schlechter). Je Buch gewinnt das Modell mit mehr plausiblen Brettern,
  ein Brett, das es nicht lesen kann, nimmt das andere (`EbookScanDiagramExtractor.Choose`). Training außerhalb des Repos
  (`~/claude/ebook-board-training`).
- **Zugrecht**: Marker links neben dem Brett (Gambit/Batsford „W“/„B“), gelesen in 300 dpi: Striche ≤ 4 px auseinander werden
  ein Zeichen (dünne Kursive zerfällt sonst), das größte mit 3–12 % der Brettbreite Höhe zählt; „B“ hat Innenräume, „W“ nicht
  (`EbookBoardImage.SideMarker`, Hansen/Beim 23/23 — das Seitenverhältnis allein trennt bei Hansen nicht). Sonst Weiß,
  `SideKnown=false`. Hat das PDF eine OCR-Textebene, sucht `EbookSolutionFinder` dort Lösungen; sonst übernimmt Stockfish.
- Ablage wie Font-Diagramme (Seite, Lage als Seitenanteil → Zonen im PDF-Leser), `FontFamily = scan`. Migration
  `EbookScanDiagrams` setzt PDF-Bücher von `Unsupported` zurück auf Pending.
- Parität C# = Python-Referenz (Beim, 157/157 Stellungen). Laufzeit ~0,5 s je Seite. Gemessen: Beim 162, Hansen 46, Buckley 31
  Diagramme (Buckley: fremder Figurensatz, viele Bretter unlesbar — offen).

## Nachgetragene Diagramme schützen (0.782.0)

Scheitert die Erkennung (Pongó „Tactical Targets 1“: 118 von 978), werden die Stellungen außerhalb der App gelesen (Opus-Vision,
`FontFamily = opus`, `Label` = Aufgabennummer) und per SQL eingespielt. `Ebook.DiagramsCurated = true` markiert solche Bücher:
`RescanDiagramsAsync` lässt sie aus, und der `EbookDiagramWorker` setzt ein trotzdem vorgemerktes Buch (Recheck-Migration,
DJVU-Umwandlung) nur zurück auf Done und lässt die Diagramme stehen. Die Migration `EbookDiagramsCurated` markiert alle
Bücher mit `opus`-Diagrammen. Einspielen: alte Zeilen löschen, neue einfügen, `DiagramState=1`, `DiagramCount`,
`SolutionCheckState=0` (Stockfish prüft neu), `DiagramsCurated=1`.

## Diagramme ergänzen statt ersetzen (0.811.0)

`POST /api/admin/ebooks/{id}/diagrams/supplement` setzt `Ebook.DiagramSupplement = true` und `DiagramState = Pending`. Der
`EbookDiagramWorker` erkennt das Buch dann neu — **auch wenn `DiagramsCurated`** —, ersetzt aber nichts: `EbookDiagramMerge`
lässt alle vorhandenen Diagramme unverändert (FEN, Familie, Lösungen, Ids) und fügt neue nur an freien Stellen hinzu
(EPUB: gleicher `ImageRef` = vorhanden; PDF/Scan: gleiche Seite und IoU > 0,4 oder Mittelpunkt in der Box). Danach
`DiagramSupplement = false`, `Done`, `DiagramCount` neu, Stockfish-Prüfung nur, wenn lesbare Diagramme dazukamen (sie prüft
nur die neuen, ungeprüften — 0.811.1); Kurs-Verweise werden nur dann neu gesucht. Scheitert die Erkennung (Datei fehlt,
Fehler), bleibt ein Buch mit Diagrammen `Done`. `DiagramsCurated` bleibt, wie es war.

- **Ungelesene Bretter** (`FontFamily = "unread"`, `Confidence = 0`): im Ergänzen-Modus werden Brettbilder/Scan-Bretter mit
  unplausibler Lesung (`EbookBoardImage.GridToBoard` = null: nicht genau ein K/k, Bauern auf Reihe 1/8, Material) nicht mehr
  verworfen, sondern mit der rohen Lesung (`GridToPlacement`, immer syntaktisch gültig, Zugrecht w, ohne Lösungssuche)
  angelegt. Für Nutzer UNSICHTBAR über `EbookDiagramVisibility.Readable()`: Leser-Liste, „Diagramm falsch“, Kurs-Verknüpfung
  (Link + Relink), Kurs aus Buch, Stockfish-Prüfung, Fließtext-EPUB, `DiagramCount` (Katalog, Filter „mit Diagrammen“,
  Fertig-Meldung). Werkzeuge (SQL/Opus) sehen sie; wer ein `unread` per „Stellung korrigieren“ (Diagramm-Id) korrigiert,
  macht es zu `hand` und damit sichtbar — sonst die Familie beim Nachtragen ändern (z. B. `opus`).
- **Reihenfolge EPUB**: EPUB-Diagramme bekommen `Y = Blockreihenfolge × 10` im Kapitel (vorher 0 → Reihenfolge im Kapitel
  zufällig); `W` bleibt 0. Im Ergänzen-Modus wird vorhandenen EPUB-Diagrammen mit `Y = 0` die Höhe über den `ImageRef` nachgetragen.
- Das 8-MB-Limit der EPUB-Erkennung gilt je BILD im EPUB (`MaxImageBytes`), nicht für die ganze Datei.

## Externe Diagramm-Worker + Vorrang (0.870.0)

Die Erkennung läuft sonst nur im API-Container (~40 Bücher/h, Server ohnehin ausgelastet). Ein **externer Diagramm-Worker**
(Patriks Laptop, `~/claude/ebook-board-training/worker/worker.py`) nimmt ihm Bücher ab: er leiht ein wartendes Buch, lädt
die Dateien, erkennt mit **`tools/DiagramCli`** (`rookhub-diagrams`, derselbe Erkennungscode: `EbookDiagramRecognizer`) und
schickt das Ergebnis. Gespeichert wird über **`EbookDiagramResults`** — denselben Weg wie beim eigenen `EbookDiagramWorker`
(ersetzen bzw. Ergänzen-Modus mit `EbookDiagramMerge`, `DiagramCount`, `Done`/`Unsupported`, Stockfish-Prüfung einreihen,
Kurs-Verweise, Fließtext-EPUB, Fertig-Meldung).

**Leihe** (`EbookDiagramLease`, Spalten `DiagramLeaseId/Owner/Until`): das Buch bleibt `Pending`; wer es rechnet, hat eine
Leihe — der eigene Worker (`server`, 12 h, nach einem Neustart zurückgegeben), ein externer (`pat:<Token-Id>`, 30 min,
per Lebenszeichen verlängert). Übernommen wird atomar (`UPDATE … WHERE Pending AND Leihe frei`); der eigene Worker überspringt
Bücher unter fremder Leihe. Läuft eine Leihe ab, ist das Buch von selbst frei; ein spätes Ergebnis wird abgelehnt (409/404).

**Vorrang**: `POST /api/admin/ebooks/{id}/diagrams/supplement` setzt zusätzlich `DiagramPriority` — das Buch steht in
`QueueOrder` vor allen anderen (auch vor Uploads), für den eigenen wie für externe Worker. Jeder Abschluss löscht Leihe,
`DiagramPriority` und `DiagramExternalFailed`.

Endpunkte (`DiagramWorkerController`, `ebooks.manage` + API-Token mit Scope **`diagrams`**; der Scope-Zaun lässt so einen
Token NUR auf `/api/diagram-worker`; ein Admin-Browser-Login kommt zum Ausprobieren auch durch):

| Methode | Pfad | Zweck |
|---|---|---|
| POST | `/api/diagram-worker/claim` | `{ worker?, cliVersion? }` → 200 Auftrag `{ jobId, ebookId, title, supplement, pageCount, leaseUntil, heartbeatSeconds, files: [{ fileId, role, format, sizeBytes, url }] }`, 204 = nichts zu tun |
| GET | `/api/diagram-worker/jobs/{jobId}/files/{fileId}` | Datei des Auftrags (Range erlaubt), nur solange die Leihe gilt |
| POST | `/api/diagram-worker/jobs/{jobId}/heartbeat` | Leihe +30 min → `{ leaseUntil }`; 409 = Leihe weg (abbrechen) |
| POST | `/api/diagram-worker/jobs/{jobId}/release` | `{ error? }` → 204. Mit `error`: `DiagramExternalFailed`, externe Worker nehmen es nicht wieder, der Server erkennt es selbst |
| POST | `/api/diagram-worker/jobs/{jobId}/result` | `DiagramWorkerResultDto` (genau die Ausgabe von `rookhub-diagrams`) → `{ ebookId, state, diagramCount }`; 400 ungültig, 409/404 Leihe weg |

- **Was externe Worker bekommen**: `Pending`, Leihe frei, nicht `DiagramExternalFailed`, nicht „kuratiert ohne Ergänzen“
  (macht der Server ohne Erkennung), mit PDF (auch umgewandeltem DJVU) oder echtem EPUB. Reine DJVU ohne PDF-Fassung bleiben
  dem Server (er setzt sie auf `Unsupported`, nach der Umwandlung werden sie wieder `Pending`). Rollen der Dateien wie im
  Worker (`EbookDiagramRecognizer.Choose`): `primary` (Original-PDF vor abgeleitetem PDF vor EPUB), `reading` (leichte
  Lesefassung zum Rendern von Scans), `epub` (echtes EPUB zum Verknüpfen / bevorzugt bei Scan-PDF).
- **Prüfung des Ergebnisses** (`EbookDiagramWorkerService.Validate`): `ebookId` = Buch der Leihe, ≤ 50 000 Diagramme, Rumpf
  ≤ 128 MB, Längen wie die Spalten, Lage endlich und ≥ 0 (PDF: Seitenanteil ≤ 1; EPUB-Bild: `Y` = Reihenfolge), Familie aus
  `ChessDiagramFonts` + `scan`/`cnn`/`image`/`image-cnn`/`unread` (nie `opus`/`hand`), `SolutionSource` nur `book`, FEN
  syntaktisch gültig; lesbare Diagramme zusätzlich höchstens ein König und 16 Steine je Farbe (Illustrationen ohne König
  bleiben erlaubt). Ein Fehler lehnt das ganze Ergebnis ab — der Worker gibt das Buch dann mit `error` zurück.
- **Token anlegen**: Profil → API-Tokens → Bereich „Diagramm-Worker“ (nur sichtbar und erlaubt mit `ebooks.manage`;
  `POST /api/profile/tokens { name, scope: "diagrams" }` ohne das Recht → 403). Ein Token = ein Worker (Leihe je Token).
- **nginx**: eigene Location `/api/diagram-worker` (130 MB Rumpf, ungepuffert, 1 h) — die generische `/api/`-Regel deckelt
  auf 15 MB (`DeploymentConfigTests`). Vor dem Stack steht NPM: dessen Rumpf-Grenze muss das ebenfalls erlauben.

**`tools/DiagramCli`** (`rookhub-diagrams`, verweist auf das API-Projekt, Modelle eingebettet):

```
rookhub-diagrams --ebook-id 123 --primary buch.pdf [--reading lesefassung.pdf] [--epub buch.epub] [--format pdf|epub]
                 [--supplement] [--threads N] [--poppler <bin-Ordner>] [--out ergebnis.json] [--verbose]
rookhub-diagrams --version
```

`--threads` = parallele Seitenstapel beim Scan (`EbookOptions.ScanParallelism`); das Faltungsnetz rechnet je Stapel einfädig
(`EbookBoardCnn.IntraOpThreads = 1`, Server bleibt bei 2). Scans brauchen `pdftoppm` + `pdfinfo` (Poppler) im PATH; fehlen
sie, bricht das Programm mit Rückgabe 3 ab (sonst liefe ein Scan still mit 0 Diagrammen durch). Rückgaben: 0 fertig, 1
Erkennung gescheitert, 2 Aufruf falsch, 3 Poppler fehlt, 4 Datei fehlt. Protokoll auf stderr.

Windows-Build (auf dem Server, ~210 MB entpackt):

```bash
export PATH="$HOME/.dotnet:$PATH"
V=<APP_VERSION>; OUT=/srv/media/claude-sessions/diagram-worker/publish-$V
dotnet publish tools/DiagramCli -c Release -r win-x64 --self-contained -p:Version=$V -o $OUT
(cd $OUT && zip -qr /srv/media/sftp/kahalm2/ebook-board-cells/_tools/rookhub-diagrams-$V-win-x64.zip .)
echo $V > /srv/media/sftp/kahalm2/ebook-board-cells/_tools/rookhub-diagrams.version
```

Der Laptop-Worker liest `_tools/rookhub-diagrams.version` über WebDAV und lädt das Zip bei neuer Version
(Anleitung: `~/claude/ebook-board-training/worker/README.md`). Eine neue Erkennung im Server sollte immer mit einem neuen
Build auf den Laptop — sonst rechnen beide verschieden.

**Als Container (0.873.0)**: `tools/DiagramWorker` — dasselbe `rookhub-diagrams` im Image (`ghcr.io/kahalm/rookhub-diagram-worker`,
amd64 + arm64, Workflow `diagram-worker.yml` bei jedem Release-Tag) plus ein schlanker Python-Worker nur für die
Diagramm-Erkennung (`worker.py`, Test `scripts/tests/test_diagram_worker.py`). Gedacht für den DGX Spark und andere
Linux-Rechner; Einrichten: `tools/DiagramWorker/README.md`. Die Laptop-Fassung (Windows, mit Brett-Aufträgen) bleibt.

## Brett-Ausschneide-Aufträge (0.874.0)

Die Opus-Lese-Schleife (`ebook-board-training/fs`) braucht je Buch ausgeschnittene Bretter (`cells.npz`, `crops/`). Statt
über Dateien im WebDAV-Bereich laufen die Aufträge jetzt (zusätzlich, WebDAV bleibt bis zur Umstellung von `remote.py`)
über RookHub: Tabelle **`BoardCutJobs`** (`BoardCutJobService`), Leihe wie bei den Diagramm-Workern (30 min, Lebenszeichen).
Der Container-Worker (`tools/DiagramWorker`, gleicher Token mit Scope `diagrams`) nimmt Brett-Aufträge VOR der
Diagramm-Erkennung; das Ausschneiden ist wörtlich der Laptop-Code (`boardcut/`, geprüft bytegleich an #3615).

| Methode | Pfad | Zweck |
|---|---|---|
| POST | `/api/admin/board-jobs` | Admin: `{ ebookId, fileId?, payload }` (payload = bisheriges Auftrags-JSON mit `pages`) → Auftrag; ein wartender älterer für dasselbe Buch wird `Cancelled` |
| GET | `/api/admin/board-jobs?state=Pending` · `/api/admin/board-jobs/{jobId}` | Stand (`Pending`/`Done`/`Failed`/`Cancelled`, Worker, Fehler) |
| POST | `/api/admin/board-jobs/{jobId}/cancel` | wartenden Auftrag zurückziehen |
| POST | `/api/board-worker/claim` | Worker: `{ worker? }` → `{ jobId, ebookId, title, payload, file: { url, … }, leaseUntil, heartbeatSeconds }`, 204 = nichts |
| GET | `/api/board-worker/jobs/{jobId}/file` | das PDF (Original vor abgeleitetem, wie `prep.book_file`) |
| POST | `/api/board-worker/jobs/{jobId}/heartbeat` · `…/release` | Leihe verlängern · zurückgeben (`{ error }` = fehlgeschlagen) |
| POST | `/api/board-worker/jobs/{jobId}/result` | multipart `cells` (npz), `prep` (JSON), `crops` (ZIP nur `<Diagramm-Id>.png`) |

Ablage: **`<Ebooks:Root>/board-cells/<EbookId>/`** (Prod-Host `/srv/media/rookhub-ebooks/prod/board-cells/<EbookId>`),
dieselben Dateien wie bisher unter `ebook-board-cells/b<id>`. Überschrieben wird, gelöscht nie („Bretter nie löschen“);
`prep.json` kommt zuletzt (sein Dasein heißt „fertig“), jede Datei erst als `.part`. Der Admin-Teil ist für den Worker-Token
gesperrt (Scope-Zaun).

## Brett-Modi im Leser (0.777.0)

Die Brett-Leiste ist eine eigene Komponente (`features/ebooks/ebook-board.component.*`, `app-ebook-board`, Eingabe das
gewählte Diagramm). Modi: **Ansehen**; **Ziehen** (Figuren frei, Liste mit Zurücknehmen); **Kalkulieren** (Variantenbaum wie
der Kalkulations-Modus der Kurse — `calc-tree.util` + `app-calc-lines`, je Diagramm in `localStorage` `rh.ebooks.calc.<id>`,
eine Linie öffnet `/analysis?fen=&moves=`); **Lösen** — Art „Einfach“ (falscher Zug wird zurückgenommen) oder „Training“
(erster Fehler = nicht gelöst, das Brett spielt die Lösung vor), Quelle „gespeichert“ (Buch- bzw. Engine-Linie der
Prüfung) oder „Stockfish jetzt“ (7 Halbzüge, `StockfishService.getBestMove` Tiefe 16 im Browser; ohne gespeicherte Lösung
voreingestellt); **Maia** (man spielt die Seite am Zug, Maia antwortet über `MaiaEngineService.chooseMove` in wählbarer
Stärke; Modell wird erst auf Knopfdruck geladen, ~46 MB). Engine-Knopf: `LiveEngineSession` + `app-live-engine-panel`
rechnet jede Brettstellung, in jedem Modus. Der Modus bleibt beim Blättern zu anderen Diagrammen.

## Partiensammlungen: Modus „Partie“ (0.831.0)

Für Bücher, deren Diagramme aus Partien stammen (Partiensammlungen, Turnier- und Wettkampfbücher, Engine-Partiensammlungen
wie die „Chess Notes“), zeigt das Brett die GANZE Partie ab der Diagrammstellung statt einer Aufgabe. Daten: Tabelle
`EbookGames` (je Buch, Kopf + Hauptlinie als SAN in `Moves`, Herkunft `Source` z. B. `prep:<PrepGameId>`/`pgn`) und je
Diagramm `EbookDiagrams.GameId` + `GamePly` (Halbzüge bis zur Diagrammstellung; FK `SetNull`, mehrere Diagramme teilen
sich eine Partie). Gelöscht wird mit dem Buch.

- `GET /api/ebooks/{id}/diagrams/{diagramId}/game` (ohne Anmeldung wie die Diagrammliste) → `{ id, white, black, event,
  date, result, moves[], ply }`; ohne Partie 404. Die Diagrammliste trägt `hasGame`.
- `PUT /api/admin/ebooks/{id}/games` (`ebooks.manage`) `{ replace, games: [{ key, white, black, event, date, result, moves,
  source }], links: [{ diagramId, gameKey, ply }] }` → `{ games, linked, skipped }`. Eine Verknüpfung zählt nur bei
  buch-eigenem Diagramm, mitgelieferter Partie und Halbzug innerhalb der Partie; unbenutzte Partien werden nicht
  gespeichert; `replace` entfernt vorher alle Partien des Buchs. Die Zuordnung selbst entsteht außerhalb (Diagramm-FENs
  gegen `PrepGames` der Hauptspieler bzw. die PGN des Buchs nachspielen).
- Brett-Modus `game` („Partie“, auch als `DefaultBoardMode` des Buchs wählbar): der Knopf erscheint nur bei Diagrammen
  mit Partie oder bei Büchern auf „Partie“. Kopf (Weiß – Schwarz · Turnier · Datum · Ergebnis), Schritt-Leiste (Anfang,
  ◀, ▶, Ende, „Diagramm“), Zugliste je Vollzug (aktueller Zug getönt, Diagrammzug unterstrichen, Klick springt). Ein
  Brettzug, der dem nächsten Partiezug entspricht, geht in der Partie weiter; jeder andere beginnt eine Nebenlinie
  (Zurücknehmen, „Zurück zur Partie“). Die Engine-Leiste ist wie in allen Modi über ⋮ zuschaltbar. Eine unspielbare
  Zugfolge wird beim ersten falschen Zug abgeschnitten (`EbookBoardComponent.replay`).

## Ohne Anmeldung (0.778.0)

Bibliothek und Leser gehen auch ohne Konto (Routen ohne `authGuard`, Menüpunkt `ebooks` steht standardmäßig auf `All` —
ein Admin-Override in der Menü-Sichtbarkeit hat Vorrang). Der Aufrufer ist dann die anonyme Kennung der App
(`X-Visitor-Id`, dieselbe wie Puzzles/Endlos, `ANON_PUZZLE_SESSION_KEY`; strenge Form `ValidationConstants.SessionIdPattern`),
im Backend `EbookCaller` (ein `int` wird implizit zum angemeldeten Aufrufer):
- **Sichtbar** (`EbookAccess.Visible`): Bücher mit Freigabe für die Gruppe „Everyone“ und die eigenen anonymen Uploads
  (`Ebook.AnonymousSessionId`, `OwnerUserId = null`). Verwalten (Bearbeiten/Löschen) nur die eigenen; Freigaben und
  „Diagramm falsch“ melden bleiben angemeldet (`[Authorize]`), `manageAll` gilt anonym nie.
- **Hochladen** (`POST /api/ebooks`, `[AllowAnonymous]` + `anonymous-write`): nur mit gültiger Kennung, Kontingent
  `Ebooks__AnonymousQuotaBytes` (300 MB, zugleich Dateigrenze), Gesamtdeckel aller anonymen Uploads `Ebooks__AnonymousTotalBytes`
  (10 GB), abschaltbar mit `Ebooks__AnonymousUploads=false` (→ 403 `ebook_anonymous_upload_disabled`). Nie für Gruppen freigegeben.
- **Lesestand**: eigene Tabelle `EbookAnonymousProgresses` (EbookId + SessionId).
- **Ausweis** für Datei/Titelbild: `GET access-token` gibt anonym einen Ausweis mit „a:“ + Kennung aus
  (`EbookAccessTokens`); die Sichtbarkeit wird je Anfrage neu geprüft. Das Frontend vergisst den Ausweis bei jedem
  Wechsel des Nutzers (An-/Abmelden), sonst sähe ein Abgemeldeter am selben Gerät noch die Bücher des Kontos.
- **Übernahme** beim Anmelden/Registrieren (`AuthService.claimAnonymousPuzzleSession` → `POST /api/ebooks/claim-session
  { sessionId }`, `[Authorize]`): die anonymen Uploads gehören dem Konto, anonyme Lesestände werden übernommen (je Buch
  gewinnt der jüngere) und gelöscht. Antwort `{ claimed }` = Zahl der Bücher.
- **Aufräumen**: `EbookAnonymousCleanupWorker` (stündlich) löscht nie übernommene anonyme Uploads nach
  `Ebooks__AnonymousRetentionDays` (30) Tagen samt Dateien, Titelbild und alten anonymen Leseständen.
- Oberfläche: Banner „nicht angemeldet“ mit Anmelden-Knopf (`returnUrl=/ebooks`), Hochladen-Dialog ohne Sichtbarkeit,
  stattdessen der Hinweis (nur du, bis 300 MB, wandert beim Anmelden ins Konto); Auswahl/Sichtbarkeit und Melden ausgeblendet.

**Blättern per Tastatur** (0.779.1): Der Leser hört dokumentweit auf ←/→/Bild↑/Bild↓ (PDF: Seite; ↑/↓: 80 px) bzw. im EPUB
auf Seiten — auch wenn der Fokus im Brett liegt. Ausgenommen: Eingabefelder, Menüs (`.cdk-overlay-container`), der
Seiten-Scrollkasten des PDF und der EPUB-Leser selbst (bekommen die Taste ohnehin), der Trenner, bereits verarbeitete Tasten.

**Mausrad im EPUB** (0.780.1): `EpubViewerComponent.onWheel` blättert seitenweise (Host und — per `rendition.hooks.content` —
im Kapitel-iframe, dessen Ereignisse den Host nicht erreichen). Raddrehung wird gesammelt (≥ 40 px = eine Seite: Chrome-Raste
100 px, Firefox 3 Zeilen ≈ 48 px), danach 350 ms Pause gegen Doppelblättern bei Touchpads.

**Lösen: gleichwertige Züge zählen** (0.780.3): Weicht der Zug von der Linie ab, bewertet Stockfish im Browser
(`StockfishService`, Tiefe 14) die Stellung danach gegen die nach dem Buchzug (nach einer Abweichung: gegen die Stellung
davor) aus Sicht des Ziehenden. Angenommen, wenn höchstens 50 cp schlechter, Remis bleibt Remis (beide ±80 cp) oder Gewinn
bleibt Gewinn (beide ≥ 300) — `EbookBoardComponent.sameResult`. Danach antwortet Stockfish statt der Buchlinie (auch beim
Vorspielen im Training). Die Lösungsanzeige nennt eine Remisstellung (`engineEval` ±80) als „nur ein Weg“.
Seit 0.879.0 gilt das nur noch, wenn die Lösung von Stockfish stammt oder die Prüfung den Buchzug als fraglich markiert
hat (`solutionCheck` `doubtful`). Sonst sagt das Brett „auch gut, das Buch spielt aber etwas anderes“, nimmt den Zug
ohne Fehler zurück und man sucht den Buchzug weiter (Nutzerwunsch 29.10.2026).

**Filter „Mit Diagrammen“** (0.780.4): nur Bücher mit mindestens 100 erkannten Diagrammen (`EbookService.MinDiagramsForFilter`) —
Partiensammlungen und Lehrbücher mit wenigen Stellungen fallen heraus.

**Scan-Plausibilität** (0.780.5): `EbookBoardImage.PossibleMaterial` — je Farbe ≤ 8 Bauern, Überzählige (D>1, T/L/S>2) nur so
viele wie fehlende Bauern; sonst wird das Brett verworfen (gilt für Scans und EPUB-Bilder). `FindBoards` entdoppelt
überlappende Treffer (Überdeckung > 50 % des kleineren, der mit besserem `GridFit` bleibt) — doppelte Rahmen (Pongó) lieferten
sonst ein zweites, verschobenes Raster. Pongó „Tactical Targets 1“ bleibt trotzdem schlecht (sehr feine Schraffur).

**Startmodus des Bretts je Buch** (0.781.0): `Ebook.DefaultBoardMode` (view/free/calc/solve/maia, `null` = Standard „solve“,
`EbookFormats.NormalizeBoardMode`), im Bearbeiten-/Hochladen-Dialog wählbar, im DTO immer gesetzt. Die Brett-Komponente
startet beim ersten Diagramm damit; danach bleibt der selbst gewählte Modus beim Blättern.

**Drosselung Datei/Titelbild** (0.781.1): `GET …/file` und `…/cover` tragen die Policy `ebook-file` (1 500/min je Adresse,
`RateLimitPartitions.EbookFilePolicy`) und sind vom globalen 100/min-Topf ausgenommen (`HasOwnIpCeiling`) — pdf.js lud ein
großes Buch in Hunderten Bereichsanfragen, die Adresse war danach eine Minute lang für alles gesperrt (429 → „Could not load“).

## Buch → Kurs (0.785.0)

`POST /api/ebooks/{id}/course` (angemeldet, Buch sichtbar) legt aus den Diagrammen einen PERSÖNLICHEN Kurs an
(`EbookCourseBuilder`): Diagramme mit Lösung (`SolutionSan`, aus dem Buch oder von Stockfish) werden Aufgaben, Diagramme
ohne Lösung Info-Linien (Stellung + Bildunterschrift); unspielbare Lehrbilder (ohne König u. Ä.) fallen weg. Titel der
Linie „Diagramm {Label}“, Reihenfolge wie im Buch. Läuft über den normalen PGN-Import (`UploadPersonalCourseAsync`).
Keine einzige Lösung → 400 (erst die Stockfish-Prüfung abwarten). Oberfläche: ⋮-Menü im Leser „Als Kurs anlegen“.

**Kapitel wie im Buch (0.805.0):** `EbookChapterIndex` liest das Inhaltsverzeichnis — PDF-Lesezeichen (Position = Seite) bzw. EPUB-`nav.xhtml`/`toc.ncx` (Position = Spine-Kapitel, URL-kodierte Dateinamen werden dekodiert verglichen). Ein Diagramm gehört zum letzten Eintrag an oder vor seiner Position; gegliedert wird nach der obersten Ebene (nur ein Eintrag oben → die Ebene darunter). PDF ohne Lesezeichen: über `EpubChapter` das Kapitel im echten EPUB bzw. im Fließtext-EPUB. Das Kapitel steht im PGN als `[Black]` (Kurs-Format).

## Sammlungen (0.787.0)

`EbookCollections` (Id, Name) + `Ebook.CollectionId` (FK, SetNull). In `GET /api/ebooks` erscheint ohne `collection` und
ohne Suche je Sammlung EIN Eintrag (das Buch mit der kleinsten Id unter den sichtbaren, gefilterten Büchern der Sammlung,
`collectionStub=true`, `collection={ id, name, bookCount }`) — die Liste zeigt ihn als Ordner. `?collection={id}` zeigt
nur die Bücher der Sammlung (`EbookListDto.collection` = Kopf). Mit Suche stehen Treffer einzeln da. Sichtbar ist eine
Sammlung, sobald eines ihrer Bücher sichtbar ist (keine eigene Freigabe). Seitengröße der Liste 60 (0.787.0).

| Methode | Endpoint | Recht | Zweck |
|---|---|---|---|
| GET | `/api/ebooks/collections` | `ebooks.manage` | alle Sammlungen mit Buchzahl |
| PUT | `/api/ebooks/collections/assign` | `ebooks.manage` | `{ ebookIds, collectionId?, newName?, additional?, fromCollectionId? }` — zuordnen / neu anlegen / (beides leer) herausnehmen; leere Sammlungen verschwinden. 0.861.0: `additional` = zusätzlich (Hauptsammlung `Ebooks.CollectionId` bleibt, weitere in `EbookCollectionLinks`), `fromCollectionId` = nur aus dieser nehmen (Hauptsammlung weg → erste weitere rückt nach) |
| PUT | `/api/ebooks/collections/{id}` | `ebooks.manage` | umbenennen `{ name }` |
| DELETE | `/api/ebooks/collections/{id}` | `ebooks.manage` | auflösen, die Bücher bleiben |

Logik: `EbookCollectionService`; Oberfläche: Auswahl → „Zu Sammlung …“ (`ebook-collection-dialog`), in der Sammlung
„Aus Sammlung nehmen“, ⋮ am Sammlungskopf Umbenennen/Auflösen.

## „Nicht erkanntes Diagramm?“ (0.788.0)

Doppelklick im PDF-Leser auf eine Stelle OHNE Diagramm-Zone (nur angemeldet) → Rückfrage „Ist hier ein Diagramm, das nicht
erkannt wurde?“ (Ja/Nein; Schließen speichert nichts). `POST /api/ebooks/{id}/missed-diagram` `{ page, x, y, isDiagram }`
legt eine Zeile in `EbookMissedDiagrams` an (Klickpunkt als Seitenanteil, Nutzer, Zeit) — Rohmaterial für die Auswertung,
welche Bücher/Seiten die Erkennung verpasst. Keine Oberfläche zum Auswerten; abfragen per SQL.

**EPUB (0.798.0):** im EPUB-Leser fragt ein Doppelklick auf ein Bild OHNE erkanntes Diagramm (oder auf Text) dasselbe
(`epub-viewer` → `missedPick`). Gespeichert wird `{ page: 0, isDiagram, epubRef, epubChapter }`: `EpubRef` = Bildpfad im
EPUB (wie `EbookDiagram.ImageRef`, bei Text `null`), `EpubChapter` = Spine-href des Kapitels. Regel: `Page ≥ 1` ODER
`EpubRef`/`EpubChapter` gesetzt, sonst 400.

## „Stellung korrigieren“ (0.798.0)

⋮ des Bretts → „Stellung korrigieren“ (nur, wer das Buch verwalten darf: Besitzer bzw. `ebooks.manage`; sonst ausgeblendet)
öffnet den Stellungs-Editor des Analysebretts (`PositionSetupComponent`, `requireLegal=false`) im Dialog
`ebook-position-dialog`: Figuren setzen/verschieben/entfernen, Weiß/Schwarz am Zug. `PUT /api/ebooks/{id}/diagrams/{diagramId}/position`
`{ fen }` prüft die FEN (8 Reihen à 8 Felder, genau ein König je Farbe, Zugrecht w/b; Rest ergänzt), setzt `EbookDiagram.Fen`
und `SideKnown = true` und antwortet mit dem aktualisierten Diagramm (das Brett zeigt es sofort). Dabei:
- **Immer** eine Zeile in `EbookDiagramCorrections` (EbookId, DiagramId, UserId, Page, OldFen, NewFen, FontFamily und
  Confidence des Diagramms zu dem Zeitpunkt, CreatedAt) — Material, um die Erkennung an diesen Fällen zu verbessern.
- Buch wird `DiagramsCurated = true` (kein Rescan überschreibt Hand-Korrekturen) und `SolutionCheckState = Pending`.
- Lösung: Buchlösung wird ab der neuen Stellung neu nachgespielt (nicht spielbar → keine Linie, Text bleibt), eine
  Engine-Lösung verfällt; `SolutionCheck = Unchecked`, Stockfish rechnet beim nächsten Prüflauf neu.

## Mehrere Formate eines Buchs: EPUB bevorzugt, ein Diagrammsatz (0.793.0)

- Leseformat (`EbookFormats.Preference`): **EPUB > PDF > DJVU > CBZ/CBR > MOBI/AZW3**. `PrimaryFormat` ist damit bei
  EPUB+PDF das EPUB; im Leser-⋮ „Als PDF lesen“/„Als EPUB lesen“ (`readAs`), beim PDF die Lesefassung (`EbookFileDto.Derived`)
  vor dem Original. Downloads aller Dateien wie bisher.
- Diagramme nur EINMAL: der `EbookDiagramWorker` erkennt aus dem PDF (Schachfont exakt) und verknüpft per
  `EbookFormatLinker` jedes Diagramm mit seinem Bild im EPUB (Needleman-Wunsch über die Buchreihenfolge, Ähnlichkeit =
  gleiche Steine auf besetzten Feldern, ab 0,6). Verknüpfte Diagramme tragen `ImageRef` + `EpubChapter` UND ihre PDF-Lage
  (`Page`/`X…`); reine EPUB-Diagramme wie bisher (`Page` = Kapitel, `W` = 0). Der Leser nimmt im PDF die Diagramme mit
  `W > 0` als Zonen, im EPUB die mit `ImageRef` (Kapitel `EpubChapter ?? Page`).
- Migration `EbookDiagramEpubChapter` reiht alle nicht kuratierten Bücher mit PDF+EPUB neu zur Erkennung ein.

## Faltungsnetz für Scan-Diagramme (0.794.0)

`EbookBoardCnn` (ONNX Runtime, eingebettet `Assets/ebook-board-cnn.onnx`) liest in `EbookScanDiagramExtractor.ScanPage` jedes
gefundene Brett aus der 300-dpi-Seite: Flächenmittel auf 256 × 256, 64 Felder zu 32 × 32 (Grauwert 0–1), Klassen
PNBRQKpnbrqk + leer. Ist die Lesung plausibel (`EbookBoardImage.GridToBoard`: je ein König, keine Bauern auf der Grundreihe,
mögliches Material), gilt sie (`FontFamily = cnn`, `EbookDiagram.Confidence` = Wahrscheinlichkeit des unsichersten Felds),
sonst wie bisher die MLP-Modelle. Trainiert auf ~3 400 Opus-Diagrammen (Pongó, 5 × Awerbach, Silman; Skripte
`~/claude/ebook-board-training/hr`); Gegenprobe ohne eigene Diagramme: Awerbach-Lehrbücher/Silman 99,5–100 %, andere
Awerbach 82–87 %, Wasserzeichen (Läufer/Springer) 64 %, Pongó (kleine Bretter) 2 % — dort braucht es eigene Beispiele.
Migration `EbookDiagramConfidence` reiht nicht kuratierte Scan-Bücher (Scan-Diagramme oder ohne Treffer) neu ein.
Neues Modell: ONNX (Eingabe `cells` [n,1,32,32], Ausgabe `logits` [n,13]) unter demselben Namen ablegen.

## Fließtext-EPUB aus Text-PDFs (0.802.0, Prototyp)

Gute Schach-PDFs (Textebene + Schach-Font, z. B. Woodpecker, Grooten) bekommen auf Zuruf ein sauberes EPUB 3 als
ABGELEITETE Datei `converted/<id>.epub` unter `Ebooks:Root` (`EbookFile.Derived`, wie die PDF-Fassung der DJVU-Bücher;
wird mit dem Buch gelöscht, ein zweiter Lauf ersetzt sie). Weil der Leser EPUB bevorzugt (0.793.0), liest man das Buch
danach fließend mit einstellbarer Schrift; „Als PDF lesen“ wechselt zurück. Ein ECHTES EPUB geht immer vor
(`EbookFormats.Primary`: beim EPUB das Original vor dem abgeleiteten, beim PDF umgekehrt — Frontend `fileFor` ebenso);
für Bücher mit echtem EPUB lehnt der Dienst ohnehin ab.

- **Auslöser**: `POST /api/admin/ebooks/{id}/reflow` (`EbookReflowService`). Automatisch nach der Diagrammerkennung nur mit
  `Ebooks__ReflowAfterDiagrams=true` (Vorgabe AUS — erst 2–3 Bücher beurteilen) und nur bei Font-Diagrammen. Hat ein Buch
  schon ein Fließtext-EPUB, baut der `EbookDiagramWorker` es nach jeder Neuerkennung neu (die neuen Diagrammzeilen
  brauchen ihre Bildverweise). Die EPUB-Verknüpfung (`EbookFormatLinker`) nimmt nur echte EPUBs.
- **Pipeline**: `EbookReflowPdfReader` (PdfPig: Glyphen mit Grundlinie/Größe/fett/kursiv, Diagramm-Fonts fallen weg,
  Lesezeichen) → `EbookReflowLayout` (Zeilen, Bundsteg-Erkennung für zwei Spalten — auch aus nebeneinanderstehenden
  Diagrammen —, Kolumnentitel/Seitenzahlen weg: Randstreifen + Wiederholung auf ≥ 3 Seiten oder ein Lesezeichen-Titel,
  Absätze über Zeilenabstand/Einzug/kurze Zeile/„erstes Wort hätte gepasst“, hängender Einzug der Variantenbücher,
  Trennungen zusammengezogen, Überschriften aus der Schriftgröße h1–h3 + ganz fette Einzeiler ohne Ziffern = h3,
  Kapitel aus den Lesezeichen der obersten zwei Ebenen, sonst h1/h2, sonst je 20 Seiten; Kapitel über 24 Seiten in Teile)
  → `EbookEpubWriter` (mimetype unkomprimiert zuerst, container.xml, OPF mit Titel/Autor/Sprache/Kennung/
  `dcterms:modified`, nav.xhtml mit Unterpunkten auf Seitenanker `#p<Seite>`, Titelblatt, je Kapitel ein XHTML).
- **Zeichen**: Figurinen der Figurinen-Schriften (`EbookSolutionFinder.FigurinePiece`/`IsFigurineFont`, SP-Fonts `¢£¦¥¤`,
  U+F0xx, Unicode, RookHub U+E024…) → ♔♕♖♗♘ in einer Spanne mit Symbol-Schriften; „£“ in normaler Schrift bleibt „£“.
  SP-Bewertungszeichen (² ³ µ ÷ „ ©) → ⩲ ⩱ ∓ ∞ ⇆ =/∞; Wingdings-Häkchen → ✓; Zugrecht-Dreiecke (Wingdings3) weg.
- **Figurensalat (0.818.0)**: Scan-PDFs mit Zeichenerkennungs-Textebene haben statt der Figurinen Salat vor dem Zugfeld
  („lt:Jxc4“, „�h3“, „'§'b8“). `EbookFigurineRepair` sucht solche Zugwörter (Präfix vor `x?[a-h][1-8]`, ohne echte
  Figurenbuchstaben, Bauernzüge, Striche, Zugnummern), und erst ab 40 im Buch rendert es die betroffenen Seiten
  (`EbookScanDiagramExtractor.RenderPage`, 150 dpi, parallel), schneidet das Zeichen aus (32×32, Flächenmittel wie das
  Brettnetz) und liest es mit `Assets/ebook-figurine-cnn.onnx` (K/Q/R/B/N/„keine Figur“). Nur Lesungen ≥ 0,9 ersetzen
  den Salat durch ♔…♘. Training: `~/claude/ebook-board-training/figfix` (`glyphset.py` → `train_glyph.py`, 122 000
  Zeichen aus 35 Büchern, Labels aus der Prod-Korrektur vom 20.10.); auf 5 zurückgehaltenen Büchern 94–99 % gegen die
  Labels, die Restfehler sind fast alle falsche Labels. Gemessen: Pongó 9 972 ersetzt (31 s), NIC Yearbook 61 35 628,
  Woodpecker (saubere Textebene) 0. Zugfelder mit verlesener Ziffer („dS“) und reine Buchstabenpräfixe bleiben stehen.
- **Diagramme**: an der Stelle jedes `EbookDiagram` mit PDF-Lage (`W > 0`) ein Brettbild aus der FEN
  (`EbookBoardSvg`, cburnett-Figuren aus `Assets/pieces`, App-Farben, je Figur ein `<symbol>`; ~3–8 KB) unter
  `OEBPS/images/p<Seite:D4>-<n>.svg`, Bildunterschrift Nummer + △/▲ (Zugrecht, wenn bekannt). Text im Brett, Koordinaten
  und die Nummer im Rand fallen weg. `EbookDiagram.ImageRef` (= ZIP-Pfad) + `EpubChapter` (Spine-Position, 1 = Titelblatt)
  werden gesetzt → Doppelklick im EPUB-Leser holt die Stellung aufs Brett. Kuratierte Diagramme bleiben, sie bekommen nur
  die Verweise.
- **Nicht im Prototyp**: Bilder und Tabellen des PDF (Tabellen werden zu Textzeilen, Inhaltsverzeichnis-Seiten zu
  Einzelzeilen), Fußnoten, nicht erkannte Diagramme (deren Font-Zeichen fallen einfach weg), Buchstaben-Figurinen
  (SemFig bei Grooten bleibt „Rb1“), eine Stellungskorrektur (`…/position`) zeichnet das Bild erst beim nächsten Lauf neu.
- Gemessen 15.10. (lokal, Dateien nur gelesen): Woodpecker 394 S. → 32 Kapitel, 1 144/1 144 Diagramme als Bild, 2,7 MB,
  17 s; Grooten 544 S. → 52 Kapitel, 995/995, 9 s; Ward „Dragon“ 59 S., 80/80, < 1 s. epub.js (Ordner-Modus wie im Leser)
  zeigt alle drei.


## Bücher auf Englisch übersetzen (0.876.0)

Alle Bücher werden im Hintergrund über das Sprachmodell auf der **eigenen Hardware** (Spark, `TextLlm:BaseUrl`) ins
Englische übersetzt — nie über den Claude-Text-Schlüssel (`IClaudeJsonClient.IsLocal`), und nur außerhalb der
Sperrzeiten der Spark (`TextLlm:QuietHours`, dieselben wie Kurs-Übersetzung und Partietexte: Mo–Do 8–17, Fr 8–14 Wien).
Ergebnis je Buch und Zielsprache ist ein ABGELEITETES EPUB `converted/<id>.<lang>.epub` unter `Ebooks:Root`.

**Figurenbuchstaben** (0.876.1, `EbookNotationLetters`): setzt das Buch landestypische Notation (de/hr/sr S L T D,
nl P L T D, es/it/pt C A T D R, fr C F T D R, cs J S V D, hu H F B V, pl S G W H), stellt das Schreiben des EPUBs sie im
ganzen Kapitel auf Englisch um („Sf3“ → „Nf3“, „e1=D“ → „e1=Q“) — auch reine Zuglisten, die nie ans Modell gehen; nur
Text zwischen Tags, Attribute bleiben. Umgestellt wird nur, wenn landestypische Buchstaben die englischen mindestens
3 : 1 überwiegen (≥ 5 Züge): ein spanisches Buch mit englischer Notation behält sie („R“ = König im Spanischen).

- **Schalter**: `EbookTranslation:Languages` (Compose `EBOOK_TRANSLATION_LANGUAGES`, Komma-Liste, heute `en`), LEER = aus
  (Vorgabe, auch Dev — die Spark ist geteilt). `EbookTranslation:Parallel` (Vorgabe 8) Anfragen je Buch gleichzeitig,
  `ChunkChars` (3000) / `ChunkItems` (40) je Anfrage. Die Obergrenze für ALLE LLM-Zwecke ist `TextLlm:MaxConcurrent`
  (Vorgabe 8) — wer schneller will (vLLM verträgt ≈ 64 Folgen), hebt beide. Pausierbar über „Hintergrundjobs pausieren“.
- **Sprache je Buch** (`Ebook.Language` + `LanguageSource`, `EbookLanguageDetector`): aus dem Text (Probe über das ganze
  Buch, Funktionswörter wie `CommentLanguage` + eigene Liste Kroatisch, Kyrillisch an der Schrift → ru/uk/bg/sr), aus dem
  EPUB bzw. einer Textprobe des PDF (40 Seiten). `dc:language` nur als Hinweis bei knapper Entscheidung. Quelle
  `text`/`manual`; `unclear` (gemischt: zweite Sprache > 45 % der Treffer der ersten, oder < 40 Treffer) und `notext`
  (Scan, DJVU, < 2 000 Zeichen Prosa) werden nicht übersetzt. Erkennung läuft im selben Worker, immer (keine Spark).
- **Reihenfolge** (`EbookTranslationService.ClaimNextAsync`): von Hand vorgezogene zuerst; dann alle Bücher, die weder
  Englisch noch Deutsch sind (nach Id); erst wenn davon keines mehr dran ist UND jedes Buch eine geprüfte Sprache hat, die
  deutschen (`LateSources`: für Ziel `en` = `de`, für ein späteres Ziel `de` = `en`). Bücher in der Zielsprache nie,
  anonyme Uploads nie. Ein wartendes Buch (`NotBefore`) hält die deutsche Runde nicht auf.
- **Quelle** = das EPUB, das der Leser zeigt (`EbookFormats.Primary`, echtes vor Fließtext-EPUB). Nur Text-PDF → erst
  `EbookReflowService.ConvertAsync` (Fließtext-EPUB, wird damit auch das Leseformat des Buchs!); Diagramme noch nicht
  erkannt → 6 h später wieder; Reflow abgelehnt (Scan ohne Textebene) → `NoText`. **Stufe 2** (Scans über die OCR-Ebene)
  ist offen: dafür `LanguageSource = notext` / `State = NoText` wieder aufnehmen.
- **Zerlegen** (`EbookTextSegmenter`, `EbookEpubTranslation`): alle (X)HTML-Dokumente des Manifests (Kapitel, `nav.xhtml`)
  und das NCX; Block-Tags trennen, Inline-Tags werden Platzhalter `<tN>…</tN>` (umschließendes Paar) bzw. `<tN/>`
  (Bild, Umbruch, benachbarte Tags); Hüllen um den ganzen Block bleiben draußen. Schachnotation (SAN in allen
  Figurenbuchstaben-Sprachen, Figurinen, Zugnummern, Langnotation, Rochade, Bewertungszeichen, Ergebnisse, FEN, Figurinen
  im Privatbereich) wird folgenweise — auch über `<span>♖</span>` hinweg — EIN Platzhalter und geht nie ans Modell; Blöcke
  ohne Wörter, PGN-Kopfzeilen und Partieköpfe „Name – Name, Ort 1985“ bleiben unübersetzt. `head`, `script`, `style`,
  `svg`, `pre`, `code` werden nicht angefasst. Figurenbuchstaben werden NICHT umgesetzt (kein `PieceLetters.Convert`).
- **Modell** über `CommentTranslator` (`TranslationSubject.EbookText`, eigener Auftrag: Platzhalter genau einmal
  zurück, Notation/Namen bleiben, ‘…’ als Anführungszeichen; Längen-, Sprach- und Zitatprüfung, zweiter Versuch). Jeder
  verschiedene Text geht nur EINMAL ans Modell. Fehlen Platzhalter oder scheitert eine Fuhre, geht jeder Text allein noch
  einmal; was dann scheitert oder beim Einsetzen die Verschachtelung bräche, bleibt im Original (`SegmentsFailed`).
- **Fortsetzbar**: jede fertige Fuhre landet sofort in `converted/<id>.<lang>.cache.jsonl` (SHA-256 des Modelltexts →
  Übersetzung). Sperrzeit/Pause/Neustart bricht den Lauf ab, die Zeile geht zurück auf `Pending`, der nächste Lauf
  schickt nur, was fehlt. Spark nicht erreichbar → Lauf hält an, nichts wird als gescheitert gebucht.
- **Ausgabe**: Kopie des Quell-EPUB, `mimetype` zuerst unkomprimiert, alle Einträge in gleicher Reihenfolge und
  Schreibweise, Bilder/CSS/Schriften byte-gleich, ids/Anker unverändert (Diagramm-`ImageRef`/`EpubChapter` gelten
  weiter), `lang`/`xml:lang` am `<html>` und `dc:language` auf die Zielsprache, Text als UTF-8.
- **Veraltet**: `SourceHash` (SHA-256 über OPF + Text-Dokumente) und `SourceFileId`; ein neues Fließtext-EPUB stellt
  fertige Übersetzungen sofort zurück, sonst prüft der Worker im Leerlauf je Durchgang 3 fertige gegen ihre Quelle. Dank
  Zwischenspeicher kostet das Erneuern nur den geänderten Text.
- **Löschen** des Buchs entfernt Zeilen, EPUB und Zwischenspeicher (`EbookStorage.DeleteTranslations`).
- Tabelle `EbookTranslations` (je Buch + Sprache eindeutig): `State` Pending/Running/Done/Failed/NoText, `Priority`,
  `NotBefore`, `SourceFileId`, `SourceHash`, `RelativePath`, `SizeBytes`, `SegmentsTotal/Done/Failed`, `Model`, `Error`,
  `Runs`, Zeitstempel. Bewusst KEINE `EbookFile`-Zeile — an „epub + Derived“ hängen Leseformat, Reflow,
  Diagramm-Verknüpfung und Fingerabdrücke.

| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/ebooks/{id:int}/translations/{lang}/epub/{token}/{**path}` | **AllowAnonymous** (Ausweis im Pfad oder Bearer), Rate-Limit `ebook-entries` | Ein Eintrag des übersetzten EPUB (Leser „als Ordner“), Sichtbarkeit wie `…/epub`; keine fertige Übersetzung → 404 |
| GET | `/api/ebooks/{id:int}/translations/{lang}/file?t=&download=` | **AllowAnonymous** (Ausweis oder Bearer) | Das übersetzte EPUB als Datei („<Titel> (en).epub“) |
| GET | `/api/admin/ebooks/translations` | `ebooks.manage` | Übersicht: eingeschaltet, Modell, Sperrzeit bis, laufendes Buch mit Fortschritt + Restdauer (Tempo dieses Laufs), Bücher je Sprache (`und`/`notext`/`pending`), je Ziel fertig/wartend/gescheitert/ohne Text/noch offen (davon erste Runde), die nächsten 10, die letzten 20 gescheiterten |
| POST | `/api/admin/ebooks/{id:int}/translations/{lang}` | `ebooks.manage` | Buch vorziehen bzw. neu übersetzen (Vorrang, weckt den Worker); Buch schon in der Sprache → 400, unbekannt → 404 |
| POST | `/api/admin/ebooks/translations/retry-failed` | `ebooks.manage` | Gescheiterte und unvollständige (`SegmentsFailed > 0`) wieder einreihen → `{ queued }` |

`EbookDto` trägt `language` und `translations` (fertige Zielsprachen). **Oberfläche**: im Leser ein Sprachschalter
(Symbol `translate`, Menü „Original“ / „Englisch“, aktiv in Primärfarbe), nur mit fertiger Übersetzung; die Wahl gilt je
Gerät (`rh.ebooks.readLang`) für jedes Buch mit dieser Übersetzung und öffnet immer das EPUB. Beim Wechsel baut der
EPUB-Leser neu auf und springt an dieselbe Stelle (CFI ohne Zeichenversatz, `coarseCfi`); der Lesestand ist einer für
beide Sprachen. Regal: Abzeichen mit `translate`-Symbol + „EN“ und Tooltip; Verwalter: im ⋮ der Bibliothek „Buch-Übersetzungen“ (Übersicht
als Dialog, „Gescheiterte wiederholen“), im ⋮ der Karte „Als Nächstes ins Englische übersetzen“.

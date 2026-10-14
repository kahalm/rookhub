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
gelöscht).

**Titelbilder** rendert der `EbookCoverWorker` (alle 20 s, 4 Bücher je Durchlauf) nachträglich: PDF über `pdftoppm` (Seite 1,
480 px, dazu die Seitenzahl aus `pdfinfo`), DJVU über `ddjvu` → Ein-Seiten-PDF → `pdftoppm`, EPUB/CBZ als Bild aus dem Zip
(OPF-Cover bzw. erstes Bild). Werkzeuge liegen im API-Image (`poppler-utils`, `djvulibre-bin`); fehlen sie, bleibt das Buch
auf `CoverState = Failed` (kein weiterer Versuch) und die Karte zeigt den Titel statt eines Bilds.

**Datei-Endpunkte sind `[AllowAnonymous]` mit Ausweis**: `<img>`, Download-Links und der PDF-Leser (Bereichsanfragen) können
keinen Bearer schicken. `GET /api/ebooks/access-token` gibt einen 12-h-Ausweis (`EbookAccessTokens`: DataProtection-
versiegelte Nutzer-Id), der als `?t=` mitgeht; jede Anfrage prüft die Sichtbarkeit des Buchs für genau diesen Nutzer gegen
die Datenbank (Admin-Flag ebenfalls aus der DB, nicht aus dem Ausweis). Ohne Ausweis UND ohne Bearer: 401. Eingetragen in
`EndpointAuthInventoryTests.ExpectedAnonymous`.

| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/ebooks?search=&tag=&mine=&withDiagrams=&sort=&page=&pageSize=` | Auth | Sichtbare Bücher, eine Seite (`pageSize` ≤ 100, Vorgabe 24) + Rahmen: `tags[]` (Schlagwort-Facette über ALLE sichtbaren, unabhängig vom Filter), `uploadEnabled`, `quotaUsedBytes`/`quotaLimitBytes` (null = unbegrenzt), `maxFileBytes`, `canManageAll`, `canImportLibrary`. `sort`: `title` (Vorgabe) / `newest` / `recent` (zuletzt gelesen zuerst). `tag` trifft nur ganze Schlagworte. Je Buch `canManage`, `groupIds` (nur wenn verwaltbar), `files[]`, `primaryFormat` (PDF vor allem anderen), `progress` (Lesestand des Aufrufers), `hasCover` |
| GET | `/api/ebooks/groups` | Auth | Gruppen, die der Aufrufer freigeben darf: Verwalter alle, sonst die eigenen + „Everyone" (`isEveryone`). Literal vor `{id}` |
| GET | `/api/ebooks/access-token` | Auth | `{ token, expiresAt }` — der Ausweis für `…/file` und `…/cover` |
| POST | `/api/ebooks` | Auth | Hochladen: Multipart `file` + `title?` (sonst aus dem Dateinamen), `author?`, `year?`, `tags?` (CSV), `description?`, `targetMinElo?`, `groupIds?` (CSV) → 201 + Buch. 400 `ebook_format_unsupported` / `ebook_file_too_large` / `ebook_file_empty` / `ebook_quota_exceeded` / `ebooks_not_configured`, 409 `ebook_duplicate_file`. Gruppen außerhalb der erlaubten werden still verworfen. Gesperrt in der Impersonation |
| PUT | `/api/ebooks/groups` | Auth | Freigabe vieler Bücher auf einmal `{ ebookIds[], groupIds[] }` (ersetzt die bisherige; nicht verwaltbare Bücher werden übersprungen) → `{ updated }`. Literal vor `{id}` |
| GET | `/api/ebooks/{id:int}` | Auth | Ein Buch (unsichtbar → 404) |
| PUT | `/api/ebooks/{id:int}` | Besitzer/Verwalter | Metadaten + Freigabe; nur gesetzte Felder wirken (`year: 0` = keine Angabe, `groupIds: []` = nur ich). Leerer Titel → 400; fremd → 403; unsichtbar → 404 |
| DELETE | `/api/ebooks/{id:int}` | Besitzer/Verwalter | Löscht Buch, Freigaben, Lesestände und die HOCHGELADENE Datei + Titelbild (Bibliotheksdateien bleiben) → 204 |
| GET/PUT | `/api/ebooks/{id:int}/progress` | Auth | Lesestand `{ page, pageCount? }` (1-basiert; `pageCount` bleibt, wenn keiner mitkommt). Unsichtbar → 404 |
| GET | `/api/ebooks/{id:int}/file?t=&fileId=&download=` | **AllowAnonymous** (Ausweis oder Bearer) | Die Hauptdatei bzw. `fileId`, mit Bereichsanfragen (`PhysicalFile(enableRangeProcessing)`) — der PDF-Leser holt nur die Seiten, die er zeigt. `download=true` → Anhang „<Titel>.<format>“ (Dateisystem-sichere Zeichen). Datei auf der Platte weg → 404 `ebook_file_missing` |
| GET | `/api/ebooks/{id:int}/cover?t=` | **AllowAnonymous** (Ausweis oder Bearer) | Titelbild (JPEG/PNG, Typ an den Bytes erkannt) oder 404, solange keines da ist; `Cache-Control: private, max-age=86400` |
| GET | `/api/admin/ebooks/library` | `ebooks.manage` | `{ configured, uploadsEnabled, imported, coversPending }` |
| POST | `/api/admin/ebooks/library/import` | `ebooks.manage` | Bibliothek einlesen (synchron, Sekunden): gleichnamige Dateien eines Ordners (`Buch.pdf` + `Buch.djvu`) werden EIN Buch; Beiblatt `Buch.json` (`title`, `author` als Text oder Liste, `year`, `tags[]`, `targetMinElo`) liefert die Metadaten, sonst Ordner „Nachname, Vorname" + Dateiname (Ordner `AAA…`, `Unknown`, `.…` gelten nicht als Autor). Idempotent: bekannte Pfade werden übersprungen, ein neues Format neben einem bekannten Buch angehängt. Eingelesene Bücher gehören dem Admin und sind zunächst NUR für Verwalter sichtbar — Freigabe danach in der Übersicht (Auswahl → „Sichtbarkeit setzen"). Antwort `{ configured, filesSeen, added, filesAdded, skipped, unreadable }` |

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

## Diagramm-Erkennung (Stufe 1, 0.766.0)

Digital gesetzte Bücher zeichnen Diagramme als TEXT in einem Schach-Font — jedes Feld ein Buchstabe. `EbookDiagramWorker`
(alle 30 s, 2 Bücher je Durchlauf) liest jedes PDF mit **PdfPig** (`EbookDiagramExtractor`): Buchstaben eines bekannten
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
| POST / DELETE | `/api/ebooks/{id:int}/diagrams/{diagramId:int}/report` | Auth | „Diagramm falsch“ melden (`{ note? }`, idempotent) bzw. eigene Meldung zurücknehmen → `{ reported }`. Tabelle `EbookDiagramReports` (Seite + FEN mitgeschrieben, `DiagramId` ohne FK — überlebt eine Neuerkennung, die die Diagramm-Zeilen ersetzt; unique je Nutzer + Diagramm). Im Diagramm-DTO `reportedByMe`, `reportCount` (nur Verwalter) |
| GET | `/api/admin/ebooks/diagram-reports` | `ebooks.manage` | Meldungen, neueste zuerst (≤ 500) mit Buchtitel, Seite, FEN, Font-Familie, Hinweis, Nutzer, `diagramExists` |

**Leser**: hat das Buch Diagramme, wird er zweispaltig — Buch links, Brett rechts (`clamp(300px, 32vw, 460px)`). Über jedem
Diagramm liegt eine Zone (`PdfViewerComponent.zones`, Prozent der Seite → zoomfest); Doppelklick (oder Fokus + Enter) holt die
Stellung aufs Brett, Schwarz am Zug dreht es. ‹/› gehen alle Diagramme durch und scrollen das Buch mit, „Analysieren“ öffnet
`/analysis?fen=…&from=/ebooks/:id`, FEN kopieren. Unter 900 px liegt das Brett als Leiste unten und erscheint erst nach dem
ersten Doppelklick.

**Übersicht merkt sich die Ansicht** (0.768.0): Suche, Schlagwort, Sortierung, „Meine Uploads“, „Mit Diagrammen“ und Seite
stehen in der Adresse (`?q=&tag=&sort=&mine=1&diagrams=1&page=`, `replaceUrl`) und in der Sitzung (`rh.ebooks.listState`) —
der Zurück-Pfeil im Leser führt auf das nackte `/ebooks` und landet trotzdem in derselben Ansicht.

**Brett spielbar** (0.769.0): Hand-Knopf neben dem Brett schaltet `playable` ein; gespielte Züge (SAN + FEN) stehen unter dem
Brett mit Zugnummern ab der Buchstellung, Zurücknehmen / Buchstellung, „Analysieren“ und „FEN kopieren“ nehmen die aktuelle
Stellung. Regelwidrige Buchstellungen (chess.js lehnt ab, z. B. Studie ohne König) bleiben nur zum Ansehen.

## EPUB-Leser, DJVU → PDF, Trenner (0.770.0)

- **EPUB** (`EpubViewerComponent`, epub.js 0.3.93 als Lazy-Chunk): Datei als ArrayBuffer (die Datei-Adresse hat keine Endung),
  paginiert, ‹ › / Pfeiltasten / Wischen, Inhaltsverzeichnis im Kopf. Lesestand: `EbookProgress.Locator` = CFI, `Page` =
  Prozent (1–100) bei `PageCount` 100 — so funktioniert der Fortschrittsbalken der Karten unverändert. Die CSP des
  Frontend-nginx erlaubt dafür `blob:` bei `style-src` und `font-src` (epub.js reicht Buch-CSS und -Schriften als blob: ins
  iframe; vorher: Buch ungestaltet, CSP-Fehler). Buch-Skripte bleiben aus (epub.js-Vorgabe).
- **DJVU → PDF** (`EbookDjvuConverter`, alle 30 s ein Buch, `ddjvu -format=pdf`, Deckel 30 min): Bücher NUR mit DJVU
  (`EbookFormats.NeedsPdfConversion`) bekommen `converted/<id>.pdf` unter `Ebooks:Root` als `EbookFile` mit `Derived = true`
  (liegt immer unter der beschreibbaren Wurzel, wird mit dem Buch gelöscht; `EbookStorage.ResolveFile` wählt die Wurzel).
  Stand am Buch `ConversionState` (NotNeeded/Pending/Done/Failed); Upload/Import/Migration setzen Pending. Gemessen: 227
  Seiten in 13 s, ~6× die DJVU-Größe (Bibliothek: 66 Bücher nur als DJVU). Danach Titelbild + Diagramme neu vorgemerkt.
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

**Fertig-Meldung** (0.774.0): `EbookProcessing.IsProcessing` (Titelbild, DJVU→PDF, Erkennung, Stockfish-Prüfung — letztere nur
mit Diagrammen und eingeschalteter Prüfung) → DTO `processing` (Karte „Wird verarbeitet …“). Jeder Worker ruft nach seinem
Schritt `NotifyIfReadyAsync`: bei Uploads (nicht bei Bibliotheksimporten) genau EINE Glocken-Meldung `ebook_ready`
(`title`, `diagrams`, Link `/ebooks/{id}`), Merker `Ebook.ReadyNotifiedAt`. Push-Kategorie „other“.

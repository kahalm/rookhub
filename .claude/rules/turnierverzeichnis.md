---
paths:
  - "**/*TournamentDirectory*"
  - "**/*DirectorySweep*"
  - "**/*DirectorySource*"
  - "**/Directory*.cs"
  - "**/*Gazetteer*"
  - "**/Geo*.cs"
  - "**/TournamentCalendarSweepService.cs"
  - "**/FederationCatalog.cs"
  - "**/Services/TournamentDirectory/**"
---

# Turnierverzeichnis: Ortsnamen, Quellen, Fehlschläge

> Lädt automatisch, sobald eine passende Datei gelesen/bearbeitet wird. Ausgelagert aus `CLAUDE.md` „Wichtige Konventionen“ (2026-10-11).

- **Eine gleichnamige REGION entscheidet im Ortsnamen-Weg nicht mit** (seit 0.456.5) – Steht in derselben
  Namensgruppe auch nur ein Ort, fallen die Regionszeilen (`GeoPlaceKind.Region`) vor der Wahl heraus. Eine Region
  ist die MITTE eines Gebiets und liegt zwangslaeufig woanders als die Stadt, nach der es benannt ist — die Gruppe
  streute damit weiter als eine Stadt, die Turnierdichte entschied nicht, und es gab KEINEN Pin. Gemessen am
  2026-09-10: 63 der 100 weissrussischen Eintraege mit kyrillischem Ortstext standen auf „mehrdeutig" („Витебск":
  Stadt und PLZ-Zeile auf demselben Punkt, Oblast-Mitte 78 km weg; „Гродно" 90 km), und **639 Regionszeilen teilen
  ihren Namen mit einem Ort desselben Landes** — Tuerkei 71, Thailand 67, Aserbaidschan 65, Algerien 41, Kenia 35.
  Der Fall ist also weltweit und nicht kyrillisch. Sichtbar wurde er erst durch die Umschrift (0.456.0): vorher
  trugen kyrillische Regionszeilen einen leeren Suchnamen. Der Rueckfall auf die Regionsmitte bleibt (Feld `state`
  ueber `ResolveByRegionAsync`, und Gruppen, die NUR aus Regionen bestehen).

  **Diese Regel allein loest GROSSSTAEDTE nicht** — dort ist die Ursache eine andere: 182 Postleitzahl-Zeilen
  namens „Berlin" streuen ueber 24 km, also weiter als die 5-km-Schwelle `SameTownKm`, obwohl es EINE Stadt ist.
  Die Spannweite kann „eine Stadt mit vielen Stadtteilen" nicht von „mehrere gleichnamige Orte" unterscheiden
  („Muenster": 17 Zeilen ueber 301 km). Der Zusammenhang kann es: nach Single-Linkage gemessen (zweite Instanz,
  2026-09-10) fallen Berlin, Hamburg, Muenchen, Koeln, Bremen, Dresden und Wien **ab 8 km Lueckenschwelle** in je
  EINEN Haufen und bleiben das bis 15 km, Muenster dagegen in drei. Die beiden Regeln muessen dabei in dieser
  Reihenfolge komponieren: erst die Regionszeile heraus, dann haeufen — bei „Витебск" reisst die 78 km entfernte
  Oblast-Mitte sonst jeden Haufen auf. Steht als TODO, absichtlich noch nicht gebaut.
- **Ein Ortsname steht im Lexikon in ZWEI Schreibweisen** (seit 0.456.0) – `GeoPlace.NameNormalized` faltet
  Umlaute auf den Grundvokal (`München` -> `munchen`), `GeoPlace.NameTranscribed` haelt die ASCII-UMSCHRIFT
  (`muenchen`) und schreibt nichtlateinische Schriften um (`Київ` -> `kyiv`, `Αθήνα` -> `athina`). Gesucht wird in
  BEIDEN Spalten mit BEIDEN Textformen — Ortsnamen-Suche, Postleitzahl-Bestaetigung, Regionen. Zwei Gruende: (1)
  chess-results schreibt regelmaessig „Muenchen", und das traf `munchen` nie (53 unverortete deutsche Eintraege);
  (2) der Aufraeumteil der Normalisierung verwirft alles ausser `[a-z0-9]`, Kyrillisch fiel damit RESTLOS weg — 29 547
  von 29 571 ukrainischen PLZ-Zeilen trugen einen leeren Namen, und die Bestaetigung des PLZ-Wegs konnte dort nie
  gelingen. **Beim SUCHEN wird NICHT gefaltet** (`ue -> u` machte aus „Quedlinburg" ein `qudlinburg`) — die zweite Form
  entsteht beim IMPORT. **Die Umschrift haengt am LAND** (`NormalizeTranscribed(text, iso2)`): `и` ist ukrainisch
  ein `y` („Київ" -> `kyiv`), russisch ein `i` („Истра" -> `istra`), und GeoNames haelt es genauso — mit einer
  Tabelle fuer beide findet in einem der Laender KEIN Text seinen Ort (Russland: 2 003 Turniere, 7 % verortet, die
  groesste einzelne Luecke). Beide Seiten waehlen dieselbe Tabelle: der Lexikon-Eintrag ueber `GeoPlace.Country`,
  der Suchtext ueber das Land des Turniers. Ein Name, von dem nur eine ZAHL bleibt (die russischen PLZ-Zeilen
  heissen teils „Москва 194", in der ersten Form bleibt `194`), bestaetigt NICHTS — sonst bestaetigte er im Text
  eine Hausnummer. Nach einem Deploy einmal
  `POST /api/admin/tournament-directory/gazetteer/transcribe` laufen lassen, sonst ist die Spalte fuer den
  Altbestand leer. Ein Treffer ohne vergleichbaren Namen in BEIDEN Formen (Georgisch, Armenisch, Hebraeisch)
  bestaetigt sich ueber die LAENGE der Ziffernfolge; vorher war das stillschweigend ein Nein.
- **Verschwundene Turniere: NUR die Quelle, die ein Turnier fuehrt, darf es zurueckziehen** (seit 0.454.1) –
  Die Turniersuche zaehlt `MissedSweeps` und meldet ab dem zweiten Fehlschlag „abgesagt" (mit Benachrichtigung),
  **beschraenkt auf Eintraege MIT chess-results-Nummer**: die 15 Verbandskalender und der FIDE-Kalender fuehren
  Turniere, die dort per Definition nicht vorkommen, und sammelten sonst jede Nacht einen Fehlschlag. Am 2026-09-09
  live passiert (ein schachbund-Turnier stand nach zwei GER-Sweeps auf abgesagt, obwohl es stattfindet).
  Die Zusatzquellen ziehen seit 0.454.1 selbst zurueck (`ExternalDirectorySource.RetireVanishedAsync`) — mit vier
  Schranken: nur kuenftige Eintraege, nur ohne chess-results-Nummer, nur wenn diese Quelle der EINZIGE
  Herkunftsvermerk ist, **nur bis zum HORIZONT des Laufs** (dem spaetesten wiedergesehenen Termin — eine Quelle mit
  kurzem Vorschau-Fenster raeumt sonst alles dahinter ab), und **gar nicht**, wenn ein Lauf weniger als
  `MinSeenPercent` = 90 % seiner eigenen Kandidaten wiederbringt (dieselbe Lehre wie die MaxRows-Bremse: eine
  systematische Luecke wiederholt sich jede Nacht, die Karenz faengt sie NICHT ab). Die 90 % sind seit 0.455.0 am
  Anteil der wiedergesehenen KANDIDATEN gemessen; die erste Fassung verglich die Zahl gelieferter ZEILEN mit der Zahl
  der Kandidaten und liess alles ab der Haelfte gelten — bei Polen (620 Kandidaten, ein gewoehnlicher Tag kostet fuenf)
  waeren das 310 zugelassene Falschabsagen. Der chess-results-ANKUENDIGUNGSkalender zieht bewusst nichts zurueck — nur
  ~70 % seiner Zeilen tragen eine Kennung, und ohne stabile Kennung ist „fehlt" nicht von „umbenannt" zu unterscheiden.
  Der FIDE-Kalender zieht seit 0.455.1 ebenfalls zurueck (seine Kennungen sind stabile Ereignisnummern); dort traegt
  der Horizont besonders viel, weil ein Durchgang oft nur das laufende Jahr abfragt und ueber das naechste nichts
  sagen darf. **Spricht die Bremse an, steht das als Warnung im Log** (`RetireVanishedAsync` nimmt dafuer den Logger
  der Quelle) — ohne den Eintrag ist eine dauerhaft halb liefernde Quelle nicht von einer zu unterscheiden, bei der
  nichts verschwindet: beide ziehen nie etwas zurueck.
- **`MissedSweeps` zaehlt NAECHTE, nicht Laeufe** (seit 0.455.0) – Ein Fehlschlag wird nur gezaehlt, wenn der letzte
  laenger als `ExternalDirectorySource.MissCooldown` (20 h) zurueckliegt; `TournamentDirectoryEntry.LastMissAt` haelt
  ihn fest. Gilt fuer BEIDE Besitzer (Turniersuche und Zusatzquellen). Ohne die Sperre genuegten zwei Durchgaenge im
  Abstand von Minuten: der Aufhol-Lauf nach einem Deploy (auf Dev mehrmals am Tag) und `scripts/directory-runs.sh` mit
  bis zu elf Durchgaengen haetten abgesagt, was eine Quelle kurz nicht auswies.
- **Die Abgleichschleife der Zusatzquellen steht EINMAL, in `ExternalSweepRunner`** (Zerlegung Z7, 2026-10-25) – Eine
  Quelle implementiert `IDirectorySourceAdapter<TRow>` (Kennung, Termin, Name, `PublicId`, Foederation, Abbildung auf den
  eigenen Eintrag, Abschlusszeile) und ruft `ExternalSweepRunner.RunAsync`; Sonderfaelle sind Haken mit Vorgabe
  (`Withdrawn` = Absage/Sitzung, `FillMatch` = Luecken am chess-results-Eintrag, `SaveEvery`, `BeforeLoopAsync`,
  `unreadable` = beim Abruf unlesbare Kennungen, `BeforeMatchAsync` = Zeile VOR der Zuordnung anreichern und am
  Zeilenobjekt ablegen (ICU-Detailseite, `IcuSweepRow`), `ChessResultsId` = exakter Treffer ueber die Nummer der
  Quelle vor dem Namensvergleich (ein Treffer auf den EIGENEN Eintrag zaehlt nicht, er nimmt `ApplyAsync`), `OnCreated` = Felder nur fuer einen neuen Eintrag, `AfterLookupAsync` = nach
  beiden Suchen). Detailseiten mit Budget/Fassung gehoeren in `ApplyAsync`, nicht in einen neuen Haken. „Geliefert vor lesbar", Zwischenstand, Speichern im `finally` und
  `RetireVanishedAsync` gelten damit fuer alle umgestellten Quellen an EINER Stelle. Die Log-Vorlagen beginnen mit
  `LogName` wie vorher von Hand. Jede Quelle hat einen Golden-Test (`ExternalSweepGoldenTests`, acht Laeufe); wer
  den Runner oder einen Adapter aendert, sieht dort jede Abweichung (ICU, chess.sk, chessarbiter und FFE zusaetzlich
  mit Sonderfall-Laeufen 9 ff.). Alle fuenfzehn Verbandskalender laufen darueber; ChessCz, FIDE und der
  Ankuendigungskalender haben eine andere Schleife.
- **Eine Zeile, die wir nicht lesen koennen, gilt als GELIEFERT** (seit 0.455.0) – In allen 15 Quellen steht
  `delivered.Add(...)` VOR der Pruefung auf Termin und Namen, und die Abruf-Methoden sieben Zeilen ohne lesbaren Termin
  nicht mehr aus (KNSB traegt einen festen Termin im Zeilentyp und meldet sie ueber `KnsbFetch.Unreadable`). Vorher
  sammelte eine solche Zeile Fehlschlaege und war nach zwei Naechten abgesagt — und ein geaendertes Datumsformat
  trifft nicht eine Zeile, sondern alle: genau der systematische Fall, den die Karenz nicht abfaengt.
- **Zusammenfuehren braucht mehr als zwei gemeinsame Woerter** (seit 0.455.0) – `ExternalDirectorySource.FindMatchAsync`
  (und der FIDE- sowie der Ankuendigungs-Abgleich) pruefen zusaetzlich: (1) **Fuellwoerter aus der Worthaeufigkeit der
  Foederation** (`CorpusFillerAsync`, ab 8 % der Namen; die feste `NameFiller`-Liste ist englisch und deutsch und
  liess „torneo", „scacchi", „turniej", „szach" durch), (2) **die Ortsangaben duerfen sich nicht widersprechen**
  (`PlacesAgree` — nennt eine Seite keinen Ort, wird nicht widersprochen), (3) **kein zweiter Vermerk derselben
  Quelle** am selben Eintrag (`HasOtherNoteOfSameKind`), (4) **die BEDENKZEIT-Klassen in den Namen duerfen sich
  nicht widersprechen** (`SpeedsAgree`; nennt eine Seite keine, entscheidet weiter der Wortvergleich). Punkt 4 kam
  aus dem ersten echten Lauf: in der Nacht zum 2026-09-10 liefen alle DREI tschechischen Zeilen des „UCT Chess
  Festival 09/2026" auf den Rapid-Eintrag, obwohl Blitz, Rapid und Standard je einen eigenen
  chess-results-Eintrag haben (1474369/1474368/1474370, gleicher Termin, gleicher Name bis auf das
  Bedenkzeit-Wort) — „blitz", „rapid" und „standard" stehen in `NameFiller`, der Abgleich hatte dort also gar
  keinen Unterscheider. Verglichen werden die NAMEN und nicht `entry.Speed`, damit der Vergleich symmetrisch
  bleibt (die Quellzeile hat kein solches Feld). Anlass: am 2026-09-09 waren auf Dev drei echte italienische
  Turniere (Cormòns, Frascati, Bellante, alle 20.09.) einem „Torneo Sociale Arci Scacchi Bolzano B" vom 21.09.
  zugeschlagen und als „geht darin auf" zurueckgezogen — sie waren im Verzeichnis nicht mehr zu finden. Ein
  Vereinsturnier ueber fuenf Wochen liegt im Termin-Fenster von jedem Wochenendturnier des Landes. **Falsches
  Zusammenfuehren ist der stillste Weg, auf dem ein Turnier verschwindet**, denn der eigene Eintrag traegt danach
  keinen Herkunftsvermerk mehr und keine Verschwunden-Erkennung sieht ihn.
- **Ein Herkunftsvermerk WANDERT, er wird nicht doppelt angelegt** (seit 0.453.13) – Der eindeutige Index liegt auf
  (`Kind`, `ExternalId`) und gilt ueber den GANZEN Bestand. `ExternalDirectorySource.NoteSourceAsync` sieht deshalb
  nicht nur die Vermerke des uebergebenen Eintrags, sondern fragt die Tabelle: haengt die Kennung woanders, wird der
  Vermerk UMGEHAENGT (ueber die Navigation, damit es auch bei einem noch nicht gespeicherten Eintrag geht). Am
  2026-09-09 starben daran drei Quellen gleichzeitig (Ungarn, Tschechien, Ankuendigungskalender), ausgeloest von 400
  neuen Eintraegen, die die Namens-/Terminvergleiche auf andere Eintraege verschoben haben. Und: deckt eine
  Quellen-Kennung mehrere Eintraege ab (die tschechische „2. ligy" sind die Gruppen A bis F), MUSS der Schluessel den
  Eintrag enthalten — `ChessCzDirectorySweepService.SeriesKey(series, publicId)`. Es gab DREI Fassungen dieser Einfuege-Logik
  (der gemeinsame Helfer, eine im Ankuendigungskalender, eine als `TournamentDirectoryService.NoteSource`) — seit
  0.454.1 laufen alle ueber den Helfer.
- **Eine fremde Kennung wird NIE gekuerzt** (seit 0.453.11) – `TournamentDirectorySource.ExternalId` ist 60 Zeichen
  lang und ein SCHLUESSEL. Wer laenger liefert, bekommt von `ExternalDirectorySource.NoteSource` eine
  `ArgumentException` mit Klartext und vermerkt stattdessen einen KURZSCHLUESSEL (Hash der Quellen-Kennung, siehe
  `WcuDirectorySweepService.PublicIdOf`). Abschneiden koennte zwei Turniere verschmelzen; zentral zu HASHEN waere
  noch schlimmer, weil die Quellen ihre Vermerke ueber die ROHE Kennung wiederfinden (`s.ExternalId == slug`) und
  damit jede Nacht in den eindeutigen Index liefen. Wales und Deutschland fuehren keine Turniernummer und
  scheiterten daran monatelang jede Nacht — Deutschland erst nach 283 s hoeflichen Crawlens, das den ganzen
  Durchgang wegwarf; die Meldung stand nur als innere Ausnahme eines `DbUpdateException` im Log.

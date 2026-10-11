# Anmerkungen in mehreren Sprachen (`CommentSets`)

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Anmerkungen in mehreren Sprachen (`CommentSets`)“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.


Die Sammlungen liefern ihre Anmerkungen oft ZWEISPRACHIG — und das PGN kann das nicht ausdruecken:
gemessen am 2026-09-11 ueber alle 130 572 Zeilen des Rohbestands traegt **keine einzige** einen
`[%lang`-Marker. ChessBase haengt die Sprachen beim Export schlicht aneinander, erst der englische
Absatz, direkt dahinter der deutsche, in EINEM `{}`-Block.

**Abgelegt wird getrennt vom PGN** (`CommentSets` + `CommentTexts`), und das ist keine
Bequemlichkeit: das PGN ist die QUELLE und traegt Herkunft und Ausgabe einer gekauften Sammlung
(`SourceTitle`, `SourceVersion`). Wer eine Uebersetzung hineinschreibt, kann Quelle und Zutat nie
wieder auseinanderhalten, und ein erneutes Einlesen der Datei wuerde sie verwerfen.

**Ein SATZ je Sprache, nicht Zeilen mit Sprachspalte**: die Herkunft (aus der Quelle gelesen?
maschinell uebersetzt? mit welchem Modell?) gehoert EINMAL je Sprache hin, ein Uebersetzungslauf
schreibt einen Satz am Stueck, und „Sprache umschalten" ist eine Abfrage statt eines Filters ueber
Zeilen verschiedener Herkunft. **Der Anker ist die BIBLIOTHEKSZEILE**, wo es eine gibt — fordern
zwei Leute dieselbe Partie an, entstehen zwei Analysen, und eine Uebersetzung je Analyse waere
dieselbe Arbeit zweimal bezahlt; eine selbst eingeworfene Partie haengt an der Analyse.

**Die FIGURENZEICHEN werden aufgeloest** (`Services/Figurines.cs`). Im PGN stehen die Figuren
INNERHALB der Kommentare nicht als Buchstaben, sondern als Codepunkte der ChessBase-Figurenschrift
(U+E024 bis U+E029, privater Unicode-Bereich). Ohne diese Schrift sind sie unsichtbar, und der
Kommentar liest sich als „I can't win the pawn due to the h7+ trick" — gemeint ist `Bh7+`. Am
Bestand gemessen (2026-09-11): **45 von 101** Partien einer Stichprobe tragen solche Zeichen, und
der Nachtrag hat 737 von 6091 gespeicherten Zeilen angefasst. Die Zuordnung ist am Text BELEGT
(Koenig „I would prefer …b1", Dame „...…a5", Turm „…dg1!?", Laeufer „…h7+ trick", Springer
„...…c6", Bauer „Black's … structure") und haengt an der SPRACHE des Satzes: derselbe Springer
heisst englisch N und deutsch S. Aufgeloest wird deshalb beim Ablegen, wo die Sprache feststeht;
`comments --figurines` holt den vorhandenen Bestand nach. Der Bauer bekommt als einziger ein WORT
statt eines Buchstabens — in der Notation traegt er keinen.

**Getrennt wird satzweise** (`Services/CommentSplit.cs`), mit zwei Signalen: den Funktionswoertern
je Sprache (laengere Listen als bei `CommentLanguage` — dort wird eine ganze PARTIE eingeordnet,
hier ein einzelner SATZ) und den **Figurenbuchstaben**, `Be3/Ng4` gegen `Le3/Sg4`. Gezaehlt werden
nur Buchstaben, die in genau EINER der beiden Sprachen vorkommen (bei `fr/nl` sagt ein `D` nichts,
ein `C` und ein `P` schon).

Drei Entscheidungen, die dabei tragen:
* **Ein Block hat nicht zwei Haelften, sondern beliebig viele Abschnitte.** Sobald der Kommentator
  eine Nebenvariante einschiebt (die beim Einlesen an den Kommentar angehaengt wird), steht dort
  en-de-en-de. Zugeordnet wird deshalb satzweise mit einem Wechselaufschlag (ein gewoehnliches
  Viterbi ueber zwei Zustaende). Gemessen an 875 langen Bloecken: **55 % getrennt mit einem
  einzigen Schnitt, 92 % satzweise.**
* **Die Satzgrenze ist grosszuegig** (Doppelpunkt zaehlt, der naechste Buchstabe darf klein sein) —
  aber ein `.` nach einer ZIFFER ist keine: „8. Ng4" ist eine Zugnummer. Ein Zug am Satzende
  („…well met by Ng4.") wird dagegen erkannt, weil ein Zug auf einem FELD endet.
  Zu fein zu trennen kostet nichts (die Zuordnung legt Nachbarn derselben Sprache wieder zusammen),
  ein verpasstes Satzende kettet zwei Sprachen dagegen fuer immer aneinander.
* **Im Zweifel wird nicht geschnitten.** Reichen die Belege nicht oder widersprechen sie sich,
  bleibt der Block ganz und zaehlt zur ersten Sprache. Ein halbierter Satz ist schlimmer als ein
  zweisprachiger Block.

**Zweisprachig, aber als einsprachig vermerkt** (0.560.3, `CommentSetService.ResplitLibraryAsync`,
`tools/LibraryImport resplit [--dry-run] [--limit n] [--game id]`). Getrennt wird nur, wenn `Languages` ZWEI
Sprachen nennt — und `CommentLanguage.Detect` nimmt die zweite erst ab 60 % der Treffer der ersten, gezaehlt in
den ersten 4000 Zeichen. Eine ChessBase-Partie, deren Kommentar vorn fast nur Englisch traegt (Zugnummern,
Varianten) und den deutschen Teil erst spaeter, stand damit als `en` da: EIN gemischter Quell-Satz, und die
deutsche Uebersetzung warf die deutsche Haelfte zu Recht weg — und scheiterte an der Laengenpruefung („zu wenig
Text", am 2026-09-27 an Partie 108403 nachgestellt: 46 % deutsche Treffer vorn, 67 % ueber den ganzen Text).
Gemessen an 3 495 englischen Quell-Saetzen auf Prod: einsprachig Englisch liegt fast immer unter 5 %, zweisprachig
zwischen 10 und 100 %. `resplit` zaehlt deshalb ueber den GANZEN Kommentar (`CommentLanguage.Rank`) und nimmt die
zweite Sprache schon ab 20 % (`ResplitMinSecondShare`), verlangt aber, dass die Satz-Trennung sie auch TRAEGT: mindestens
zwei Halbzuege und mindestens halb so viele wie die erste (`ResplitMinCoverage`). Ohne diese zweite Schranke bekaeme
ein englischer Kommentar mit EINEM deutschen Zitat einen winzigen deutschen Quell-Satz — und der sperrte die Partie fuer
die deutsche Uebersetzung. Umgebaut wird nur ohne von Hand gepflegte Fassung (`Human` → `Conflict`): `Languages` auf
„erste,zweite", die Quell-Saetze neu, die maschinellen Saetze in BEIDEN Sprachen weg (sie entstanden aus dem Gemisch).
Die Schwelle von `Detect` selbst bleibt — sie entscheidet beim Einlesen, und dort ist ein falsches „zweisprachig"
teurer als ein verpasstes.

**Gebaut wird beim Einreihen, nicht auf Vorrat** (`CommentSetService.EnsureSourceAsync`, gerufen
aus `GameAnalysisService.CreateAsync` und aus `tools/LibraryImport queue`): der Rohbestand hat
94 898 kommentierte Partien, sie alle zu zerlegen waeren Millionen Zeilen fuer Partien, die nie
jemand spielt. `tools/LibraryImport comments` holt den Altbestand nach; `--probe n` misst die
Trennquote an echten Partien, ohne etwas zu schreiben.

**Ausgeliefert wird ueber `?lang=`** an den Sitzungs-Endpunkten (`GET /api/guess-sessions/{id}`,
`POST` beim Starten und beim Raten). Ohne Satz in der gewuenschten Sprache kommt die Quelle; fehlt
ein EINZELNER Halbzug darin, tritt die Quelle nur fuer diesen ein und die Zeile nennt ihre Sprache
(`GuessHistoryMoveDto.CommentLanguage`) — eine Luecke waere die schlechtere Antwort. Das
Sitzungs-DTO fuehrt `CommentLanguages` (was es gibt) und `CommentLanguage` (was gerade kommt). Der
Altbestand ohne Saetze bekommt seine Kommentare weiterhin direkt aus dem PGN.

Das Brett zeigt die Wahl als Kuerzel neben der Blaetterleiste (nur, wenn es mehr als eine Sprache
gibt); die Wahl merkt sich das Geraet, und beim ersten Mal gilt die Sprache der Oberflaeche.

**Uebersetzen** (`Services/CommentTranslationService.cs`, `tools/LibraryImport translate --to de`)
legt einen weiteren Satz an — `Origin = Machine`, dazu `TranslatedFrom` und das Modell. Vier
Regeln, die dabei nicht kippen duerfen:
* **Die PARTIE ist die Einheit, nicht der Kommentar.** Uebersetzt wird in moeglichst wenigen
  Fuhren (`ChunkChars` 8000), weil Figurennamen und Eroeffnungsbegriffe sonst innerhalb derselben
  Partie wechseln — „Springer" hier, „Pferd" zwei Zuege spaeter.
* **Die Figurenbuchstaben stehen im Auftrag** (de: K D T L S, fr: R D T F C …). Ohne diese Angabe
  wird aus den Zuegen Buchstabensalat.
* **Uebersetzt wird aus der QUELLE**, nie aus einer Uebersetzung, und die Quelle wird nie
  ueberschrieben — auch `--force` ersetzt nur eine MASCHINELLE Fassung.
* **Ein Fehlschlag schreibt gar nichts.** Ein halb uebersetzter Satz waere der schlechtere Zustand:
  er sieht vollstaendig aus.
* **Die LAENGE wird geprueft** (`MinLengthShare` = 0,7). Deutsch ist eher laenger als Englisch;
  liegt das Ergebnis deutlich darunter, fehlt Text. Am 2026-09-11 an echten Partien erlebt: ein
  sparsameres Modell lieferte 18 bis 53 % der Quelllaenge — Saetze mitten im Absatz abgeschnitten,
  waehrend Struktur und Zuege stimmten. Keine der uebrigen Pruefungen schlug an, weil in der Prosa
  keine Zuege stehen. Die Grenze liegt bei 70 % und nicht hoeher, weil manche Quell-Saetze selbst
  zweisprachig sind und die Uebersetzung die doppelte Haelfte zu Recht wegwirft (gemessen 53 %).
* **Kein offenes Zitat** (0.551.1, `CommentTranslator.EndsInsideQuote`, gilt fuer Partien UND Kurse). Das Modell
  oeffnet ein deutsches Zitat mit „ und schliesst es mit dem GERADEN `"` — das beendet in der JSON-Antwort den
  String, die Grammatik laesst danach nur noch das Schliessen zu: der Text endet mitten im Zitat, alle Eintraege
  dahinter fehlen. Am 2026-09-27 auf Prod gemessen: jede fuenfte Kurs-Linie verworfen (fehlende Eintraege), und
  308 von 57 079 Bibliothekstexten mitten im Zitat abgeschnitten GESPEICHERT — bei Partien fing das nur die
  Laengenpruefung, und die nur, wenn viel fehlte. Deterministisch, keine Laune (Linie 54177 dreimal an derselben
  Stelle). Zwei Riegel: der Auftrag verlangt Anfuehrungszeichen, die das Modell NICHT mit dem geraden `"` verwechselt
  (seit 0.555.1 `QuoteNote`: Deutsch »…«, Englisch ‘…’, sonst «…» — mit „…“ im Auftrag schloss Qwen trotzdem gerade:
  an vier Prod-Linien 0 von 4 Versuchen glatt, mit »…« 8 von 8), und
  ein Ergebnis, das in einem offenen „…/«…/“… endet, das die Vorlage NICHT offen hat, ist ein Fehlschlag (die Vorlage
  darf offen sein: Chessable trennt Saetze auch mitten im Zitat auf zwei Zuege auf). Weil das Modell streut (an 54177
  ging einer von zwei Versuchen glatt), bekommt eine Fuhre mit abgeschnittenem Zitat oder fehlenden Eintraegen EINEN
  zweiten Versuch (`MaxAttemptsPerChunk` = 2); ein abgebrochener Aufruf, zu wenig Text und die falsche Sprache nicht.

* **Deutsch duzt den Leser** (0.565.1, `CommentTranslator.AddressNote`, Wunsch 2026-09-27): ohne Vorgabe siezte Qwen fast
  immer („Beachten Sie …", „Finden Sie den Weg") — an acht Prod-Kurslinien 22-mal „Sie" gegen einmal „du"; mit der Zeile
  „Address the reader informally with du …" 0 gegen 18, auch in Aufforderungen. Andere Zielsprachen bekommen (noch)
  keine Vorgabe. Schon gespeicherte Uebersetzungen aendert das nicht — Stand 27.09.: 200 von 3 073
  Bibliotheks-Saetzen und 234 von 1 486 Kurs-Saetzen siezen (Suche nach „Sie/Ihnen/Ihr…" mitten im Satz).

Braucht `Anthropic:TextApiKey` (derselbe Schluessel wie die Puzzle-Tipps — NICHT der Konto-Schluessel
`Anthropic:ApiKey`, der gehoert allein dem Formular-Einlesen). Ohne Schluessel passiert
nichts — der Rest des Stacks laeuft unveraendert. **Das Modell ist ein eigener Schalter**
(`Anthropic:TranslationModel`, Vorgabe `claude-sonnet-5`) und nicht dasselbe wie bei den Tipps:
ein Tipp ist ein Dreizeiler, der jede Sorgfalt wert ist, eine Uebersetzung ist Mengenarbeit —
rund 8000 Zeichen je Partie, und der Bestand hat 94 898 kommentierte.

**Beides geht auch OHNE Analyse** (`comments --library n`, `translate --library n`): der Text
haengt nicht an der Engine, und eine angeforderte Partie steht damit sofort in beiden Sprachen da
statt erst nach einer halben Stunde Rechnen.

**Den GANZEN Bestand uebersetzen** (0.539.0): `translate --to de --library 200000 --parallel 8`
(Env `TextLlm__BaseUrl`/`TextLlm__ApiKey` = Spark, `ConnectionStrings__DefaultConnection`). Die Auswahl
ist `CommentTranslationService.LibraryCandidatesAsync` — kommentiert, weder aussortiert noch Dublette,
ohne Satz in der Zielsprache, die BESTEN zuerst (Note, kommentierte Halbzuege, Textmenge): auf ~95 000
Partien dauert es Tage, und was die Punktepartie zuerst zeigt, soll zuerst fertig sein. Die Quell-Saetze
legt der Lauf je Partie selbst an (`EnsureSourceForLibraryAsync`) — ein vorgeschaltetes `comments
--library` ueber den ganzen Bestand ist nicht noetig. `--parallel p` = p Partien gleichzeitig mit je
eigenem DbContext (vLLM buendelt gleichzeitige Anfragen; eine einzelne nutzt nur einen Bruchteil des
Durchsatzes). Warnungen des Uebersetzers und des Modell-Clients gehen auf die Konsole: „abgebrochen",
„verworfen: n % der Quelllaenge" und „am Token-Deckel abgeschnitten" sind sonst von „nichts zu tun" nicht
zu unterscheiden. Wiederholbar — was die Zielsprache hat, faellt aus der Auswahl.

**KURSE** (Plan „Kurs-Kommentare mehrsprachig", TODO.md — Stufe A 0.547.0 Uebersetzen + Ausliefern, Stufe B 0.548.0
Auftraege + Hintergrunddienst, Stufe C 0.549.0 Oberflaeche — Sprachwahl, Uebersetzungs-Kasten, Labels, siehe
`src/frontend/CLAUDE.md`): je Kurs-LINIE und Sprache ein `CommentSet` mit `BookPuzzleId` (dritter Anker neben
Bibliothekszeile und Analyse, eindeutig `(BookPuzzleId, Language)`, Cascade). Was dabei anders ist als bei Partien:
* **Es gibt KEINEN Quell-Satz.** Die Quelle bleibt die Linie (`Comment`, `MoveComments`, `Title`, `Chapter`) —
  genau diese Felder ueberschreiben Aufbereitung (in-place per oid/LineId) und naechtliches Aktualisieren; ein
  zweiter Quell-Satz muesste staendig nachgezogen werden. Kurs-Saetze sind `Origin = Machine`,
  `TranslatedFrom` = `Book.CommentLanguage` (Quellsprache, beim ersten Lauf aus einer Stichprobe der ersten 200
  kommentierten Linien bestimmt, nicht bestimmbar = `und`; eine spaetere Korrektur macht nichts ungueltig).
* **Stellen** (`Services/Courses/CourseTextSlots.cs`, NUR dort die Zahlen): `≥ -1` wie `MoveComments`, `-2` =
  `Comment`, `-3` = `Title`, `-4` = `Chapter`. Ein Satz traegt alles, was die Linie an Text hat.
* **Fingerabdruck** `CommentText.SourceHash` (`Services/Courses/CourseTextHash.cs`: 16 Hex-Zeichen SHA-256 ueber den
  normalisierten Text, `\r\n`→`\n`, getrimmt; NIRGENDS selbst hashen) — Pflicht bei Kurs-Saetzen, `null` bei
  Partien. Er entscheidet (1) VERALTET: passt er nicht mehr zum Text der Linie, liefert der Localizer das
  Original und der naechste Lauf uebersetzt nur diesen Text neu; (2) WIEDERVERWENDEN: derselbe Text in einem
  anderen Kurs-Satz gleicher Sprache (Doppel-Import, `_firstkey`-Kopie, gleicher Kapitelname) wird KOPIERT.
* **Uebersetzt wird in `Services/Courses/CourseTranslationService.cs`** ueber denselben Kern wie Partien
  (`Services/CommentTranslator.cs`: Fuhren, Auftrag, `PieceLetters`, Laengen- und Sprachpruefung — der Partie-Weg
  ist woertlich unveraendert, `CommentTranslationServiceTests` unangetastet). Je Linie: nur fehlende/veraltete
  Stellen ans Modell, gleiche Texte (Kommentar = Einleitung, bei 51 549 von 87 035 Prod-Linien) nur EINMAL,
  Stellen mit verschwundener Vorlage fliegen raus; das Modell muss JEDEN Eintrag beantworten, sonst schreibt die
  Linie NICHTS. Laenge und Sprache pruefen bei Kursen nur die Prosa-Stellen (`≥ -2`), die Laenge erst ab 150
  Zeichen; der Auftrag nennt die Regeln fuer Partie-Zitate/Namen/Ueberschriften; Quelle `und` → „from the language
  it is written in", Figurenbuchstaben bleiben.
* **Ein Kurs-Lauf** (`TranslateCourseAsync`): Quellsprache sichern (Ziel = Quelle → nichts), offene Arbeit in C#
  bestimmen (Fingerabdruck-Vergleich je Linie), alle dabei fehlenden KAPITELNAMEN in EINER Fuhre (Einheitlichkeit;
  jede Linie traegt ihr Kapitel danach an `-4`; Kapitelnamen bekommen KEINE Laengen- und Sprachpruefung — kurz und
  voller Namen, eine deutsche Liste mit englischen Eroeffnungsnamen laese sich als englisch), dann die Linien in
  Kursreihenfolge, `CourseTranslation:Parallel` (Vorgabe 4) gleichzeitig mit je eigenem Scope/DbContext, in **zwei
  Phasen**, damit jeder verschiedene Text je Lauf genau EINMAL ans Modell geht: Linien mit gemeinsamem Zuganfang
  tragen dieselben Kommentare, liegen nebeneinander und landen gleichzeitig im Parallel-Fenster, die Wiederverwendung
  greift aber erst nach dem Speichern (Prod, Buch 36: 1,90 Mio. Zeichen, davon 0,25 Mio. verschieden — ohne Phasen
  gingen 0,36 Mio. ans Modell). Phase 1: jeder offene Fingerabdruck GEHOERT der ersten Linie, in der er offen ist,
  jede Linie uebersetzt nur ihre eigenen (`ownedHashes`); Phase 2: alle noch offenen Linien normal, praktisch nur
  Wiederverwendung — ans Modell nur noch, wo die Besitzer-Linie scheiterte. Eine Linie zaehlt, sobald sie fertig ist
  (am Ende `LinesDone + LinesFailed = LinesTotal`), Zwischenstand alle 10 Linien an einen Rueckruf. Abbruch per
  Token: die laufenden Linien schreiben nichts, der naechste Lauf ueberspringt das Fertige.
* **Von Hand**: `tools/LibraryImport translate --to de --course <bookId> [--parallel p]` (Env wie beim
  Bibliothekslauf: `TextLlm__*`, `ConnectionStrings__DefaultConnection`; Strg+C bricht sauber ab) — ohne Auftrag,
  an der Warteschlange vorbei.
* **Auftraege** (0.548.0, `Services/Courses/CourseTranslationJobService.cs`, Tabelle `CourseTranslationJobs`, Endpunkte unter
  „Kurse"): anfordern darf jeder mit Kurs-Zugang, in jede der 25 Oberflaechensprachen
  (`Services/Courses/CourseTranslationLanguages.cs` — Spiegel von `SUPPORTED_LANGS`, beide Seiten mit LITERALER Liste im
  Test), **hoechstens EIN offener angeforderter Auftrag je Nutzer** (wartend oder laufend), Admin unbegrenzt. Gibt es
  fuer (Kurs, Sprache) schon einen offenen, kommt DER zurueck (200, egal wer ihn anlegte, zaehlt nicht gegen das
  Limit). Beides erzwingt der Dienst, nicht die DB. Reihenfolge der Pruefungen: Zugang (404) → Sprache
  (`unsupported-language`) → Modell (`not-configured`, 503) → Quellsprache (`same-language`, dafuer wird sie hier
  bestimmt) → vorhandener Auftrag → offene Arbeit (`nothing-to-translate`, ueber `OpenWorkAsync`, `LinesTotal` =
  offene Linien) → Limit (`user-limit`, 409, mit dem offenen Auftrag). **In der Sperrzeit angenommen, kein 503.**
  Zurueckziehen: der Nutzer seinen WARTENDEN, der Admin jeden offenen (`Cancelled`). **Konto loeschen** setzt die
  offenen Auftraege des Nutzers auf `Cancelled` („account deleted") — `RequestedByUserId` hat bewusst keinen FK.
* **Hintergrunddienst** (`Services/Courses/CourseTranslationWorker.cs`, EIN Auftrag gleichzeitig — die Parallelitaet steckt
  im Lauf): kein Text-Modell → schlafen (10 min); Sperrzeit (`QuietHours`) → schlafen bis `EndOf`, hoechstens 10 min
  am Stueck; sonst `ClaimNextAsync` — **angeforderte vor der Automatik, je Gruppe die aeltesten** (`InQueueOrder`, EINE
  Stelle fuer Dienst und Platzanzeige). Ohne Arbeit wartet er auf einen Weckruf (`CourseTranslationSignal`: Anfordern,
  Nachziehen), hoechstens 5 min. Beim Start `Running` → `Queued`. **Abbruch mitten im Lauf** ueber einen eigenen Token:
  Sperrzeit beginnt (alle 30 s geprueft) oder ein NEU angeforderter Auftrag kommt, waehrend AUTOMATIK laeuft
  (`PreemptAutomatic` — ein grosser Kurs rechnet sonst einen halben Tag, und „Vorrang" hiesse nur „danach") → zurueck
  auf `Queued`; Admin zieht zurueck → bleibt `Cancelled`; Dienst stoppt → bleibt `Running`, kommt beim Start zurueck.
  Der Fortschritt landet je Zwischenstand (zu Beginn, dann alle 10 Linien) in einem EIGENEN Kontext am Auftrag —
  nur die Zaehler; steht er dabei nicht mehr auf `Running` (zurueckgezogen, Konto geloescht), bricht der Lauf ab.
  Ergebnis: `Done` (auch mit einzelnen gescheiterten Linien, `LastError` nennt sie), `Failed` (keine einzige Linie
  ging durch), `Cancelled` + `same-language` (Quellsprache inzwischen = Ziel).
* **Automatik** (`CourseTranslation:AutoLanguages`, Compose `COURSE_TRANSLATION_AUTO_LANGUAGES`, Vorgabe LEER = aus; nur
  Prod bekommt `de,en`, sonst uebersetzte Dev dieselben Kurse ein zweites Mal auf der Spark): wartet nichts, legt der
  Dienst EINEN Auftrag fuer den naechsten Kurs an, dem die Sprache fehlt — zuletzt benutzte Kurse zuerst (juengster
  `CourseAttempt`), ueber die Sprachen hinweg (der frischeste Kurs bekommt de UND en vor dem naechsten), Kurse in der
  Quellsprache nicht. Vorauswahl in SQL („Linie mit Text ohne Satz in der Sprache"), den Fingerabdruck prueft der
  Lauf. **Sperrfrist `AutoCooldown` (7 Tage)** nach einem fertigen Auftrag der Sprache — sonst holte eine Linie, die nie
  einen Satz bekommt, die Automatik in eine Schleife. **Modell nicht erreichbar (0.624.1):** scheitert eine Linie,
  während `IsUnreachable` wahr ist, bricht `TranslateCourseAsync` den Lauf ab (`CourseTranslationRunStatus.Unreachable`),
  der Auftrag geht mit `LastError = "model unreachable"` und OHNE gescheiterte Linien zurück auf `Queued`, und der Dienst
  wartet `UnreachablePoll` (5 min) statt sofort den nächsten zu nehmen. Anlass 30.09.2026: zehn Minuten „Connection
  refused" — Auftrag 53 verbuchte 252 von 264 Linien als gescheitert, galt als fertig, und die Sperrfrist hielt den Kurs
  sieben Tage halb übersetzt.
* **Nachziehen** (`EnqueueRefreshAsync`/`NotifyCourseChangedAsync`): aendert sich ein Kurs mit Uebersetzungen — der
  Import-Kern `PgnImportService.ImportIntoBookAsync` (Aktualisieren, Neu-Aufbereiten, Cache-Weg, angehaengte Linien,
  auch nur geaenderte Etiketten), `CourseAuthoringService` (Kapitel umbenennen, Linien einfuegen) —, bekommt jede
  Sprache mit Saetzen einen Automatik-Auftrag (erledigt nur Veraltetes). Ein WARTENDER genuegt, ein LAUFENDER nicht
  (er hat seine Arbeit vorher bestimmt). Ein Fehler dabei laesst die Aenderung selbst nicht scheitern.
* **Vorrang vor dem Bibliothekslauf** liegt AUSSERHALB der App: `.jobs/spark-uebersetzung.sh` haelt an, solange ein
  angeforderter Auftrag offen ist (`Status IN (0,1) AND RequestedByUserId IS NOT NULL`).
* **Ausliefern ueber `?lang=`** (`Services/Courses/CourseCommentLocalizer.cs`, in den Controllern NACH dem Dienst): ersetzt
  `Comment` und `MoveComments[ply]` NUR, wo der Fingerabdruck zum Original IM DTO passt. **`Title`/`Chapter` werden
  NIE ersetzt** — der Kapitelname ist im Frontend ein SCHLUESSEL (`?chapter=`, Kapitel-PGN, Umbenennen,
  Gruppieren); die Uebersetzung kommt als `TitleLabel`/`ChapterLabel` (bzw. `Label` an den Kapitellisten), Anzeige
  `label ?? original`. Dazu `CommentLanguage` (was tatsaechlich kommt), `CommentLanguages` (was es fuer die Linie
  gibt, **Quelle zuerst**) und `CommentMachine`. **`lang` fehlt oder ist kein Kuerzel → DTO exakt wie vorher**
  (keine Abfrage — alte Clients, Offline-Kopien); `lang` = Quellsprache → Original. Endpunkte mit `lang` (per grep
  erhoben): `GET /api/courses/{id}/puzzles`, `/{id}/public`, `/{id}/next`, `/{id}/chapters`, `GET /api/courses/{id}`
  (Detail, Kapitel-Labels), `GET /api/book-puzzles/{id}`, `/{id}/next`, `/{id}/random`,
  `GET /api/calculations/books/{id}`, `/books/{id}/public`, `/positions/{id}`. **Bewusst Original**: Bearbeiten
  (`/{id}/lines`), alle PGN-Downloads, Tagespuzzle, Zufallspools (`/api/book-puzzles/random`), Wochenpost, Tipps,
  Kurs→Repertoire. `/{id}/flashcards` liefert nur Linien-Ids (nichts zu uebersetzen).
* **Loeschen**: jeder Pfad, der `BookPuzzles` loescht (`CourseAuthoringService.RemoveLinesAsync` = Linie/Kapitel,
  `BookAdminService.DeleteBookAsync` = Buch/eigener Kurs/Kurs→Repertoire/Konto, der Rueckbau in
  `CourseService.UploadPersonalCourseAsync`), raeumt die Kurs-Saetze AUSDRUECKLICH mit ab
  (`CourseTranslationCleanup`), das Buch zusaetzlich seine `CourseTranslationJobs`. Die TEXTE uebernimmt in MariaDB
  der Fremdschluessel (sie zu laden hiesse, jede Uebersetzung zu lesen und einzeln zu loeschen), unter InMemory
  werden sie ausdruecklich geloescht.

| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/guess-sessions` | Auth | Eigene Durchlaeufe (max. 100, neueste zuerst) |
| POST | `/api/guess-sessions` | Auth | Starten `{ gameAnalysisId, guessWhite?, startPly? }` — `guessWhite` weglassen = Seite des Gewinners |
| GET | `/api/guess-sessions/{id}` | Auth | Zustand: Kopf, Punkte, Verlauf bis hierhin, aktuelle Stellung — **ohne** den zu ratenden Zug |
| POST | `/api/guess-sessions/{id}/guess` | Auth | Raten `{ uci?, addSeconds? }`; leeres `uci` = passen (0 Punkte, keine Strafe). HIER kommt der Partiezug zum ersten Mal mit |
| GET | `/api/guess-sessions/{id}/review` | Auth | Rueckblick: je Halbzug Partiezug, eigener Zug, Stufe, Engine-Bestzug |
| DELETE | `/api/guess-sessions/{id}` | Auth | Durchlauf loeschen |
| GET/POST/DELETE | `/api/guess-sessions/anonymous[/{id}[/guess\|review]]` | **AllowAnonymous** + RL | Dieselben sechs Operationen ohne Konto; Kennung als `?sessionId=` (GET/DELETE) bzw. im Rumpf (POST). Ungueltige Kennung → 400 |
| GET | `/api/game-analyses/public` | **AllowAnonymous** + RL | Kuratierter Bestand: freigegebene UND spielbare Partien (mind. eine gerechnete Stellung), mit `annotated` fuer den Filter „alle / nur kommentierte". Kopfdaten und Fortschritt, **nicht** die Zugliste |
| PUT | `/api/game-analyses/{id}/public` | Auth | Partie in den Bestand aufnehmen/herausnehmen `{ isPublic }` — Besitzer der Analyse oder Admin |
| POST | `/api/game-analyses/guess` | Auth | Eigene Partie einwerfen `{ pgn, title? }` — KEINE Tiefe/Linien/Engine im Rumpf (Server setzt Tiefe 20). 400 mit `reason` ∈ `too-many-open` / `no-engine` / `invalid-pgn`; die Seite formuliert den Satz, der Server kennt die Sprache nicht |
| GET | `/api/game-analyses/throughput` | Auth | Tempo und Restdauer der eigenen Analysen AUS DER HISTORIE (`GameAnalysisPosition.AnalyzedAt`): Stellungen je Minute, gemessene Spanne, Rest, hochgerechnete Restdauer. Vorher zaehlte der Browser selbst mit — eine Minute offene Seite, bevor ueberhaupt etwas dastand, und beim naechsten Aufruf wieder bei null. Seit 0.521.2 nach `Services/Engines/AnalysisPace.cs`: die juengsten ≤ 200 Ergebnisse der letzten 24 h, davon nur der juengste ZUSAMMENHAENGENDE Lauf (eine Luecke > 15 min = Pause, zaehlt nicht), gemessen bis jetzt, solange der Lauf lebt, sonst bis zum letzten Ergebnis. Vorher: letzte Stunde ab dem ersten Zeitstempel darin — eine Pause davor zaehlte als Rechenzeit („0,56 Stellungen/min · noch ca. 1 h 44 min" fuer 58 offene Stellungen). Dieselbe Regel rechnet die Restdauer einer Partie (`GameEvals.EtaMinutes`). Literal-Route vor `{id:int}` |
| GET | `/api/game-analyses/guess/status` | Auth | Steht eine Engine bereit (eigene oder Haus) und wie viele der fuenf Plaetze sind frei — gefragt, BEVOR jemand ein PGN hineinkopiert |
| PUT | `/api/engine/house` | Admin | Eigene Hintergrund-Engines als Haus-Engine freigeben `{ share }` (403 ohne Admin, 400 ohne Token bzw. ohne hinterlegte Hintergrund-Engine) |

`annotated` wird IN SQL ermittelt (`Pgn LIKE '%{%'`): jede geschweifte Klammer in einem PGN ist ein
Kommentar, und so bleibt das LONGTEXT-Feld ausserhalb der Antwort. Menue-Key `guess`, Stufe **All**;
die Route traegt entsprechend keinen `authGuard` mehr.

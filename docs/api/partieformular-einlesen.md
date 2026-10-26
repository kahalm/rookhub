# Partieformular einlesen (auth, 0.529.0)

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Partieformular einlesen (auth, 0.529.0)“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.


Foto eines handgeschriebenen Partieformulars → legale Partie in „Meine Partien" (Quelle `scoresheet`), das Foto
bleibt daneben liegen. Menü-Key `scoresheet` (Stufe `Registered`), Frontend `/games/scoresheet` (Upload) und
`/games/:id/edit` (Korrekturseite, für JEDE eigene Partie). Braucht `Anthropic:ApiKey` — und der gehört seit 2026-09-25
ALLEIN diesem Feature (Tipps/Übersetzung laufen über `Anthropic:TextApiKey`); ohne Schlüssel antwortet der Upload 503 `notConfigured` und die Seite sagt es.

**Datenschutzerklärung** (Codereview 2026-09-29, A6-008): der Abschnitt „KI-Dienste" nennt Anthropic als Empfänger der
Formular-Fotos und den Sprachmodell-Server (Spark) als Empfänger der Texte (nur als Kategorie). Rechtstext bleibt
ENTWURF: Betreiber namentlich nennen und AV-Rolle klären entscheidet der Betreiber. LeagueHub-Einlesungen haben
keinen Aufräumlauf — das Foto geht nur beim Übernehmen oder Verwerfen.

**Drei Schichten, jede für sich testbar:**
* **Lesen** (`ClaudeScoresheetVisionClient`, Modell `Anthropic:ScoresheetModel`, Vorgabe seit 0.533.2 `claude-opus-5-5`
  im Modus „nur abschreiben" — siehe **Vorgabe** unten; mit `Scoresheet:Thinking=true` adaptives Nachdenken; gestreamt, structured output nach `ScoresheetPrompt.Schema`): je Halbzug, was DASTEHT (`written`, in der
  Sprache des Formulars), die Lesart des Modells als englische SAN, bis zu drei Ersatz-Lesarten und eine Sicherheit.
  Das Modell soll zuerst das GANZE Formular lesen und die Partie im Kopf mitspielen; Korrekturen (Streichungen,
  Pfeile, übersprungene Zeilen) löst es selbst und nummeriert neu. Bild vorher aufrecht (EXIF) und auf 2000 px
  (`ScoresheetImage`).
* **Auflösen** (`ScoresheetResolver`, rein, ohne Modell): macht daraus die LEGALE Partie, die das ganze Formular am
  besten erklärt — Strahlsuche über 32 Stellungen statt einer Entscheidung je Zug. Kosten je Halbzug: Lesart des
  Modells 0, Eintrag in seiner Sprache 0,2 (`ScoresheetNotation`: 18 Sprachen, Groß/klein egal, Langschrift,
  Figurinen, jede Lesart ist nur ein KANDIDAT — die Stellung entscheidet), Ersatz-Lesart 1, fehlender Zusatz
  („Sd2" für Sbd2) +0,3, Lesefehler 2,5 je Zeichen, Joker 8. **Der Joker braucht eine Bestätigung** durch den
  nächsten Eintrag (sonst „löst" die Suche jeden Unsinn auf), höchstens 1 + einer je 15 Einträge, und bei einer
  Sackgasse geht die Suche bis zu drei Einträge zurück. **Unsichere Stellen** bekommen die drei wahrscheinlichsten
  Lesarten, jede bis zu 60 Einträge weitergespielt; gemessen wird die GLATTE Reichweite (bis zum ersten Lesefehler
  oder Joker — mit genug Reparaturen käme fast jede falsche Lesart bis zum Ende). **Verschobene Formulare**
  (0.530.0): passt an einem Eintrag nichts glatt, darf ein Zug EINGESCHOBEN werden, der auf dem Formular fehlt
  (Kosten 5, bestätigt nur, wenn der Eintrag danach glatt passt; vorsortiert nach dem übernächsten Eintrag, höchstens
  6 je Zustand), oder ein Eintrag ÜBERSPRUNGEN — aber nur ein DOPPELT notierter (sonst wäre Überspringen der
  billigste Weg, Unleserliches loszuwerden, und die Lesung ließe still echte Züge weg). Solche Wege bekommen drei
  Einträge lang reservierte Plätze im Strahl, sonst verdrängen sie die vielen billigen Lesefehler-Varianten. Weitere
  Regeln: ein KLEINER Buchstabe a–h vorn ist eher Bauer als Figur (+0,3), in Handschrift verwechselbare Zeichen (6/8,
  1/7, a/d …) machen einen Lesefehler um 0,5 billiger, ein gestrichenes/dazugedichtetes Zeichen zählt 1,2 statt 1;
  Gleichstände werden fest nach Stellung/Zug entschieden (die Zugreihenfolge von Gera.Chess ist kein Vertrag).
  **Eintrag vor Deutung** (0.531.0): der Formular-Eintrag kostet 0, die SAN-Deutung des Modells 0,1 — sind beide legal und
  verschieden, gewinnt was DASTEHT (das Modell „korrigiert" sonst richtige Einträge, deren Legalität es falsch einschätzt;
  ist der Eintrag illegal, trägt die Deutung). **Markiert** wird zusätzlich: Sicherheit „low", eine vom Modell genannte
  Alternative, die ein ANDERER legaler Zug ist (auch bei „high"), und ein Widerspruch Eintrag/Deutung zwischen gleich
  direkten Lesarten. „medium" allein markiert NICHT — das Modell vergibt es freigiebig (am Testsatz 13 % aller Züge).
  **Kurzschrift beim Schlagen** (0.555.0, gemeldet 2026-09-27): „LxS", „SxB", „DxD", „exd" — schlagende Figur (bzw.
  Linie des Bauern) und geschlagene Figur (bzw. Ziellinie) OHNE Zielfeld, die alte Schreibweise und die von Anfängern
  und Kindern. `ScoresheetNotation.ShortCaptures` liest sie (Figurenbuchstaben wie sonst, der geschlagene Bauer heißt
  deutsch/skandinavisch „B", sonst „P"; gemischte Schrift mit englischem „B" für den Läufer läuft über die Fremdsprache
  mit), `ScoresheetResolver.Score` prüft sie gegen das Brett VOR dem Zug (`Squares`/`CaptureOf`: welche Figur zieht,
  welche steht auf dem Zielfeld, en passant) — Kosten 0,2 (Eintrag) bzw. 0,3 (Deutung des Modells), ein zweiter
  passender Schlagzug bleibt als ebenbürtige Lesart markiert. Anlass: Opus 5.5 „nur abschreiben" kopiert „BxB" wörtlich,
  der Auflöser machte daraus „b3" (Lesefehler-Weg) → falsche Partie ab Zug 6 mit 17 Markierungen. Gemessen per
  `--replay` (kostenlos): das gemeldete Formular 29–31 → 50/50 (drei gespeicherte Lesungen, 2–4 statt 17–22 unsicher),
  10er-Testsatz mit der Referenz-Lesung 550 → 579/591 (Beleg 10: 59 → 88/92), Opus-5-mit-Nachdenken- und
  Opus-5.5-effort-low-Lesungen unverändert.
  Unsicher bleibt eine Stelle nur,
  wenn der Zug zurechtgebogen ist, das Modell zweifelte oder eine andere Lesart gleich weit trägt, ohne teurer zu
  sein; eine Zugumstellung (17. Sbd4/Sfd4 Sxd4 18. Sxd4) gilt als ebenbürtig. Anlass-Partie als Test:
  `ScoresheetResolverTests` rekonstruiert alle 66 Halbzüge allein aus den deutschen Einträgen.
* **Ablauf** (`ScoresheetScanService` + `ScoresheetScanWorker`; die Fassade verteilt seit der Zerlegung Z6 auf
  `ScoresheetQuota` (Kontingent), `ScoresheetIntake` (Annahme), `ScoresheetProcessor` (Lesen, Engine-Prüfung, Partie;
  die PGN-Kommentare der Lesung baut `ScoresheetComments`),
  `ScoresheetCorrection` (Korrekturseite, Vereinspartie aus dem Archiv), `LeagueScoresheetScans` (LeagueHub) und
  `ScoresheetScanRows` (Zeile → DTO, Löschen ohne Foto)): der Upload legt einen `ScoresheetScan` an
  (Foto in der DB, `LONGBLOB`, über 12 MB auf 3000 px verkleinert), der Worker liest im Hintergrund — DB-gestützt,
  eigener Weckruf (`ScoresheetScanSignal`), NICHT die Arbeitsspeicher-Queue (Watchtower-Neustart, Chessable-Import).
  Beim Start kommen `Running` zurück auf `Pending`, nach `MaxAttempts` (3) ist Schluss. Geht die Lesung irgendwo
  nicht auf, fragt der Dienst NACH (`ScoresheetPrompt.Repair`: akzeptierte Züge, FEN, legale Züge, „oft ist ein
  FRÜHERER Eintrag falsch"), höchstens drei Durchgänge; behalten wird die beste Lesung (weitester Weg, dann die
  wenigsten Unsicherheiten). Im PGN stehen `{sheet: Qxd4}` an zurechtgebogenen Zügen und
  `{sheet, not resolved: …}` am letzten Zug.
* **Festgedacht → ohne Nachdenken abschreiben** (0.532.1, `ScoresheetReadMode`): am 25.09. auf Prod dachte Claude an
  einem vollen, verbesserten 60-Zug-Formular (Kufstein) 14 Minuten und 64 000 Tokens nach und schrieb kein Zeichen
  Antwort — 1,63 $, gescheitert mit `truncated`. Seither: ein Aufruf MIT Nachdenken hat höchstens
  `ScoresheetReader.FullCallMaxTokens` (40 000; am Testsatz brauchte die längste Lesung 23 142), und wird er
  abgeschnitten, liest der nächste Durchgang denselben Auftrag OHNE Nachdenken (`ThinkingConfigDisabled` +
  `ScoresheetPrompt.TranscribeSystem`, Deckel `TranscribeCallMaxTokens` 32 000) — und bleibt dabei, auch für die
  Nachfragen. Der Laufzeit-Deckel des Workers (`ScoresheetScanWorker.MaxRuntime`) steht auf 40 statt 15 Minuten: die
  15 reichten gerade für EINEN Aufruf. Reißt er trotzdem, ist die Einlesung `timeout` (Glocke), und der laufende Aufruf
  wird mit seinem ungünstigsten Fall verbucht (Reserve-Eingabe + sein Deckel) — die API meldet dann keine Tokens mehr.
  Nur das HERUNTERFAHREN lässt eine Einlesung auf `Running` (`ProcessAsync(…, shutdown)`), sie kommt beim Start zurück.
  Beim Gegenlesen fiel ein zweiter Fehler auf: `AddBranches` hörte nach `MaxBranchPoints` (40) GANZ auf — bei einer
  langen Lesung mit fast nur „medium" war das Budget nach 28 Zügen verbraucht, und jeder spätere zurechtgebogene Zug
  stand da wie ein sicherer (Kufstein: 42 repariert, 14 markiert). Jetzt wird über das Budget hinaus weiter MARKIERT
  (zurechtgebogen oder „low"), nur ohne Lesarten. Am 10er-Testsatz unverändert (578/591, dieselben Markierungen).
* **Ohne Nachdenken von Anfang an** (0.533.0, `Scoresheet:Thinking=false`, seit 0.533.2 die VORGABE): der erste Durchgang liest
  schon im Modus `Transcribe` (Deckel `TranscribeCallMaxTokens`), die Nachfragen ebenso. Gedacht für ein kleineres
  Modell (`Anthropic:ScoresheetModel` = Haiku — dessen adaptives Nachdenken ist nicht geprüft, daher nur so) oder wenn
  das Nachdenken sein Geld nicht wert ist. Wer das Modell wechselt, stellt die Preise der Kostenbremse mit um.
  Im Testwerkzeug seit 0.533.2 ebenfalls Vorgabe (`--thinking` schaltet es ein), `--usd-per-mtok ein,aus` (Vorgabe nach
  Modell: `claude-haiku-*` 1,5; `claude-sonnet-*` 2,10; `claude-opus-5-5` 4,20; sonst 5,25).
  **Gemessen 25.09.** (10er-Testsatz, 591 Halbzüge): Opus 5 MIT Nachdenken 578 (97,8 %, ~0,37 $ und 1–4½ min je
  Formular, 6 falsch ohne Marke); Opus 5 OHNE Nachdenken 563 (95,3 %, 0,13 $ und ~37 s je Formular, 10 falsch ohne
  Marke); **Haiku 4.5 ohne Nachdenken 208 (35,2 %, 0,025 $, 160 falsch ohne Marke) — unbrauchbar**: am Kufstein-Formular
  erfand Haiku eine völlig andere, LEGALE Partie (2…Dd6 3…c5 statt 2…Sf6 3…Sxd5), die kaum Reparaturen braucht und
  deshalb sicher aussieht. Eine glatte Auflösung ist also KEIN Beleg für eine richtige Lesung.
* **Denkaufwand** (0.533.2, `Anthropic:ScoresheetEffort` = low|medium|high|xhigh|max, leer = Vorgabe des Modells;
  Testwerkzeug `--effort`). `ClaudeScoresheetVisionClient.PlanThinking` ist die EINE Regel je Modell und Modus: „nur
  abschreiben" schaltet das Nachdenken ab — außer bei Modellen, die das nicht erlauben (`claude-opus-5-5`, Fable/Mythos:
  Nachdenken immer an), dort `effort: low`; Opus 5 verbietet Abschalten zusammen mit `xhigh`/`max`, der effort fällt dann
  weg. Ohne diese Regel liefe der Rückfall unter Opus 5.5 in einen 400. Der OpenAI-kompatible Leser schickt beim
  Abschreiben `chat_template_kwargs: {enable_thinking: false}` und `reasoning_effort: low` mit (Qwen3/Qwen3.5 denken
  sonst über die Chat-Vorlage; gpt-oss hört nur auf den zweiten Schalter, 0.597.2).
  Er STREAMT (`stream: true`, `include_usage`): vor dem Spark kappt ein Reverse-Proxy (openresty) jede Anfrage nach
  90 s ohne Antwort mit 504, eine Formular-Lesung dauert dort Minuten.
* **Vorgabe = Opus 5.5 „nur abschreiben"** (0.533.2, Wunsch des Nutzers nach dem Modellvergleich vom 25.09.2026):
  `Anthropic:ScoresheetModel` = `claude-opus-5-5`, `Scoresheet:Thinking` aus (→ `effort: low` + `TranscribeSystem`),
  Preise der Kostenbremse 4 $ / 20 $. Gemessen am 10er-Testsatz: 550/591 (93,1 %), 7 falsch ohne Marke, 0,08 $ und
  ~25 s je Formular (Ausreißer Formular 10: 59/92); am Kufstein-Formular die einzige schlüssige Lesung (120 Halbzüge,
  10 Reparaturen; Opus 5 ohne Nachdenken 52, mit Nachdenken festgedacht). Weitere Zeilen des Vergleichs: Opus 5.5
  `effort: low` mit vollem Auftrag 95,6 % (am Kufstein-Formular nach 13 Zügen aufgegeben), Sonnet 5 93,4 % ohne /
  94,5 % mit Nachdenken (8 Formulare, so teuer wie Opus 5), Qwen3.5-122B auf dem Spark rund 70 % (verrutscht bei
  unordentlichen Formularen). Diese Konfiguration ist die REFERENZ für künftige Vergleiche; zurück zu Opus 5 mit
  Nachdenken: `Anthropic:ScoresheetModel=claude-opus-5`, `Scoresheet:Thinking=true`, Preise 5 / 25.

**Lesen von außen statt über den Schlüssel** (0.687.0, Wunsch 2026-10-06: „der Watcher soll das bisherige Verarbeiten via
Key ersetzen — Key ganz abschalten"; `Scoresheet:Reader`, in `appsettings.json` auf `external`, Rückfall ohne Angabe =
`model` wie bisher — so laufen die Unit-Tests). Mit `external` liest der `ScoresheetScanWorker` nichts mehr: jede
Einlesung (RookHub UND LeagueHub, mit Konto oder über einen Teilen-Link) bleibt `pending`, bis der Watcher auf dem Server
(`~/claude/formulare-bot/watch.sh`, Claude in einer eingeschränkten Sitzung) sie über `/api/admin/scoresheets` liest und
die Lesung zurückgibt. `ProcessReadingAsync` nimmt sie in der Form der Modell-Antwort (Kästen in Pixeln des aufrechten,
auf 2000 px verkleinerten Fotos) und geht danach denselben Weg wie eine gelesene (`FinishAsync`: Auflösung,
Engine-Prüfung, Liga → offen zum Prüfen, eigene → Partie + Glocke); `Model = "claude-manual"`. Der Schlüssel ist
dabei nicht nötig (`Available` gilt auch ohne), und Tageszahl/Anzahl offener gelten weiter, die Kostenbremse nicht
(keine Kosten). Der Watcher läuft seit 21.10.2026 jede Minute (crontab, vorher alle 5) und meldet das Abholen über
`POST /api/admin/scoresheets/{id}/start` (Status `running`, `StartedAt`). Die Seiten zeigen seit 0.817.0 zwei Schritte mit
Uhr (`SheetReadingStatusComponent`, RookHub-Upload und LeagueHub „Formular prüfen“): „Wartet auf den Leser“ ab dem Hochladen,
nach dem Abholen ✓ dahinter (mit der Wartezeit) und „Wird gelesen“ ab `ScoresheetScanDto.StartedAt`; nach 15 min Warten
der Hinweis, dass der Leser wohl nicht läuft. (Die alte Schätzung „1–2 Sekunden pro Zug“ ist seit 0.815.2 weg.)

**Kostenbremse** (`ScoresheetBudget`, gewünscht 2026-09-25: „nicht dass einer mein Konto leerräumt"): gerechnet in
GELD, nicht in Einlesungen — eine Einlesung mit zwei Nachfragen kostet das Dreifache. Jeder Aufruf verbucht SOFORT
die Tokens, die die API meldet (`InputTokens`/`OutputTokens`/`CostMicroUsd` an der Einlesung; Nachdenken zählt als
Ausgabe, abgebrochene und abgeschnittene Aufrufe zählen mit). Drei Budgets, alle als Konfiguration mit Vorgabe:
`Scoresheet:UserDailyUsd` (2), `Scoresheet:UserMonthlyUsd` (10, über 30 Tage), `Scoresheet:GlobalDailyUsd` (15, alle
Nutzer zusammen — schützt das Konto auch bei vielen Nutzern), dazu `Scoresheet:AnonDailyUsd` (3, ALLE Einlesungen
ohne Konto zusammen — zehrt zusätzlich vom Gesamtbudget, der Rest bleibt den Konten; Sperrgrund bleibt
`globalBudget`, Codereview A6-003); Preise `Scoresheet:InputUsdPerMTok` (4) /
`Scoresheet:OutputUsdPerMTok` (20) = Claude Opus 5.5 (bis 0.533.1: 5 / 25 = Opus 5) — wer das Modell wechselt, stellt sie mit um. **Der
Antwort-Deckel (max_tokens) kommt aus dem verbleibenden Budget** (`ScoresheetBudget.Allowance`, seit 0.531.0): so viel
Ausgabe, wie nach 12 000 Eingabe-Tokens Reserve noch bezahlbar ist, höchstens 64 000 (je Aufruf mit Nachdenken seit
0.532.1 höchstens 40 000, siehe oben); unter 16 000 startet der Aufruf
nicht — so wird kein Budget überzogen, und lange Partien bekommen trotzdem Platz (ein fester Deckel von 24 000 schnitt
am Testsatz die 92-Halbzug-Partie nach 0,63 $ ab). Der Stoppgrund im Stream wird über Gleichheit mit der API-Zeichenkette
geprüft (`reason == "max_tokens"`), NICHT über `ToString()` — das meldete ein abgeschnittenes Ende als „failed". Geprüft beim Upload (Absage 400 `userDailyBudget`/`userMonthlyBudget`/
`globalBudget`) und vor JEDEM Aufruf im Worker: vor der ersten Lesung scheitert die Einlesung mit dem Grund, vor
einer Nachfrage entfallen nur die Nachfragen (die Lesung bis dahin bleibt). Admins: keine Nutzerbudgets und keine
Tageszahl, das Gesamtbudget gilt auch für sie. Dazu weiter `Scoresheet:DailyLimit` — seit 0.568.1 **EINE Einlesung je
24 h** (Wunsch 2026-09-27, vorher 20; rollendes Fenster) — und 3 offene je Nutzer. Mit einer am Tag hängt viel an der
Zählregel (`ScoresheetQuota.CountingSince`): eine GESCHEITERTE Einlesung ohne Kosten zählt nicht (sonst sperrte
ein Ausfall auf unserer Seite 24 h), eine bezahlte schon. **Partie löschen ist kein zweiter Versuch**: die Einlesung
bleibt als Zeile stehen, nur Foto, Dateiname, Modell-Antwort und Stand gehen mit der Partie
(`DetachWithoutLoading`; vorher ging die Zeile samt Verbrauch — löschen + neu hochladen umging Tageszahl UND Budget).
Solche Zeilen stehen nicht in `GET /api/scoresheets`. Das UPDATE läuft im selben SaveChanges VOR dem DELETE der Partie
(sonst nähme der Cascade-Fremdschlüssel die Zeile mit) — `ScoresheetScanSqlTests` prüft das gegen MariaDB. Die
Upload-Seite zeigt den Verbrauch in Prozent (`budgetUsedPercent`, das knappere Budget) und bei erreichter Tageszahl,
ab wann die nächste geht (`nextAllowedAt`).

**Engine-Prüfung der Lesung** (0.646.0, `Services/Scoresheet/ScoresheetPlausibility.cs`, gemeldet 2026-10-03 an Gruber–Schöler,
LeagueHub-Vereinspartie 153: „viele Zickzack in der Bewertung — meist ein Zeichen, dass ein Zug nicht richtig erkannt wurde").
Das Modell hatte „Tc1" als „Te1" und „Ke4" als „Kc4" gelesen — beides legal, also nahm der Auflöser es, und ab dort ließ
Schwarz zehn Züge lang `fxe1=D+` liegen. Der Auflöser kennt nur Schrift und Legalität; nach dem Lesen (`ProcessAsync`, BEIDE
Wege — „Meine Partien" und Liga) bewertet deshalb Stockfish die Partie (`IScoresheetEngine`, Singleton
`StockfishScoresheetEngine`: EIN Prozess je Prüfung, `go depth 10`, ~7 ms je Stellung). Regeln, die nicht kippen dürfen:
* **Zickzack** = ein Halbzug verliert ≥ `SwingPct` (15) Prozentpunkte GEWINNCHANCE (Lichess-Formel `GameAccuracy.WinPercent`,
  nicht Centibauern — ein verpasstes Matt in entschiedener Stellung zählt sonst), und der nächste ebenso. Gesucht wird an den
  `Window` (4) Halbzügen bis einschließlich des ersten Patzers (bei „Kc4" kam er zwei Halbzüge später).
* **Ersatz-Lesarten nur um EIN leicht verwechselbares Zeichen** (`MaxExtraCost` 2,0 über der gewählten — c/e, 1/7, 5/6 aus
  `ScoresheetNotation.IsConfusable`; ein beliebiges Zeichen 2,5 nur, wenn das Modell den Eintrag selbst „low" las). Gemessen am
  10er-Testsatz (perfekte Lesung und Abschrift, 20 Durchläufe): mit 2,5 standen 13 richtige Züge als unsicher da (echte
  Amateur-Patzer, die eine beliebige Ein-Zeichen-Lesart „glättet"), mit 2,0 drei. Der Rest wird je Lesart über ein Fenster neu
  gelesen (`ScoresheetResolver.Resolve(…, writtenTo:)`, neu: liest nur bis zu einem Eintrag) und neu bewertet.
* **Zwei Stufen**: ERSETZT (`ScoresheetPly.Check = "replaced"`, unsicher, alte Lesung als zweite Option) nur, wenn das Zickzack
  ≥ `PersistentPlies` (4) Halbzüge anhält, genau EINE Stelle es am besten beseitigt (Gleichstand an mehreren Halbzügen = die
  Engine kann nicht unterscheiden — „Ra6" statt „Ra5+" glättet „Kc4" genauso wie „Ke4"), es ≥ `MinReplaceGain` (2) Zickzacks
  beseitigt UND die volle Neuauflösung nicht mehr zurechtgebogene Züge und nicht mehr Unaufgelöstes hat (am Testsatz, Beleg 04,
  glättete „Lf3+" statt „Lxf5" das Zickzack und bog dafür elf Züge zurecht). Sonst nur ANGEBOTEN (`Check = "suggested"`: der
  gelesene Zug bleibt, unsicher, die Lesart ohne Zickzack als zweite Option; bei Gleichstand bis zu drei Stellen).
* **Messung** (Prüfwerkzeug im Scratchpad, nicht im Repo): Gruber nachgestellt (Soll-Partie + „Te1"/„Kc4" eingestreut) → Te1
  ersetzt, Ke4 angeboten, 3 s; 10er-Testsatz → 0 Ersetzungen, 3 Markierungen an richtigen Zügen; 80 zufällig eingestreute
  Ein-Zeichen-Fehllesungen → 0 Schäden, 3 richtig angeboten — die meisten Fehllesungen fängt schon der Auflöser ab, und von den
  übrigen sind die meisten „leise" (kein Bewertungssprung): die sieht keine Engine.
* **f/g verwechselbar** (0.647.1, `ScoresheetNotation.Confusable`): in Einlesung 11 derselben Partie las das Modell „Dg5 Dxg5
  hg5" als „Df5 Df5 hf5" — mit dem Paar findet schon der Auflöser 113/115 Halbzüge (Test `Resolve_GsReadAsFs_FindsTheQueenTrade`
  mit den echten Einträgen); am Testsatz unverändert.
* **Ausfallsicher**: keine Engine, Fehler, `Budget` (90 s) überschritten → die Lesung bleibt, wie sie ist (Warnung im Log).
  Schalter `Scoresheet:Plausibility=false`; Pfad `Scoresheet:EnginePath` (sonst `StockfishPath.Resolve`: `/usr/games/stockfish`
  aus dem Debian-Paket im API-Image — NICHT im PATH —, sonst `stockfish`), Tiefe `Scoresheet:EngineDepth`. Dieselbe Regel
  gilt seit 0.647.1 für den Tipp-Generator (`StockfishAnalyzer`, `Stockfish:Path`) — bis dahin fand er im Container keine Engine.
* **Oberfläche**: Chip „von der Engine korrigiert" / „Engine zweifelt" mit Erklärung als Tooltip (`games.edit.engine*`, RookHub
  `game-edit.component.ts`; LeagueHub `club-scan-page.component.ts`), `check` reist in `ScoresheetPly`/`EditPly` mit. Das
  Neu-Aufbereiten auf der Korrekturseite (`…/scoresheet/resolve`) prüft NICHT erneut — es läuft im Request.

**Testwerkzeug** `tools/ScoresheetBench` (Konsole, wie `tools/LibraryImport` kein Teil des Images, OHNE Datenbank
und ohne zweite API-Instanz — es benutzt nur `ScoresheetReader` + Auflöser): misst einen Ordner mit `NN.png|jpg`
(Formular), `NN.pgn` (Soll), `NN.formular.txt` (Abschrift) und `belege.json`. `--resolver-only` nimmt die Abschrift
als Eingabe (kostenlos, misst nur den Auflöser); ohne den Schalter liest das Modell die Bilder
(`ANTHROPIC_API_KEY`, harter Deckel `--max-usd`). Ausgabe: `report.md`, `results.json`, je Beleg `NN.plies.tsv`
(Abschrift, Modell, Soll, Ergebnis, Art, Lesarten, Kosten des Soll-Wegs) und `NN.answer.json`. Stand 25.09.2026 am
10er-Testsatz (HCS USA + Brasilien, portugiesisch): nur Auflöser 583/591 Halbzüge richtig, jeder falsche markiert.
Mit Modell (Claude Opus 5, 25.09.): 0,18–0,56 $ und 1–4½ min je Formular, 3,69 $ für alle zehn; auf den neun vollständig
gelesenen 486/499 richtig. `--replay <ordner>` wertet gespeicherte Antworten neu aus, ohne Kosten — so werden
Auflöser-Änderungen an echten Modell-Lesungen gemessen. Verglichen wird der ZUG (von–nach), nicht die Schreibweise.
**Andere Leser** (0.532.0, bisher NUR im Testwerkzeug, in der API nicht verdrahtet): `--provider openai` liest über eine
OpenAI-kompatible Schnittstelle (vLLM auf eigener Hardware, gedacht für Qwen3-VL auf dem DGX Spark;
`OpenAiScoresheetVisionClient`: Bild als Daten-URL, `response_format: json_schema` mit demselben Schema — lehnt der
Server das ab, einmal ohne und dann dabei bleiben —, Denkblöcke und Code-Zäune fallen weg, `finish_reason: length` =
`truncated`; Vorgabe-Auftrag `ScoresheetPrompt.TranscribeSystem` = NUR abschreiben, `--prompt full` = der
Claude-Auftrag). `--provider dots` nimmt dots.ocr (`DotsOcrScoresheetVisionClient`): ein Dokument-Leser, der IMMER
denselben Layout-Auftrag bekommt — wörtlich `prompt_layout_all_en` aus dem dots.ocr-Quelltext, auf diesen Wortlaut ist
das Modell trainiert, ein Test hält ihn fest — und Tabellen als HTML liefert; `DotsOcrLayout` macht daraus
Formular-Einträge (Nummern-Spalte = Zahlen, die Zeile für Zeile um eins steigen — so fallen Zeit-Spalten heraus;
jede Nummern-Spalte beginnt einen Block, darin Weiß und Schwarz; fehlende Nummer aus dem Versatz; ohne Nummern
paarweise; ohne Tabelle Zeilen „12. Sf3 Sc6"), ohne SAN-Deutung, und liest mit EINEM Durchgang
(`ScoresheetReader.ReadAsync(maxRounds: 1)` — eine Nachfrage ergäbe dieselbe Lesung). Aufruf:
`--endpoint <…/v1> --key-env SPARK_API_KEY [--model …] [--max-tokens 16384]`; der Schlüssel kommt nur aus der
genannten Umgebungsvariable, `--list-models` prüft die Verbindung. Eigene Hardware kostet 0 $ — der Deckel ist dort das
Kontextfenster des Servers, nicht das Budget. Abgelegt wird zusätzlich die Rohantwort (`NN.raw.txt` bzw.
`NN.dots.txt`).

**Quer fotografiert** (0.672.6, `Services/Scoresheet/ScoresheetOrientation.cs`, gemeldet 2026-10-05 an LeagueHub-Formular 24): aus den
Kästen der ersten Lesung — auf einem aufrechten Formular steigen die Zugnummern nach UNTEN und Schwarz steht RECHTS neben
Weiß (Mediane der Schritte n→n+1 und Weiß→Schwarz; mindestens 6 bzw. 4 Paare, beide Richtungen eindeutig, sonst 0) — folgt
je Seite die Drehung 0/90/180/270 im Uhrzeigersinn. Ist eine nötig, dreht `ScoresheetImage.Rotate` die gespeicherten Fotos
aufrecht (volle Größe, JPEG), und die Einlesung liest NOCH EINMAL (zweiter Aufruf, gleiche Kostenbremse). Übernommen wird die
zweite Lesung samt gedrehter Fotos nur, wenn sie aufgeht; sonst bleiben erste Lesung und Fotos. Kein eigener Modell-Aufruf
und keine Schemaänderung — die Referenz-Lesungen bleiben wörtlich.

**Mehrere Seiten** (0.600.0, Wunsch 2026-09-29: „2. Bild für 2. Seite von Partieformular (+ 3. Seite)"): eine lange Partie
geht über mehrere Blätter — bis `ScoresheetScanService.MaxPages` (3) Fotos in Seitenreihenfolge, hochgeladen als mehrere
Teile `file` in EINER Anfrage, gespeichert als Seite 1 = `ScoresheetScan.Photo` (bestehende Einlesungen bleiben, wie sie
waren) und Seite 2+ = `ScoresheetScanPages`, dazu `ScoresheetScan.PageCount`. Es bleibt EINE Einlesung (Tageszahl, Budget).
Gelesen wird in EINEM Aufruf mit allen Fotos (`IScoresheetVisionClient.ReadPagesAsync`, `ScoresheetReader.ReadPagesAsync`;
Claude und der OpenAI-kompatible Leser setzen „Page n:" vor jedes Foto, dots.ocr und die Test-Attrappen können nur eine
Seite — Vorgabe der Schnittstelle, Absage `multiPage`), damit das Modell sieht, wo Seite 2 weitermacht (auch mit neu
beginnender Nummerierung, ein Zugpaar kann über die Seiten gehen). **Mit EINER Seite bleiben Auftrag und Schema wörtlich
wie vorher** (die Referenz-Lesungen hängen daran, Test): erst ab zwei Seiten nennt `ScoresheetPrompt.FirstRead`/`Repair`
Seitenzahl und Maße je Seite, und `Schema(multiPage: true)` verlangt je Zug `page`. Die gespeicherte Antwort trägt dann
`pageSizes` (`ScoresheetTranscription.WithImageSize`), jeder Kasten wird in den Pixeln SEINER Seite umgerechnet
(`NormalizedBoxes`), `EntryPages()` klemmt die Seite auf 1..n. `GET …/scoresheet` liefert `pageCount` + `pages` (je Eintrag),
`GET /api/games/{id}/photo?page=n` die Seite, jede Antwort mit `X-Page-Count` (damit blättert der Foto-Dialog aus Liste,
Partieseite und Korrekturseite ohne weiteren Abruf). Korrekturseite: Seitenwahl über dem Foto, der gewählte Zug blättert
mit (`SheetEditSession.currentPage`), der Ausschnitt kommt aus der Seite des Eintrags (Maße je Seite werden beim Laden
gemessen, `setPageSize`). Löschen: Partie löschen räumt die Seiten ohne Laden ab (`RemovePagesWithoutLoading`, die
Einlesung bleibt fürs Kontingent), das Konto ebenso. **Frontend-nginx**: eigene `location ^~ /api/scoresheets` mit 64 MB
(die generische /api/-Regel deckelt auf 15 MB, drei Handyfotos kämen nicht durch; `DeploymentConfigTests` hält sie über dem
`[RequestSizeLimit]` des Uploads). **LeagueHub seit 0.690.1** (Wunsch 2026-10-06): `POST …/club/scans` und
`POST /api/league/s/{token}/club/scans` nehmen ebenfalls bis zu drei Teile `file` (`ClubUpload.ReadPagesAsync`, 64 MB, die
verschachtelte nginx-Location in `/api/` dafür auf 64M — `DeploymentConfigTests` hält sie über dem `[RequestSizeLimit]`, Codereview A6-020; eine nginx-413 kommt als HTML ohne `reason`, RookHub und LeagueHub zeigen sie als „Foto zu groß“), `GET …/scans/{id|key}/photo?page=n`; die Seite bietet nach dem ersten Foto
„+ Seite 2/3“, die Prüfseite blättert zwischen den Seiten (folgt dem gewählten Zug).

**Mehrere Fotos DERSELBEN Seite + zusammenführen** (0.736.0, Wunsch 2026-10-10: „zwei Fotos einer Partie, beide die gleiche
Seite — vereinen für bessere Erkennung"; „zwei Bilder schon analysiert, dort zusammenführen"): Tabelle `ScoresheetScanViews`
(ScanId Cascade, `Page` ab 1, `View` ab 1 — 0 ist das Hauptfoto, unique (Scan, Page, View)), höchstens `MaxPhotosPerPage` 4
je Seite. Hochladen (nur LeagueHub, angemeldet und Teilen-Link): Formularfeld `layout` = je Teil `file` seine Seite
(„1,1,2"; `ScoresheetUpload.SheetPage`, `SheetGroups` — erstes Foto je Seite = Hauptfoto; Absagen `invalidLayout`,
`tooManyViews`). `POST /api/league/club/scans/{id}/merge { otherId }` (contribute, nur angemeldet, eigene bzw. Verwalter, gleicher
Verein): die Fotos der anderen (Haupt, Seiten, weitere) werden weitere Fotos der passenden Seite (über die Seitenzahl hinaus
die letzte), die andere wird wie verworfen geschlossen (Archiv), die Ziel-Einlesung wartet wieder (`Pending`, StartedAt/Error
leer) und wird mit allen Fotos neu gelesen → neuer Stand; 400 `same`/`busy` (eine läuft)/`tooManyViews`, 404 fremd.
`LeagueScanStateDto.ViewCounts` (je Seite), `ExternalPendingScanDto.ViewCount`, `?view=` an allen drei Foto-Endpunkten (Liga,
Teilen-Link, Admin). Jeder Löschweg der Seiten räumt auch die weiteren Fotos ab (`RemoveViewsWithoutLoading`); das Archiv
nimmt sie seit 0.756.0 MIT (`ScoresheetScanArchive.View`, 0 = Hauptfoto, eindeutig je Einlesung/Seite/View): „Korrigieren"
einer Vereinspartie zeigt das zweite Exemplar wieder (`ScoresheetEditStateDto.ViewCounts`, `GET …/games/{id}/sheet/photo?page=&view=`).
Vorher fielen sie beim Übernehmen weg (Stapel 2, 12.10.: 50 Partien ohne ihr zweites Exemplar übernommen).
**Bewertungskurve beim Korrigieren** (0.756.0, Wunsch 2026-10-12): die Korrigieren-Seite holt `GET …/games/{id}/evals` und zeigt
die Kurve der GESPEICHERTEN Partie (`scan-eval-curve.ts`, `correctionCurve`) mit Punkten für Fehler/Patzer/verpasst; ein
Zickzack (verschenkt, gleich zurückgegeben) ist fast immer ein falsch gelesener Zug, ein Klick springt hin. Ab der ersten
geänderten Stelle bleibt die Kurve leer (die Analyse gehört zur gespeicherten Partie). Die Kästen der Lesung gehören immer zum Hauptfoto. Der Watcher (`~/claude/formulare-bot/bin/fetch-scan`,
außerhalb des Repos) legt sie als `img/s<id>-p<seite>-v<n>.jpg` ab; `instructions-scan.md` sagt, bei unklaren Einträgen dort
nachzusehen. Gelesen durch das Modell (`Scoresheet:Reader=model`) werden sie nicht.

**Glocke** (0.531.0): fertig gelesen → `scoresheet_read` (Daten white/black/moves/uncertain/unresolved, Link auf
`/games/{id}/edit`), gescheitert → `scoresheet_failed` (Daten reason, die Glocke übersetzt den Code über
`scoresheet.error.*`). Ein Fehler beim Benachrichtigen lässt die Einlesung nicht scheitern.

**Meine Seite** (0.531.0, `SavedGame.OwnerSide` white/black/null): selbst festgelegt schlägt `DetermineOwnerSide` die
Zuordnung über den Plattform-Namen — und weil Partieseite, Teilen-Link UND Link-Vorschaubild (`OgMetaService`) alle
über diese eine Regel gehen, dreht eine Festlegung überall. Beim Einlesen: Formularfeld `side` (white/black/auto,
`ScoresheetScan.OwnerSide`); `auto` sucht Nachname, Anzeigename, Vorname, Benutzername als GANZES Wort (ohne
Groß/klein, ohne Akzente) in den gelesenen Spielernamen — nur wenn genau EINE Seite passt
(`ScoresheetScanService.GuessOwnerSide`). Korrekturseite: `PUT /api/games/{id}` mit `ownerSide` (leer = zurücknehmen,
weglassen = unverändert), für jede Partie.

**Ausschnitt je Eintrag** (0.550.0, Wunsch 2026-09-27): das Modell liefert zu jedem Eintrag `box` = [x0, y0, x1, y1] in
PIXELN des Bildes, das es bekam (Schema-Pflichtfeld, in BEIDEN Aufträgen erklärt; ~15 Ausgabe-Tokens je Halbzug mehr,
~+0,02–0,04 $ je Formular). **Die Größe steht im Auftrag** (`ScoresheetPrompt.FirstRead`/`Repair` mit `photoSize`,
„The photo is 1500 × 2000 pixels"), und die Einlesung hängt `imageWidth`/`imageHeight` an die gespeicherte Antwort
(`ScoresheetTranscription.WithImageSize`). Warum Pixel (0.551.3): um „0..1000" gebeten, antwortete Opus 5.5 im echten
Lauf trotzdem in Pixeln des 1500×2000-Bildes (y bis 1790) — die Ausschnitte saßen alle falsch; im Einzeltest vorher hatte
es sich an 0..1000 gehalten. Die Einheit hängt also nicht verlässlich an der Bitte, die Pixel mit genannter Größe schon.
`GET /api/games/{id}/scoresheet` reicht die Kästen als `boxes` in 0..1000 durch (Index wie `written`) —
`ScoresheetTranscription.NormalizedBoxes` rechnet Pixel → Promille, verwirft Kästen, die über 3 % aus dem Bild ragen,
sortiert vertauschte Ecken, verwirft alles ohne vier Werte mit Fläche (`null`). Einlesungen von 0.550.0 ohne Bildmaße:
nur übernommen, wenn KEIN Wert über 1000 liegt, sonst alle `null`. Die Korrekturseite zeigt unter dem Foto den Eintrag des gewählten Halbzugs (`ply.w`; am Ende
der Liste den ersten unaufgelösten, `unresolvedFrom`) als Ausschnitt mit Umfeld — seitlich 60 % der Kastenbreite, oben/
unten 130 % der Höhe (`cropView` in `game-edit.util.ts`) —, rot gerahmt, wenn der Zug unsicher ist. Ausgeschnitten wird
per CSS aus dem schon geladenen Foto (Seitenverhältnis aus `naturalWidth/Height`), keine Bildbearbeitung am Server. Das
gespeicherte Foto ist meist das ORIGINAL mit EXIF-Drehung, das Modell sah es gedreht (`ScoresheetImage.Prepare`); der
Browser zeigt es ebenfalls gedreht, deshalb passen die Koordinaten ohne Umrechnung. Ältere Einlesungen und dots.ocr
haben keine Kästen → kein Ausschnitt.

**Korrekturseite**: das Brett zeigt die Stellung VOR dem gewählten Halbzug, ein Zug am Brett ersetzt ihn (oder fügt
ein), `Zug löschen` streicht ihn. Bei einer eingelesenen Partie wird danach der REST neu aufbereitet
(`POST …/scoresheet/resolve`, ohne Modell-Aufruf): Präfix = die festen Züge, `writtenFrom` = welcher Formular-Eintrag
zum nächsten Halbzug gehört (ersetzen: der nächste, einfügen: derselbe, löschen: der übernächste —
`resolveRequest` in `game-edit.util.ts`). **Ein Halbzug OHNE Eintrag (`w = null`, eingefügt) verbraucht keinen**
(0.558.1, `nextEntryAt`): ersetzen/löschen geht beim nächsten OFFENEN Eintrag weiter (nach dem letzten Halbzug mit
Eintrag), und ein ersetzter eingefügter Zug bleibt ohne Eintrag. Vorher zählte jeder Halbzug dazwischen mit — an
Prod-Partie 27 (36…Bd5 vergessen, Rb4 eingefügt) fiel beim Nachspielen von Rb4 der Eintrag „Kd7" weg und der Rest
verrutschte. Gespeichert wird über `PUT /api/games/{id}`: Züge werden nachgespielt
(illegal → 400, nichts geschrieben), nicht bearbeitete Header (Elo, Bedenkzeit, FEN) bleiben, und **ändern sich die
Züge, fallen `GameAnalysisId`, der Stand des Fehler-Trainings und die Nacherzählung weg** — sie gehörten zu einer
anderen Partie (`SavedGameCorrection.OnMovesChangedAsync`, gilt seit Codereview N8-002 auch für den längeren Re-Save aus der
Erweiterung, `POST /api/extension/games`). Der
Stand je Halbzug (bestätigt, Lesarten) geht als `scoresheetPlies` mit und liegt als `ResolutionJson` an der Einlesung.

| Methode | Endpoint | Zweck |
|---------|----------|-------|
| GET | `/api/scoresheets/status` | `{ available, dailyLimit, usedToday, nextAllowedAt, budgetUsedPercent, blocked, unlimited, languages[{ code, name, pieces }] }` — `nextAllowedAt` (UTC) nur bei erreichter Tageszahl |
| GET | `/api/scoresheets?take=` | Die letzten Einlesungen (ohne Foto) |
| POST | `/api/scoresheets` | Multipart `file` (bis zu 3 Teile = Seiten in Reihenfolge, 0.600.0) + `language` (Code oder `auto`) + `side` (white/black/auto) → 202 mit der Einlesung (`pageCount`); 400 `reason` ∈ `noFile`/`unsupportedImage`/`tooLarge`/`tooManyPages`/`dailyLimit`/`tooManyOpen`/`invalidLanguage`/`userDailyBudget`/`userMonthlyBudget`/`globalBudget`, 503 `notConfigured` |
| GET | `/api/scoresheets/{id}` | Stand: `pending`/`running`/`done`/`failed` (+ `error`), `savedGameId`, Zähler |
| DELETE | `/api/scoresheets/{id}` | Eigene Einlesung löschen (A6-016): Foto(s), Antwort des Modells und Stand gehen, die Zeile bleibt fürs Kontingent (Vermerk `∅`, nicht mehr in der Liste); eine entstandene Partie bleibt ohne Foto. 204; 404 fremd/unbekannt/Liga-Einlesung/schon gelöscht; 409 `busy` solange `pending`/`running` |
| PUT | `/api/games/{id}` | Partie korrigieren `{ moves[{ san, comment? }], white, black, result, event, site, round, date, ownerSide?, scoresheetPlies? }` (`GameCorrectionController`, eigene Klasse unter derselben Route wie `GamesController`) |
| GET | `/api/games/{id}/photo?download=&page=` | Das Formular-Foto, Seite `page` (ab 1; 404 ohne) — Header `X-Page-Count` = Zahl der Seiten |
| GET | `/api/games/{id}/scoresheet` | Formular-Einträge + Stand je Halbzug für die Korrekturseite; `pageCount` + `pages` (Seite je Eintrag) |
| POST | `/api/games/{id}/scoresheet/resolve` | Rest neu aufbereiten `{ prefix[], writtenFrom }` → `{ plies, unresolved, unresolvedFrom }` |
| GET | `/api/admin/scoresheets/pending` | messages.admin: wartende Einlesungen für den Leser von außen (0.687.0) `[{ id, purpose (own/league), userId, anonymous, pageCount, notationLanguage, ownerSide, createdAt }]`, älteste zuerst |
| GET | `/api/admin/scoresheets/{id}/photo?page=` | messages.admin: Foto einer Seite |
| POST | `/api/admin/scoresheets/{id}/reading` | messages.admin: Lesung übernehmen `{ transcription }` → die Einlesung; 400 `reason` ∈ notPending/invalidTranscription, 404 |
| POST | `/api/admin/scoresheets/{id}/start` | messages.admin: der Leser fängt an — wartend → läuft, die Seite zeigt „wird gelesen“ (0.721.2) → 204 |
| POST | `/api/admin/scoresheets/{id}/fail` | messages.admin: als gescheitert schließen `{ reason }` (unreadable/noMoves/failed, Glocke wie sonst) → 204 |

## Fristen der Fotos (A6-016, 2026-10-25)

Der tägliche Aufräumlauf (`AnonymousDataRetentionService` → `ScoresheetPhotoRetention`) leert — wie `DELETE` — Foto(s) und
Lesung, die Zeile bleibt fürs Kontingent: **gescheiterte** Einlesungen nach `Retention:ScoresheetFailedDays` (30 Tage ab
Ende), nie übernommene **Liga-Einlesungen ohne Konto** (Teilen-Link, auch nie gelesene) nach
`Retention:ScoresheetOpenAnonymousDays` (14 Tage ab Upload). Das Archiv (`ScoresheetScanArchives`) geht nach `ExpiresAt`
(365 Tage) — bisher nur beim nächsten Abschluss einer Liga-Einlesung mitgeräumt. Fotos übernommener RookHub-Einlesungen
bleiben bei ihrer Partie. Übersicht aller Fristen: [speicherdauer.md](speicherdauer.md).


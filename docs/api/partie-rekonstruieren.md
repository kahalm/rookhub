# Partie rekonstruieren (auth)

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Partie rekonstruieren (auth)“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.


Eine am Brett gespielte Partie aus Bruchstücken wieder zusammensetzen. Der Ausgangspunkt ist die
Erinnerung: die ersten Züge weiß man meist vollständig, danach nur noch einzelne Stellungen und
kurze Zugfolgen. Aufgezeichnet wird deshalb eine GEORDNETE Liste von Teilen
(`GameReconstructionPart`) — Zugfolge (SAN) oder Stellung (FEN) —, nicht eine fertige Partie.

**Ein PGN kann das nicht.** Eine Stellung ohne den Weg dorthin lässt sich darin nicht ausdrücken,
und genau diese Teile sind hier die Eingabe. Erst wenn die Lücken geschlossen sind, entsteht eine
Partie; die SUCHE danach ist ein späterer Schritt, aufgezeichnet wird zuerst.

**Die Grundannahme ist LÜCKE, nicht Anschluss** (`GameReconstructionPart.ContinuesPrevious`,
Vorgabe `false`): Bruchstücke stammen von verschiedenen Stellen der Partie — hingen sie aneinander,
wären sie EIN Teil. Nur das erste Teil hängt automatisch an der Grundstellung. Die Oberfläche setzt
den Haken beim Anlegen genau dann vor, wenn das neue Zug-Teil auf eine STELLUNG folgt („in dieser
Stellung ging es so weiter") — nach einer Zugfolge nicht.

**Geprüft wird serverseitig** (`Services/Games/ReconstructionChain.cs`, reine Funktion über Gera.Chess,
ohne DB und Engine): je Teil `Anchored` (ist die Stellung davor bekannt?), `Valid`, `StartFen`,
`EndFen`, `PlyCount`, `StartPly` und der erste nicht spielbare Zug. Zwei Regeln hängen daran:

* **Ein Bruchstück ohne Anschluss ist NICHT falsch.** Ohne die Stellung davor lässt sich eine
  Zugfolge weder bestätigen noch widerlegen — und diese Stellung ist ja das Gesuchte. Solche Teile
  tragen `Anchored = false` und sonst keinen Vorwurf.
* **Ein behaupteter Anschluss, der nicht passt, wird gemeldet** (`Mismatch`): sagt ein Stellungs-Teil
  „direkt danach", stimmt aber nicht mit der Stellung nach dem vorigen Teil überein, ist das ein
  Widerspruch in den Erinnerungen — genau die Auskunft, die beim Rekonstruieren weiterhilft.

`StartPly` gibt es nur, solange die Kette lückenlos an der Grundstellung hängt: eine Zugnummer nach
einer Lücke wäre geraten. `KnownPlies`/`PrefixSan` sind derselbe Gedanke für die ganze Partie — was
schon sicher steht.

Jede schreibende Operation antwortet mit der GANZEN Rekonstruktion: ein eingefügtes Teil ändert
Gültigkeit und Nummern aller folgenden, ein DTO nur des geänderten Teils wäre danach mehrfach falsch.
Deckel: `MaxPerUser` 50, `MaxParts` 200, Zugtext 4000 Zeichen.

| Methode | Endpoint | Zweck |
|---------|----------|-------|
| GET | `/api/reconstructions` | Eigene Rekonstruktionen (zuletzt geänderte zuerst) mit Teilezahl, gesicherten Halbzügen und Lücken |
| POST | `/api/reconstructions` | Anlegen `{ title, white?, black?, event?, playedOn?, result?, note? }`; 400 `reason: too-many` am Deckel |
| GET | `/api/reconstructions/{id}` | Detail: alle Teile samt Auswertung + `prefixSan` |
| PUT | `/api/reconstructions/{id}` | Kopfdaten ändern |
| DELETE | `/api/reconstructions/{id}` | Löschen (mit allen Teilen) |
| POST | `/api/reconstructions/{id}/parts` | Teil anhängen `{ kind (0 = Züge, 1 = Stellung), moves?, fen?, fromPly?, continuesPrevious?, note? }`; 400 `reason` ∈ `no-moves`/`invalid-fen`/`too-many-parts` |
| PUT | `/api/reconstructions/{id}/parts/{partId}` | Teil ändern (gleicher Rumpf) |
| DELETE | `/api/reconstructions/{id}/parts/{partId}` | Teil löschen (Reihenfolge wird geschlossen) |
| PUT | `/api/reconstructions/{id}/parts/order` | Reihenfolge setzen `{ partIds: [] }` — fehlende Ids bleiben hinten, damit eine unvollständige Liste nichts verschwinden lässt (Literal-Route VOR `{partId}`) |
| POST | `/api/reconstructions/{id}/parts/{partId}/gap/propose` | **Lücke schließen**: sucht die Wege und setzt sie als VORSCHLÄGE (`Generated`) vor das Teil `{ maxPlies? }` → `{ reason, nodes, budgetExhausted, inserted, detail }`. Ersetzt die Vorschläge derselben Lücke. Rate-Limit `reconstruction-gap` (6/min je Konto, gemeinsam mit `gap`); 429 `reason: busy`, wenn gerade alle Suchplätze belegt sind — die alten Vorschläge bleiben dann |
| POST | `/api/reconstructions/{id}/parts/{partId}/gap/discard` | Vorschläge dieser Lücke verwerfen (idempotent) |
| POST | `/api/reconstructions/{id}/parts/{partId}/gap/waypoint` | Eine Stellung aus einem Vorschlag übernehmen `{ fen, certain? }` — sie kommt als eigenes Teil davor, die Vorschläge fallen weg, die Lücke zerfällt in zwei |
| POST | `/api/reconstructions/{id}/parts/{partId}/gap` | **Lücke schließen**: sucht die Züge von der Stellung am Ende des vorigen Teils bis zu diesem Teil `{ maxPlies? }` → `{ fromFen, toFen, maxPlies, nodes, budgetExhausted, reason, solutions[] }`. 200 auch ohne Weg — „es gibt keinen Weg" ist eine Auskunft, kein Fehler des Aufrufers; nur 429 bei Rate-Limit (`reconstruction-gap`) oder `reason: busy` (alle Suchplätze belegt) |
| POST | `/api/reconstructions/{id}/parts/{partId}/gap/apply` | Einen gefundenen Weg übernehmen `{ moves }` — die Züge kommen als eigenes Teil VOR `partId`, beide schließen danach nahtlos an. 400 `reason` ∈ `does-not-fit`/`no-gap`/`no-previous`/`no-anchor`/`no-moves`/`target-not-a-position` |
| POST | `/api/reconstructions/{id}/share` | **Ganze Partie teilen**: öffentlichen Link einschalten (idempotent) → `{ shareToken }` |
| DELETE | `/api/reconstructions/{id}/share` | Link abschalten — ein späteres Teilen erzeugt ein ANDERES Token |
| GET | `/api/reconstructions/shared/{token}` | **Ohne Anmeldung**: die ganze Partie hinter dem Link (Kopfdaten, alle aufgezeichneten Teile in Reihenfolge, `knownPlies`/`gaps`/`prefixSan`) |

**„Ganze Partie teilen"** (`/r/{token}`, Knopf auf der Detailseite): geteilt wird die
REKONSTRUKTION, nicht eine Kopie — wer den Link öffnet, sieht den Stand von jetzt, samt der
Lücken, die noch offen sind. Genau darum verschickt man ihn („so weit habe ich die Partie,
erkennst du den Rest wieder?"). Drei Entscheidungen hängen daran:

* **Kein PGN.** Die öffentliche Seite zeigt dieselben Teile wie der Besitzer — Züge, erinnerte
  Stellungen, Lücken dazwischen — und nicht eine Zugliste, die die Lücken verschweigen müsste.
  Der Betrachter blättert mit ← → oder per Klick durch die Stellungen (`buildRows` in
  `shared-reconstruction.component.ts` spielt die Züge mit chess.js nach; eine Lücke ist eine
  Zeile ohne Stellung und damit kein Halt beim Blättern).
* **Vorschläge der Lückensuche bleiben draußen** (`Generated`): sie sind Arbeitsstand des
  Besitzers, keine Aussage über die Partie.
* **Ein vergebenes Token bleibt** (Teilen ist idempotent), damit ein schon verschickter Link
  gültig bleibt; das Abschalten ist der Widerruf und erzeugt beim nächsten Mal ein neues.

**Die Lücke schließen ist eine SUCHE, kein Raten** (`Services/Games/GapSolver.cs`). Gesucht wird das
klassische Beweispartie-Problem: welche Halbzüge führen von der Stellung am Ende des vorigen Teils
zu der erinnerten Stellung? Der Baum wächst mit ~30 Zügen je Halbzug, deshalb vier Dinge:
iterative Vertiefung (die KÜRZESTE Erklärung zuerst — ist eine Tiefe fündig, hört die Suche auf),
eine zulässige untere Schranke (`MinPlies`), Zugsortierung (`Ordered`: zuerst die Züge, die eine
Figur auf ihr Zielfeld stellen) und ein Gedächtnis für ausgeschöpfte Sackgassen.
`MaxSearchPlies` = 12, Vorgabe 4, Knoten-Budget 500 000, Zeitbudget 4 s (beide meldet die Antwort
als `budgetExhausted` samt `deepestSearched` — „so weit kam ich" ist eine andere Aussage als
„es gibt keinen Weg").

**Die Suche ist reine CPU im Request-Thread — deshalb gedeckelt** (Codereview 2026-09-29, N5-001; vorher
3 Mio. Knoten/12 s und nur der globale 100/min-Deckel je Adresse, ein frei registriertes Konto hielt so rund
hundert Suchen gleichzeitig am Laufen): Rate-Limit `reconstruction-gap` (6/min je Konto) auf `gap` und
`gap/propose`; höchstens `GapSearchGate.DefaultSlots` = 3 Suchen gleichzeitig im ganzen Prozess, sonst SOFORT
429 `reason: busy` (kein Warten, keine Schlange im Thread-Pool); Abbruch der Anfrage oder Herunterfahren
beendet die Suche wie ein erschöpftes Budget; das Sackgassen-Gedächtnis hört bei `MaxDeadEntries` = 200 000
auf zu wachsen. Gemessen (~20 000 Knoten/s): sechs Halbzüge ~1 s, Spanisch über acht 2,2 s; eine
Sizilianisch-Stellung nach acht Halbzügen lief selbst mit 30 s ins Budget — die Antwort sagt dann `budget`.

**Die Schranke ist die halbe Miete** (`MinPlies`): je Seite das MAXIMUM aus Schlagfällen
(verschwundene Figuren der Gegenseite), Umwandlungen und **Verschiebung** — für jede Figur der
ZIELstellung die billigste passende Ausgangsfigur (Springer per Distanztabelle, Läufer 1 oder 2,
Turm/Dame 1 oder 2, König Chebyshev, Bauern nur vorwärts, seitwärts nur per Schlag, Doppelschritt
aus der Grundreihe als EIN Zug, Umwandlung über den Bauernmarsch), dazu die feste Parität. Die
Rochade bewegt zwei Figuren in einem Zug und wird deshalb mit bis zu 3 abgezogen. Vorher stand
dort „veränderte Felder ÷ 4" — bei einer Stellung acht Halbzüge später sagte das „mindestens
zwei", also praktisch nichts, und die Suche erstickte ab sechs Halbzügen im Budget. **Wer hier
etwas ändert, prüft die ZULÄSSIGKEIT**: `MinPlies_NeverAsksForMoreMovesThanWereActuallyPlayed`
spielt zufällige Zugfolgen und besteht darauf, dass die Schranke nie mehr verlangt, als gespielt
wurde — eine zu große Schranke verwirft echte Lösungen lautlos (so gefunden: der Doppelschritt des
Bauern zählte als zwei Züge).

Vier Regeln, die dabei nicht kippen dürfen:

* **„Nicht gefunden" ist nicht „gibt es nicht".** Bricht die Suche am Budget ab, sagt die Antwort
  das (`budgetExhausted`), und die Oberfläche schreibt einen anderen Satz. Ein Ergebnis, das beides
  gleich behandelt, verleitet dazu, eine richtige Erinnerung zu verwerfen.
* **Das en-passant-Feld zählt nur, wenn BEIDE Seiten eines nennen** (`GapSolver.Matches`). Der
  Stellungs-Editor schreibt dort immer „-" (er kennt den Zug davor nicht), die Zug-Erzeugung setzt
  nach jedem Doppelschritt eines. Verlangte man Gleichheit, fiele die häufigste Erinnerung durch
  („und dann ging der Bauer nach e5" — jeder Weg, dessen letzter Halbzug ein Doppelschritt ist).
* **Das Ziel ist immer ein STELLUNGS-Teil.** Eine Zugfolge nach einer Lücke hat selbst keine
  bekannte Ausgangsstellung — und genau die wäre das Ziel der Suche.
* **Beim Übernehmen wird NOCH EINMAL geprüft** (spielbar ab der Stellung davor, endet auf der
  Zielstellung): die Züge kommen aus einer Antwort, aber ankommen tut ein Request. Passt es nicht,
  entsteht gar kein Teil. Das eingesetzte Teil trägt `Certain = false` — mehrere Wege enden in
  derselben Stellung, welcher gespielt wurde, weiß nur der Mensch.

**Gefundene Wege stehen als VORSCHLÄGE in der Liste** (`GameReconstructionPart.Generated`, seit
0.493.0): „Lücke schließen" sucht und setzt die Wege dorthin, wo die Lücke ist —
`POST …/parts/{partId}/gap/propose` (ersetzt die Vorschläge derselben Lücke),
`…/gap/discard` wirft sie weg, `…/gap/waypoint` macht eine Stellung DARAUS zu einem eigenen Teil
(„die stimmt" bzw. ihre korrigierte Fassung) und teilt die Lücke damit in zwei kleinere.
`ReconstructionChain.Analyze` ÜBERSPRINGT Vorschläge: sie stehen in der Liste, aber die Lücke bleibt
offen und die „gesicherten Halbzüge" wachsen nicht — sonst behauptete die Rekonstruktion etwas,
das nur geraten ist. Ein Teil, das ein Mensch bearbeitet, verliert die Marke (`UpdatePartAsync`);
`AddPartAsync` kann mit `InsertBeforePartId` an beliebiger Stelle einsetzen. Die Oberfläche blendet
Vorschläge auf Wunsch aus, öffnet den ersten gleich zum Durchklicken und bietet dort „Stellung
stimmt" / „Stellung korrigieren" / „ganze Linie übernehmen".

**Sicher oder unsicher** (`GameReconstructionPart.Certain`, Vorgabe `true`): je Bruchstück ein
Haken. Wer etwas aufschreibt, meint es zunächst — die Auskunft, auf die es ankommt, ist das
Gegenteil („hier bin ich mir nicht sicher"), und die steht am TEIL statt in einer Notiz, weil eine
unsichere Stellung der erste Kandidat für einen zweiten Blick ist, wenn die Lücke daneben nicht
aufgeht. Im Request ist das Feld NULLBAR: ein Client, der die Frage nicht kennt, darf nicht für den
Nutzer „unsicher" behaupten (und die Migration trägt für den Bestand `true` nach).

**Wer am Zug ist, sagt notfalls der Mensch** (`GameReconstructionPart.BlackToMove`): bei einer
Stellung steht die Seite in der FEN, bei einem anschließenden Teil in der Stellung davor — bei
einem FREIEN Bruchstück gibt es keine Quelle dafür, und ohne die Angabe ließe sich „und dann schlug
ER auf f7" gar nicht eingeben (das Brett stünde auf Weiß). Der Umschalter im Editor löst ein Teil
deshalb vom vorigen, wenn die gewählte Seite der Stellung davor widerspricht. Beim ERSTEN Teil
heißt der Haken: das ist nicht die Eröffnung — `ReconstructionChain` verankert es dann nicht an der
Grundstellung, sondern behandelt es wie jedes andere freie Bruchstück.

**Aufgezeichnet wird im FLUSS** (`reconstruct-detail.component.ts`; Zug-Editor `part-editor.component.ts`, Lückenzeile
`gap-proposals.component.ts`, reine Zug-/Lücken-Hilfen `reconstruct-moves.util.ts` — Zerlegung Z25): eine leere Rekonstruktion
öffnet gleich den Zug-Editor, „Stellung eingeben" speichert das Getippte und führt in den
Stellungs-Editor, und eine übernommene Stellung öffnet wieder den Zug-Editor (mit Anschluss an sie).
Ein LEERER Zug-Editor wechselt dabei nur die Art und legt nichts an — das ist der Weg „direkt zur
nächsten Stellung". So entsteht Züge → Stellung → Züge → Stellung, ohne nach jedem Teil zurück ins
Menü zu gehen. Der Sicher-Haken lässt sich an jedem gespeicherten Teil mit einem Klick umlegen
(`toggleCertain`), weil einem beim Aufschreiben erst später auffällt, dass ein früheres Bruchstück
wackelt.

Menü-Key `reconstruct` (Stufe `Registered`), Frontend `/reconstruct` (Liste) und
`/reconstruct/:id` (Arbeitsplatz: Teile links, Brett rechts). Die Züge werden im Browser
mitgespielt (chess.js), damit man beim Tippen und Klicken sofort sieht, wo man steht — die
verbindliche Auskunft kommt trotzdem vom Server.

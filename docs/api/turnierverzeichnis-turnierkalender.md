# Turnierverzeichnis / Turnierkalender (lesen ohne Anmeldung, schreiben mit Konto)

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Turnierverzeichnis / Turnierkalender (lesen ohne Anmeldung, schreiben mit Konto)“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.

Gefuellt vom naechtlichen Sweep der chess-results-Turniersuche (`TournamentDirectoryScheduler`,
03:00 UTC; Nachbarlaender taeglich, uebrige Foederationen rotierend). Rein lesend — hier wird
nichts gecrawlt.

**Aufbau des Sweeps** (Zerlegung Z8 + zweite Runde W7b, 2026-10-26): `TournamentDirectoryService` = Ablauf je
Foederation (Abruf, Sweep-Zeile, Speichern, Meldungen, Log); der Abgleich der Trefferliste mit dem Bestand (bekannte
Eintraege laden, Zeilen uebertragen/anlegen, Verschwundene zaehlen) steht in `DirectorySweepMerge`, die vier
Turnierart-Durchgaenge in `DirectoryArtPasses`, Schluessel/Hash in `DirectoryKeys`, Meldungen in `DirectoryNotifier`.
Die Admin-Endpunkte liegen in `AdminTournamentDirectoryController` (Partial: Wartung + FIDE/chess-results in der
Hauptdatei, die Verbandskalender-Ausloeser in `AdminTournamentDirectoryController.Federations.cs`).

**Ohne Anmeldung** (seit 0.643.0): Suche, Karte, Kalender, Einzelturnier und die beiden Orts-Endpunkte sind
`[AllowAnonymous]` mit der Policy `directory-read` (`RateLimitPartitions.DirectoryRead`, 120/min je Konto bzw. IP,
mal `RateLimitScale`). Was am NUTZER haengt, faellt fuer Gaeste still weg statt zu scheitern: keine ausgeblendeten und
keine gemerkten Turniere (`IgnoredIdsAsync`/`SubscribedIdsAsync` liefern leer — `GetUserId()` wirft ohne Anmeldung und
waere ein 500), kein `ForUserId`. Ein `profileId` ohne Anmeldung ist ein **400**, nie das Profil eines anderen.
Ausblenden, Melden, „Mein Turnier fehlt" und die Suchprofile bleiben `[Authorize]`
(`PersonalActions_StillRequireAnAccount_ReadsDoNot` prueft die Attribute).

> **Abschaltbar: `TournamentDirectory:Enabled=false`** (`TOURNAMENT_DIRECTORY_ENABLED=false`,
> Vorgabe an). Dann laeuft weder der naechtliche Durchgang noch der Aufhol-Lauf nach einem
> Neustart — die `/api/admin/tournament-directory/...`-Endpunkte bleiben aber vollstaendig
> bedienbar. Dasselbe fuer die Spielerkarten des Turnierverlaufs ueber
> `PlayerHistory:Enabled=false` (04:30 UTC). **Auf DEV seit 2026-09-10 beides abgeschaltet**: dort
> stoert ein Sweep zur Unzeit die Messung, an der gerade jemand arbeitet, und die Laeufe werden
> ohnehin von Hand angestossen. Prod bleibt unveraendert an.

**Verortung (`GeocodingService`, ueberarbeitet in 0.418.0).** Der Spielort ist Freitext und nennt
bei Ligen MEHRERE Orte. Die Reihenfolge:

1. **Postleitzahl** — auf wenige Kilometer genau. Mehrere weit auseinanderliegende PLZ = mehrere
   Spielorte. Dieser Weg laeuft VOR der Zerlegung, weil Adressen dieselben Trennzeichen benutzen
   („Halle 1, Eichetstrasse 29, 5020 Salzburg" ist EIN Ort, nicht drei). Der Ortsname des Treffers
   muss im Text vorkommen — sonst gewinnt eine Hausnummer, die wie eine PLZ aussieht. Als
   Bestaetigung zaehlt auch ein WORTANFANG ab 4 Zeichen: chess-results schreibt „9300 St.Veit",
   im Lexikon steht „St. Veit an der Glan".
2. **Ortsnamen je Abschnitt** (`GeoTextNormalizer.VenueSegments`, Trenner `, ; / und &`). Ohne die
   Zerlegung bildete die Kandidatenerzeugung Wortfolgen ueber das Komma hinweg, und weil laengere
   Wortfolgen kuerzere schlagen, gewann in „Mayrhofen, St.Veit" das „st veit" — der Pin sass
   250 km entfernt in Tirol, obwohl der erste Ort im Text Mayrhofen ist.

   **Adresse oder Ortsliste? Eine ZIFFER im Text entscheidet das** (0.419.2). Eine Liste von
   Spielorten nennt Ortsnamen („Mayrhofen / St. Veit/Glan", „Bad Haering/Schwaz/Jenbach/Absam/
   Kufstein"), eine Adresse hat eine Hausnummer. Am Dev-Stand nachgemessen: von 262 als mehrortig
   erkannten Eintraegen hatten **191 eine Ziffer und waren durchweg Adressen**, die die Zerlegung
   zerschnitten hat („Festsaal der Gemeinde Schwarzach, Marktplatz 4, Schwarzach" wurde zu ZWEI
   Spielorten desselben Ortes). Am haertesten traf es BRA und ARG — die zwei groessten
   Foederationen im Bestand, fuer die keine Postleitzahlen importiert sind, sodass der PLZ-Weg
   dort nie greift. Mit Ziffer gilt deshalb der LETZTE Treffer als der EINE Spielort: vorn stehen
   Gebaeude und Strasse, der Ort weiter hinten.
3. **Regionsmitte** aus der Bundesland-Spalte.

**Vereinsnamen als letzter Entscheider (`VenueDisambiguationService`, 0.419.0).** Es gibt einen
Fall, den keine Namensregel loesen kann: die Abkuerzung im Ortstext ist ZUFAELLIG exakt der Name
eines anderen Ortes. Nachgestellt an tnr1405166 — Ortstext „Mayrhofen, St.Veit", im Lexikon heisst
genau ein Eintrag „St. Veit" und der liegt in TIROL; gemeint ist laut Ausschreibung „St. Veit an
der Glan" in Kaernten, 250 km entfernt. Das sieht nicht einmal mehrdeutig aus. Die Vereinsliste des
Turniers nennt dagegen „SV ASKOE St. Veit/Glan".

Der Dienst erweitert die Kandidaten deshalb auf laengere Namen (alles, was mit dem gesuchten
beginnt) und setzt einen Pin **nur mit Beleg**: ein Vereinsname muss ein UNTERSCHEIDENDES Wort des
laengeren Namens als GANZES Wort enthalten („glan" — nicht als Teil von „Glanegg"). Fuellwoerter
(an, der, sankt, bad, neu …) zaehlen nicht, Gleichstand zwischen verschiedenen Orten entscheidet
nichts. Ohne Beleg bleibt alles, wie es war. Ergebnis: `GeoSource.TeamHint`.

**Ohne Pin zuerst**: der Topf enthaelt Eintraege ganz ohne Koordinaten (dort ist alles zu gewinnen) und schon
gepinnte, bei denen der Vereinsname nur KORRIGIERT — am 2026-09-09 auf Dev 153 gegen 919. Allein nach Termin
sortiert kamen die 153 verstreut dran, bei 50 Abrufen je Nacht also ueber Wochen.

Kosten: EIN Seitenabruf je Turnier (Crawler-Endpunkt `GET /api/tournament-search/teams?id=`,
zustandslos). Deshalb gedeckelt — `TournamentDirectory:DisambiguationBatchSize` (Vorgabe 50, 0 =
aus) je Nacht nach dem Sweep, und `TournamentDirectoryEntry.TeamHintCheckedAt` verhindert, dass
dieselben Seiten wieder geholt werden. Von Hand: `POST /api/admin/tournament-directory/disambiguate?limit=`.

**Mehrdeutige Namen bekommen KEINEN Pin.** Liegen die gleichnamigen Kandidaten weniger als 5 km
auseinander (24 Wiener PLZ), ist die Wahl gleichgueltig und die Einwohnerzahl entscheidet wie
bisher. Sonst entscheidet die **Turnierdichte**: wie viele Turniere mit VERLAESSLICHER Verortung
(PLZ oder von Hand) liegen im Umkreis von 12 km? Wo ein Schachklub Turniere austraegt, stehen
mehrere. Bewusst nur verlaessliche Pins — zaehlte man alle mit, bestaetigten die falschen Pins sich
selbst (an „Baernbach" nachgestellt: alle Pins waehlen den falschen Ort, verlaessliche den
richtigen). Bleibt es unentschieden, gibt es keine Koordinaten und `GeoSource.Ambiguous` als
Vermerk fuer die Arbeitsliste. Gemessen am Dev-Stand vor der Aenderung: 171 Eintraege mit
Kandidaten > 50 km auseinander, davon 29 mit nachweislich falschem Pin (bis 489 km, „Muenster"
gibt es 19-mal).

**Gruppen eines Turniers = EINE Karte** (`TournamentDirectoryEntry.GroupKey`, `DirectoryKeys.ComputeGroupKey`):
Hash aus Basisname, Föderation, Beginn, Ende und ORT. Der Ort ist seit UI-Sweep 2026-10-11 (r2-t-dup-cards) der VERORTETE
(`GroupPlace`: normalisierter `GeoPlaceName` bei PLZ/Ort/von Hand/Nominatim/Vereinsnamen/Quelle), sonst der normalisierte
Ortstext (Region, mehrdeutig, unverortet) — vorher der rohe Text, und „Otáhalova 1260 Varnsdorf" gegen „Varnsdorf Otáhalova 1260"
waren zwei Karten (Prod 11.10.: 65 Turniere). Weil die Verortung bei den Zusatzquellen NACH der Klassifizierung läuft, gleicht
`TournamentGroupingBackfillService.RegroupAsync` JEDEN Schlüssel mit der aktuellen Rechnung ab (schmale Projektion, schreibt nur
abweichende) — beim Start und als letzter Schritt des Nachtlaufs („Gruppenschluessel-Abgleich"). Keine Fassungs-Spalte: eine
Regeländerung zieht der nächste Start von selbst nach. Was dann noch getrennt steht, markiert die Oberfläche als „weitere Gruppe?"
(`sameNameSiblings`).

| Methode | Endpoint | Zweck |
|---------|----------|-------|
| GET | `/api/tournament-directory?from&to&lat&lon&radiusKm&fed&speed&q&weekendOnly&minPlayers&profileId&kinds&ageGroups&genders&adultsOnly&hideLeagues&includeImplausible&page&pageSize` | Turnierliste; Umkreis via Bounding-Box (SQL) + Haversine (C#). `q` sucht in Name, Ort UND Veranstalter. Die fuenf Publikums-/Formatfilter binden als EIN Objekt (`DirectoryAudienceQuery`) und gelten fuer Liste, Karte und Kalender gleich: `kinds` individual/team/unknown — **„individual" schliesst `unknown` MIT ein**: die Quelle sagt nur „ist MANNSCHAFTS-Turnierart", alles andere ist Einzel, und `unknown` heisst „Abfrage lief hier noch nicht / fiel aus" (kein eigener Fall fuer den Suchenden; als eigener gefuehrt lieferte „Einzel" eine halb leere Liste). In der SPALTE bleibt `Unknown` stehen, damit ein Netzausfall den Bestand nicht auf Einzel umschreibt, `ageGroups` u8…u20/youthUnspecified/senior (**Ueberschneidung**, nicht Gleichheit — „u12" findet die U8-U18-Meisterschaft), `genders` open/female/male, `adultsOnly` (kein JUGENDmerkmal; Senioren bleiben sichtbar), `hideLeagues`. Ein unbekannter Wert ist ein **400**, kein stilles Ignorieren. **Reihenfolge mit `from` (UX-039):** erst was im Zeitraum BEGINNT (nach Start), dann als Block was schon laeuft (`ongoing: true`), zuletzt Eintraege ohne Beginn; letzter Schluessel die Id (Seiten ueberschneidungsfrei), gilt auch im Umkreis. **Unplausible Laufzeiten** (> 1 Jahr UND ohne Spieltermine oder schon > 1 Jahr vor `from` begonnen, `TournamentDirectoryQueryService.Plausible`) sind in Liste, Karte und Kalender standardmaessig AUSGEBLENDET; `includeImplausible=true` zeigt sie mit `implausible: true`. Die Detailseite filtert nicht |
| GET | `/api/tournament-directory/map?bbox=minLat,minLon,maxLat,maxLon` | Kartenmarker im Ausschnitt (gedeckelt, `limit` Vorgabe 2000, höchstens 5000 Zeilen); Antwort `{ items, truncated }` — `truncated` = es fehlen die spätesten Turniere (Codereview F6-006, VERTRAGSÄNDERUNG: vorher eine nackte Liste) |
| GET | `/api/tournament-directory/calendar?year&month` | Ein Monat: `tournaments` (jedes Turnier EINMAL) + `days` (je Tag nur die Nummern der laufenden). Mehrtaegige Turniere stehen an JEDEM ihrer Tage — voll ausgeschrieben waren das 5962 Eintraege fuer 200 Turniere, also ~3 MB je Monat; das Frontend setzt es in `expandCalendar` wieder zusammen. Höchstens 5000 Turniere je Monat (`CalendarMaxTournaments`), darüber `truncated`; Einträge ohne jeden Termin fehlen (Codereview A5-002 — vorher still bei 200 gekappt) |
| GET | `/api/tournament-directory/{publicId}` | Einzelnes Turnier (auch abgesagte). `publicId` ist die IDENTITAET; erlaubte Formen (chess-results-Nummer, `f<FIDE-Nummer>`, Kürzel der Verbandsquellen …) stehen an EINER Stelle in `Services/TournamentDirectory/DirectoryPublicId.cs` — dieselbe Prüfung für Detailseite, Ausblenden und Melden (Codereview A5-003) |
| GET | `/api/tournament-directory/places?q=` | Ortsvorschlaege aus dem Gazetteer (PLZ oder Name) |
| GET | `/api/tournament-directory/places/nearest?lat&lon` | Naechstgelegener Gazetteer-Ort zu Koordinaten — fuer das Ortsfeld, wenn der BROWSER den Standort liefert (die Koordinaten des Nutzers verlassen den Server nicht). 204, wenn im Umkreis von 200 km kein Ort im Lexikon liegt |
| POST | `/api/tournament-directory/{publicId}/report` | „Falsches Event melden" — Rueckmeldung zu einem Eintrag `{ message?, location?, kind?, ageGroups?, gender?, speed?, isLeague?, namePattern?, sourceLink? }`, ALLE Felder freiwillig. Landet im bestehenden **Admin-Nachrichtenkanal** (`AdminMessageService.SendFromUserAsync`) statt in einer eigenen Tabelle: dort gibt es Oberflaeche, Glocke und — entscheidend — einen Rueckweg zum Melder. `namePattern` ist die Lern-Frage („bei uns heissen die Jugendturniere Schachrallye") und wandert in die Wortlisten des `TournamentClassifier`. Rate-Limit `user-message` (gemeinsamer Topf mit `POST /api/messages/reply`, Codereview F5-001) |
| POST/DELETE | `/api/tournament-directory/{publicId}/ignore` | Ein Turnier FUER MICH ausblenden bzw. wieder zeigen (idempotent). Es verschwindet aus Liste, Karte und Kalender — und aus der naechtlichen Umkreis-Meldung; nur mit `audience.includeIgnored=true` kommt es mit (und traegt dann `ignored: true`). Die DETAILseite zeigt es immer, dorthin ist man absichtlich gegangen |
| POST | `/api/tournament-directory/suggest-source` | „Mein Turnier fehlt" `{ link, message? }` — Hinweis auf eine noch nicht gecrawlte Quelle. Der **Link ist Pflicht** (nur absolutes http/https): ein Verbandskalender laesst sich zusaetzlich auswerten, eine Aufzaehlung im Freitext nicht. Rate-Limit `user-message` wie „Melden" |
| GET/POST/PUT/DELETE | `/api/tournament-search-profiles[/{id}]` | Gespeicherte Umkreise; steuern Ansicht UND naechtliche Meldung |
| GET | `/api/admin/tournament-directory/status` | Sweep-Zustand je Foederation + Geocoding-Quote |
| POST | `/api/admin/tournament-directory/sweep` | Sweep fuer 1–20 Foederationen sofort ausfuehren |
| POST | `/api/admin/tournament-directory/gazetteer/postal/{iso2}` | GeoNames-PLZ eines Landes importieren |
| POST | `/api/admin/tournament-directory/gazetteer/cities` | GeoNames-Ortsliste (cities15000) importieren |
| POST | `/api/admin/tournament-directory/gazetteer/transcribe` | **Einmalig nach dem Deploy von 0.456.0**: rechnet die zweite Schreibweise (`GeoPlace.NameTranscribed`) fuer den vorhandenen Bestand nach. Rein lokal, kein Netzabruf; mehrfach ausfuehrbar |
| GET | `/api/admin/tournament-directory/ungeocoded` | Eintraege ohne Koordinaten (Arbeitsliste) |
| POST | `/api/admin/tournament-directory/geocode-missing?limit=&force=` | Nicht verortete Eintraege erneut aufloesen — **ohne `limit` den GANZEN Bestand**: der Lauf braucht kein Netz (lokales Lexikon, 6598 Eintraege in 14 s), und ein Deckel liefert KEINE zweite Portion (die Auswahl hat keine Fortschrittsmarke, ein zweiter Aufruf sieht wieder dieselben ersten N — am 2026-09-09 live erlebt). **`force=true`** nimmt auch schon verortete vor — gebraucht, wenn sich die REGELN aendern (der Sweep verortet einen bestehenden Eintrag nur bei geaendertem Ortstext neu, ein Pin aus einer alten Regel bliebe sonst fuer immer). Entfernt dabei Pins, die nach der neuen Regel Rateentscheidungen sind; `GeoSource=Manual`, `SourceProvided` und `TeamHint` bleiben in jedem Fall unberuehrt — der Vereinsnamen-Beleg ist eine Auskunft, die die Namensregel nicht reproduzieren kann |
| POST | `/api/admin/tournament-directory/backfill-sources` | Herkunftsvermerk fuer den Altbestand nachtragen (jeder bestehende Eintrag stammt aus chess-results). Braucht kein Netz; der Sweep tut es von selbst, aber erst nach einer Rotationswoche |
| POST | `/api/admin/tournament-directory/round-plans?limit=&retryEmpty=` | SPIELTERMINE langlaufender Turniere nachtragen — ein Seitenabruf je Turnier (chess-results art=14), gedeckelt. Siehe unten |
| POST | `/api/admin/tournament-directory/fide?years=` | Den FIDE-Kalender sofort lesen (Vorgabe: laufendes Jahr + 2). Ein Seitenabruf je Jahr; neue Ereignisse kommen mit `ChessResultsId = null` dazu, erkannte werden mit dem bestehenden Eintrag verschmolzen |
| POST | `/api/admin/tournament-directory/fsi?months=` | Den Kalender des ITALIENISCHEN Verbands lesen (ein Abruf, alle Felder inline). Siehe „Zusatzquellen" unten |
| POST | `/api/admin/tournament-directory/szs` | Den Kalender des SLOWENISCHEN Verbands lesen (vier Seitenabrufe, PLZ steht in der Liste) |
| POST | `/api/admin/tournament-directory/icu?details=` | IRLAND — 81 kuenftige, 94 % neu; 30 bringen Koordinaten aus der Trefferseite mit |
| POST | `/api/admin/tournament-directory/ffe?months=&details=` | FRANKREICH — 12 Monatsseiten; die Turnierseite je Turnier einmal (Enddatum, Bedenkzeit, PLZ gibt es nur dort) |
| POST | `/api/admin/tournament-directory/sjakk?details=` | NORWEGEN — chess-results kennt fuer NOR null; nur ~25 % verortbar |
| POST | `/api/admin/tournament-directory/chess-scotland?details=` | SCHOTTLAND — 44 bis 2028, Bedenkzeit-Klasse strukturiert; Ort nur bei ~19 % |
| POST | `/api/admin/tournament-directory/frsah` | RUMAENIEN — EIN Abruf, 31 kuenftige samt der Mannschaftsligen |
| POST | `/api/admin/tournament-directory/wcu` | WALES — EIN Abruf, 38 Turniere, 30 mit Postleitzahl |
| POST | `/api/admin/tournament-directory/knsb` | NIEDERLANDE — 177 kuenftige in zwei Abrufen; OHNE Spielort (siehe unten) |
| POST | `/api/admin/tournament-directory/ecf` | Den Kalender des ENGLISCHEN Verbands lesen — 256 kuenftige Turniere, als einzige Quelle MIT Koordinaten (`Located` in der Antwort) |
| POST | `/api/admin/tournament-directory/schachbund` | Die Turnierdatenbank des DEUTSCHEN Schachbunds lesen — zwei Abrufe je Region mit der Wartezeit aus ihrer robots.txt, dauert einige Minuten |
| POST | `/api/admin/tournament-directory/chess-arbiter?details=` | Den Kalender des POLNISCHEN Verbands lesen — 611 kuenftige Turniere in einem Abruf; `details` deckelt die Detailseiten (ein Abruf je noch unbekanntem Turnier) |
| POST | `/api/admin/tournament-directory/cfc` | Den Ankuendigungskalender der CHESS FEDERATION OF CANADA lesen — 171 kuenftige gegen 68 auf chess-results, zwei Abrufe |
| POST | `/api/admin/tournament-directory/chess-cz` | Den Terminkalender des TSCHECHISCHEN Verbands lesen — bringt neben Turnieren die SPIELTERMINE der drei Mannschaftsmeisterschaften mit (`RoundDates` in der Antwort) |
| POST | `/api/admin/tournament-directory/chess-hu` | Den Kalender des UNGARISCHEN Verbands lesen (ein POST beim Crawler; die Quelle braucht dafuer rund 75 Sekunden) |
| POST | `/api/admin/tournament-directory/chess-sk?details=` | Den Kalender des SLOWAKISCHEN Verbands lesen — ein Abruf der Schnittstelle plus einer je Turnier fuer die Detailseite; `details=false` laesst den teuren Teil weg |
| POST | `/api/admin/tournament-directory/classify` | Publikum + Format des GANZEN Bestands aus den Turniernamen neu ableiten (Jugendklasse, Geschlechtsklasse, Liga) — braucht kein Netz. Der Weg, eine nachgeruestete Wortliste im `TournamentClassifier` auf den Altbestand anzuwenden; die Turnier**art** bleibt unangetastet (die kommt aus der Quelle) |
| POST | `/api/admin/tournament-directory/disambiguate?limit=` | Spielort ueber die VEREINSNAMEN aufloesen (Abkuerzungs-Fall, siehe unten) — ein Seitenabruf je Turnier, gedeckelt |
| PUT | `/api/admin/tournament-directory/{id}/coordinates` | Koordinaten von Hand setzen (GeoSource=Manual, ueberlebt den Sweep) |

Die `/api/admin/...`-Routen haengen an der Permission `tournaments.manage`.

**Spieltermine: warum Start und Ende nicht genuegen.** Ein Eintrag traegt Start und Ende, und der
Kalender zeichnet ein mehrtaegiges Turnier an JEDEM Tag dazwischen. Bei einem Wochenend-Open ist
das richtig. Bei einer LIGA ist es falsch: „26.09.2026 bis 17.04.2027" sind elf Runden mit zwei
bis fuenf Wochen Abstand — die Liga stand damit an rund 200 Kalendertagen, an denen nichts
gespielt wird, und verdeckte die Turniere, die es wirklich gibt. Am Dev-Stand gemessen: **610
offene Eintraege laufen laenger als acht Tage, 527 davon ueber 40 Tage.**

`TournamentRoundPlanService` holt die Termine (chess-results `art=14`, ~17 kB — die kleinste
Ansicht, die sie traegt; `art=2` kostet 33 kB, `art=3` 182 kB) und legt sie in
`TournamentDirectoryRounds` ab. Die Auswahl haengt an der **DAUER, nicht am Liga-Merkmal**: ob
etwas eine Liga IST, wird geraten, ob sein Zeitraum luegt, steht fest — mehr als
`MinSpanDays` (8) Tage und mehr als eine Runde. Eine monatelange Vereinsmeisterschaft ist keine
Liga und braucht die Termine genauso. `RoundPlanCheckedAt` verhindert Wiederholungen und wird
**auch bei einem leeren Plan** gesetzt (der haeufige Fall, der sich merken lassen muss); ein
NETZfehler laesst ihn dagegen leer. Der Sweep leert ihn, wenn sich der Termin geaendert hat.
`TournamentDirectory:RoundPlanBatchSize` (Vorgabe 200, 0 = aus) je Nacht.

**`retryEmpty=true`** nimmt auch Eintraege vor, die als geprueft gelten und KEINEN Termin haben.
Gebraucht, weil das Holen selbst kaputt sein kann: chess-results baut sein Layout aus Tabellen, die
Rundenplan-Tabelle steckt drei Ebenen tief, und `FindTableByHeaders` nahm die erste Tabelle, deren
Kopfzeile die Spaltennamen „enthaelt" — `TextContent` ist REKURSIV, also enthaelt schon die
aeusserste Wrapper-Tabelle sie. Ergebnis: leere Liste, ohne Fehler (auf Dev gemessen: 337 geprueft,
0 Termine). Behoben im Crawler (eine Datentabelle ist ein BLATT, `FindTableByHeaders` bevorzugt
Tabellen ohne verschachtelte Tabelle) — aber der Vermerk „geprueft" haette jede Wiederholung
verhindert, deshalb dieser Schalter. Bewusst opt-in: ein Turnier ohne veroeffentlichten Plan ist
der haeufige Fall.

**MIT `retryEmpty` kann `checked` NIE 0 werden** — und damit ist die Abbruchbedingung „wiederholen,
bis 0 kommt" dort unerreichbar. Genau das ist der Punkt des Schalters: ein Turnier ohne Plan bleibt
Kandidat. Am 2026-09-09 gemessen: von 580 Kandidaten bekamen 308 in den ERSTEN ZWEI Durchgaengen
ihren Plan, die restlichen 272 haben auf chess-results keinen — die Durchgaenge 3 bis 11 waren rund
1800 Seitenabrufe fuer null neue Termine (13 min je Durchgang), und ohne Eingriff waeren es 20
geworden. `scripts/directory-runs.sh` bricht deshalb auch ab, wenn `IDLE_ROUNDS` Durchgaenge
(Vorgabe 2) nacheinander keinen ZUGEWINN (`withPlan`) melden; ein Zugewinn setzt den Zaehler
zurueck, `IDLE_ROUNDS=0` schaltet die Regel ab. Fuer den naechtlichen Durchgang gilt das nicht — der
laeuft ohnehin nur eine Charge.

**Zwischengespeichert wird alle `SaveEvery` (25) Turniere** — und im `finally` auch beim Verlassen
eines Abbruchs, dort mit `CancellationToken.None` (ein abgebrochener Token wuerde genau den
Schreibvorgang verhindern, der die Arbeit retten soll). Grund: ein Durchgang ueber 200 Turniere
dauert **rund zwanzig Minuten** — die Seite ist in 25 ms da, den Rest machen der 1500-ms-Limiter
des Crawlers und die VPN-Rotation nach JEDEM Abruf (gemessen ~5,7 s je Turnier). Wurde erst am
Ende geschrieben, verwarf ein Abbruch, ein API-Neustart oder ein Deploy in dieser Zeit alles, und
der naechste Durchgang begann bei denselben Turnieren (auf Dev nachgemessen: nach sechs Minuten
null Spieltermine in der Datenbank). `Checked` im Ergebnis zaehlt bewusst die VERSUCHTEN, nicht
die erfolgreichen — daran haengt die Abbruchbedingung „nochmal starten, bis 0 kommt", und ein
Durchgang mit lauter Fehlschlaegen meldete sonst „nichts mehr zu tun".

Im Kalender gilt: **hat ein Eintrag Spieltermine, zaehlen NUR sie** (`Covers`). Die Termine gehen
als `roundDates` im DTO mit, die Detailseite zeigt sie.

**Ohne Termine gilt der Zeitraum nur, solange er plausibel ist** (0.437.1). Gemeldet an
tnr1474416 („II Vipiteno Chess Festival - 2° torneo rapid", 10min + 5sec): Zeitraum 11.08. bis
20.09., also 41 Kalendertage, an denen der Eintrag alles andere verdeckte. Die Angabe stammt so
von chess-results (per `tournament-info` gegengeprueft), und einen Rundenplan gibt es dort nicht —
die Abfrage liefert eine LEERE Liste, es ist also nicht „noch nicht geholt". Ein solcher Eintrag
steht nur an seinem STARTtag; ihn ganz wegzulassen waere falsch, er findet ja statt.

**Die Grenze haengt an der TURNIERART, nicht nur an der Dauer** (`MaxSpreadDays` 21,
`MaxFastSpreadDays` 8): „laenger als acht Tage" allein traefe auch das ehrliche mehrtaegige Open
(neun Tage, eine Runde pro Tag, kein hinterlegter Plan) — es verschwaende an acht von neun Tagen,
und das faellt weniger auf als der Fehler davor. Ein Schnell- oder Blitzturnier ueber mehr als eine
Woche kann dagegen nicht durchgehend sein. Am Dev-Stand gemessen: 603 der 715 termin-losen
Langlaeufer ziehen sich auf ihren Starttag zusammen, die 112 mehrtaegigen Standard-Turniere
zwischen 9 und 21 Tagen bleiben unangetastet.

**Woher ein Eintrag stammt (`TournamentDirectorySources`).** Der Sweep vermerkt bei jedem
Turnier, auf welcher Seite es gefunden wurde (`NoteSource`) — n-zu-n, weil dasselbe Turnier auf
mehreren steht und es mehr werden.

**Die IDENTITAET eines Eintrags ist `PublicId`, nicht die chess-results-Nummer** (0.428.0). Die
Nummer war gleichzeitig Schluessel, Routen-Parameter, Ausblend- und Melde-Schluessel, Abo und
Crawl-Ziel — ein FIDE-Ereignis hat keine. `PublicId` ist die chess-results-Nummer, wo es eine gibt,
sonst `f<FIDE-Nummer>`; `ChessResultsId` ist **nullbar** und alles, was chess-results wirklich
braucht, ist ausdruecklich darauf abgefragt (Rundenplan, Vereins-Aufloesung des Spielorts, Abo,
Merken, der Link dorthin). Die Migration `AddDirectoryPublicId` traegt die Spalte fuer den
Altbestand VOR dem Unique-Index nach (`UPDATE … SET PublicId = ChessResultsId`) — mit 4930
bestehenden Zeilen und Vorgabe `''` waere sie sonst gescheitert; ein Integrationstest fuehrt genau
diesen Weg vor.

**Der FIDE-Kalender ist die zweite Quelle** (`Services/TournamentDirectory/FideDirectorySweepService.cs`,
Crawler-Endpunkt `GET /api/fide-calendar?year=`). Wichtig ist der RICHTIGE Aufruf: mit
`show=table`/`show=apilist` liefert `calendar_server.php` 661 Ereignisse, **alle aus 2025** — das
war die Grundlage des frueheren Urteils „bringt heute nichts", und es war falsch. Gepflegt wird
`show=showYear&page=<Jahr>` (die Ansicht hinter `majorcalendar.php`): **143 Ereignisse fuer 2026,
139 davon lesbar, und 132 fehlten im Verzeichnis** — Kontinental-/Weltmeisterschaften und
Titelturniere, die nicht auf chess-results ausgeschrieben sind. `cat_filter`/`cat_cont` gehoeren
NICHT in den POST (Antwort 500). Ein Ereignis wird einem bestehenden Eintrag zugeordnet, wenn die
Termine hoechstens `MatchDayTolerance` (1) Tag auseinanderliegen UND die Namen mindestens zwei
unterscheidende Woerter teilen (oder eines plus denselben Ort); sonst kommt es als neuer Eintrag
mit `ChessResultsId = null` dazu. Online-Ereignisse (`ONL`) bekommen keine Koordinaten. Geplant
ueber `TournamentDirectory:FideYears` (Vorgabe 3 Jahre) nach dem naechtlichen Sweep, von Hand
`POST /api/admin/tournament-directory/fide`.

**Die VERBANDSKALENDER sind die dritte Quellenart** (`Services/TournamentDirectory/ExternalDirectorySource.cs` traegt
das Gemeinsame, je Land ein `…DirectorySweepService`; die Abgleichschleife selbst steht seit der Zerlegung Z7 EINMAL in
`ExternalSweepRunner`, die Quelle liefert nur ihre Abbildung als `IDirectorySourceAdapter<TRow>` — alle fuenfzehn
Verbandskalender laufen darueber: FRSah, WCU, CFC, SZS, ECF, chess.hu, KNSB, FSI, schachbund, Chess Scotland,
sjakk.no, ICU, chess.sk, chessarbiter und FFE. Sonderfaelle sind Haken mit Vorgabe: `BeforeMatchAsync` (ICU holt die
Detailseite VOR der Zuordnung), `ChessResultsId` (exakter Treffer ueber die Nummer der Quelle: chess.sk aus der
Liste, ICU von der Detailseite bzw. danach ueber den Vermerk), `OnCreated` (chess.sk: Nummer am neuen Eintrag), `AfterLookupAsync` (FFE laedt die
Vermerke nach); Detailseite samt Budget und Fassung (chessarbiter, FFE, sjakk.no) bleiben in `ApplyAsync`. ChessCz,
FIDE und der Ankuendigungskalender haben eine andere Schleife. Golden-Tests: `ExternalSweepGoldenTests`. Detail-Faenge
filtern ueber `ExternalDirectorySource.IsSourceFailure(ex, ct)`: ein HttpClient-Zeitlimit (OCE bei nicht abgebrochenem Lauf)
kostet nur die eine Detailseite, nicht den Lauf — A5-008). Jede
stellt dieselben vier Fragen: kenne ich
das Turnier schon (Termin ±1 Tag + zwei unterscheidende Woerter, ODER — chess.sk und ICU — die
mitgelieferte chess-results-Nummer, dann exakt), habe ich es selbst schon angelegt
(`<praefix><fremde-id>`), hat die Turniersuche es eingeholt (dann eigenen Eintrag zurueckziehen),
und woher kommt die Angabe (`TournamentDirectorySources`). **Eine Zusatzquelle fuellt nur
LUECKEN** (`FillIfEmpty`) — ersetzte sie vorhandene Werte, entschiede die Reihenfolge der
naechtlichen Durchgaenge, welche Angabe gilt. Die Turnier**art** bleibt dabei immer `Unknown`:
keine dieser Quellen sagt etwas darueber.

**Nummern-Treffer muessen halten.** ICU nennt die chess-results-Nummer nur auf der Detailseite, und
die wird je Turnier EINMAL geholt. Bis 0.832.20 fehlte die Nummer deshalb ab der zweiten Nacht, der
Namensvergleich schlug fehl, es entstand ein eigener Eintrag `ie<nr>` und der Herkunftsvermerk
wanderte dorthin — das Turnier stand doppelt im Kalender. Seitdem liest ICU in `BeforeMatchAsync` den
bestehenden Vermerk: haengt er an einem Eintrag MIT Nummer (also einem frueheren Treffer), gilt diese
Nummer weiter als exakter Schluessel. Gespeichert wird dafuer nichts Neues — die Nummer steht ja am
chess-results-Eintrag, an dem der Vermerk haengt. Eine LEERE Nummer der Quelle (chess.sk) wird seitdem als `null`
gespeichert, nicht als `""`: mit `""` zaehlte der Eintrag als „mit Nummer" (Kandidat im Namensvergleich,
ausserhalb der Verschwunden-Erkennung), und der eindeutige Index auf `ChessResultsId` liess nur EINEN
solchen Eintrag zu. Und ein EIGENER Eintrag, der die Nummer selbst traegt (chess.sk nennt sie, die Turniersuche
kennt das Turnier noch nicht), wird von der Nummernsuche wiedergefunden — das ist keine Zuordnung:
`ExternalSweepRunner` laesst einen Treffer auf den eigenen Eintrag fallen, der Eintrag nimmt den
normalen Weg (`ApplyAsync`), und Termin- und Ortsaenderungen der Quelle kommen an. Vorher zaehlte er
als „zugeordnet" und blieb auf dem Stand der ersten Nacht. ICU ist davon nicht betroffen: ihre
eigenen Eintraege tragen nie eine Nummer.

| Land | Praefix | Kosten | Was sie kann, was die anderen nicht koennen |
|---|---|---|---|
| Italien (FSI) | `it` | 1 Abruf (1,25 MB) | Von 285 Eintraegen verlinkt KEINER nach chess-results — Italien faehrt auf Vega/vesus. Keine PLZ, dafuer die Provinz (loest „Marino" auf) |
| Slowenien (SZS) | `sl` | 4 Abrufe | Faktor elf gegenueber chess-results; die PLZ steht schon in der TREFFERLISTE. Absagen stehen nur im Namen („ODPADE") — solche Turniere werden nicht angelegt |
| Slowakei (chess.sk) | `sk` | 1 + je Turnier 1 | Die einzige mit angebotener Schnittstelle. Liefert als einzige Anschrift MIT PLZ, Bedenkzeit, Rundenzahl, System und Bedenkzeit-Klasse — aber erst die Detailseite. Nennt bei einem Drittel die chess-results-Nummer selbst. Schulungen/Trainingslager stehen mit drin und bleiben draussen |
| Ungarn (chess.hu) | `hu` | 1 Abruf (POST, ~75 s) | Vor allem VORLAUF: ab November 2026 41 Turniere gegen 5. Nur Name, Termin, Ort — die Detailseite bringt gemessen nichts (46 von 52 Ortsnamen sind im Lexikon eindeutig). Ihr Feld „ifjusagi" heisst NICHT Jugendturnier (77 von 107 „ja") und wird nicht uebernommen |
| Irland (ICU) | `ie<nr>` | 5 + je NEUEM Turnier 1 | 81 kuenftige, 94 % nicht auf chess-results. 30 bringen Koordinaten aus einem eingebetteten `map-data`-Block der TREFFERSEITE mit — die Detailseite liefert KEINE zusaetzlichen, nur den Dedup-Schluessel und die Meldezahl. Die ICU ist gesamtirisch: 11 Turniere liegen in Nordirland, der Eintrag bleibt `IRL`, der Geocoder bekommt `ENG` |
| Frankreich (FFE) | `fr<ref>` | 12 + je NEUEM Turnier 1 | Der groesste Ertrag: von 40 gegengeprueften stehen ZWEI auf chess-results. Enddatum, Bedenkzeit und PLZ gibt es NUR auf der Turnierseite — ohne sie staende jedes Wochenend-Open an einem Tag und ganz Frankreich auf `Speed.Unknown`. Die FFE schreibt das Inkrement mit zwei Apostrophen (`60' + [30'']`), das normalisiert die Quelle vor dem Klassifizierer |
| Norwegen (sjakk.no) | `no`+Hash | 1 + je NEUEM Turnier 1 | chess-results kennt fuer NOR NULL kuenftige Turniere. Der Feed hat kein Ortsfeld; an allen 81 Turnieren nachgemessen (2026-09-09): **17 verortbar (21 %)**, nach einem NO-PLZ-Import **26 (32 %)**. Die Detailseite traegt dazu nur **zwei** bei — sie rechnet sich ueber Veranstalter (52 von 80) und Bedenkzeit (38), nicht ueber den Ort. Ihre FUSSLEISTE nennt die Verbandsanschrift in Oslo: `ParseDetail` liest ausschliesslich die Zeile „Spillsted", eine Suche ueber die ganze Seite pinnte ganz Norwegen nach Oslo |
| Schottland (Chess Scotland) | `sc`+Hash | 1 + bis 43 | 44 kuenftige bis 2028, Bedenkzeit-Klasse STRUKTURIERT. Die Liste nennt keinen Ort; in den Ausschreibungen steht bei 8 von 43 eine Postleitzahl — **die Verortung bleibt schwach** **Vermerkt wird der KURZSCHLUESSEL, nicht der Slug** (seit 2026-09-15): ein Slug mit 71 Zeichen sprengte die 60-Zeichen-Spalte, und die Quelle brach vom 10. bis 15.09. jede Nacht komplett ab. Die 43/44 Altvermerke (Dev/Prod) mit rohem Slug stellt der Lauf selbst um (`MigrateLegacyNotesAsync`) — ohne das haette jeder Eintrag einen zweiten Vermerk bekommen und `HasOtherNoteOfSameKind` jede Zuordnung zu chess-results blockiert. |
| Rumaenien (FRSah) | `ro<nr>` | 1 Abruf | Wie England „The Events Calendar" — aber die Spielstaette steckt schon IM Ereignis, der zweite Abruf entfaellt. Die 5 Eintraege OHNE Spielstaette sind genau die nationalen Mannschaftsligen, und genau die fehlen auf chess-results. `robots.txt` antwortet selbst mit 403 (siehe Doku) |
| Wales (WCU) | `wl`+Hash | 1 Abruf | Die billigste Quelle. 30 von 38 mit vollstaendiger Postleitzahl. KEINE eigene Kennung — sie entsteht aus Termin + Anschrift, bewusst OHNE den Namen (derselbe Veranstaltungsort erscheint als „Best Western", „Bst Western", „Bet Western"; ein korrigierter Tippfehler im Namen darf keine Dublette erzeugen). Weil dieser Schluessel laenger ist als die 60 Zeichen von `TournamentDirectorySource.ExternalId`, vermerkt die Quelle ihren KURZSCHLUESSEL (`PublicIdOf`) — roh gab es jede Nacht „Data too long for column" |
| Niederlande (KNSB) | `nl`+Hash | 2 Abrufe (15 s Pause) | 177 kuenftige gegen 12. Bedenkzeit-Klasse kommt strukturiert aus einer Taxonomie. **Der Spielort fehlt strukturell** — er stuende nur auf der Detailseite, und 177 × 15 s waeren 45 Minuten; bewusst nicht gebaut. Die Eintraege stehen in Liste und Kalender, nicht auf der Karte **Seit 2026-09-14 gesperrt fuer unseren VPN-Ausgang**: statt JSON kommt eine JavaScript-Warteseite (HTTP 200, text/html, „One moment, please..."), von einer privaten IP dieselbe Adresse unveraendert als JSON. Das ist eine IP-Sperre, keine geaenderte Schnittstelle, und sie wird NICHT umgangen; der Crawler meldet sie als 502 mit Auszug (`SourceResponse.EnsureJson`) statt als Parser-Absturz. |
| England (ECF) | `en<nr>` | 12 Seiten, **~200 s** | 278 kuenftige Turniere (gemessen 2026-09-09), **86 % nicht auf chess-results**. Die mit ABSTAND langsamste Quelle, und nicht wegen des Servers: ihre robots.txt verlangt „Crawl delay: 10", das Warten IST die Laufzeit. Sie ist der Grund, warum `TournamentDirectoryService.DefaultCrawlerTimeoutSeconds` 600 s betraegt — mit den frueheren 180 s lief sie in JEDER Nacht in den Timeout, ohne je ein Turnier zu liefern. Die EINZIGE Quelle mit Koordinaten (`geo_lat`/`geo_lng` im Spielstaetten-Endpunkt, 168 von 256) → `GeoSource.SourceProvided`, kein Geocoding. Ihre Schlagworte sind gepflegt: „Meeting" ist kein Turnier, „Online" hat keinen Ort, „Juniors Only" ist eine verlaessliche Jugend-Angabe |
| Deutschland (schachbund) | `de`+Hash | 2 je Region (25) | Ein reines MELDE-System: hier stehen Vereins-Abendturniere, Jugend-Cups, Fernschach, Problemschach und Schach960 — Arten, die chess-results nie fuehrt. Die SEITE traegt die Anschrift (105 von 106), der FEED Rundenzahl und Bedenkzeit; beides wird gebraucht. „europa"/„welt" sind keine Laender und bekommen keine Foederation. Der Slug ist laenger als 60 Zeichen: vermerkt wird der Kurzschluessel (`PublicIdOf`) Vier Regionen der Uebersichtsseite haben KEINEN Feed (schach960, problemschach, blindenschachbund, fernschachbund — gemessen 2026-09-15, beide Adressvarianten 404); sie werden ohne Ausschreibung gelesen (`RegionsWithoutFeed`). Bewusst eine feste Liste: ein 404 auf einer gewoehnlichen Region bleibt eine Warnung. |
| Polen (chessarbiter) | `pl<jahr>-<nr>` | 1 + je NEUEM Turnier 1 | Die ergiebigste Einzelquelle: 611 kuenftige Turniere in EINEM Abruf. Die Liste nennt kein Jahr (kommt aus der Sortierung); die Detailseite bringt Enddatum, Bedenkzeit, Rundenzahl, System — und als einzige Quelle die TEILNEHMERZAHL schon vor dem Turnier. **Nur ~20 % der Turniere haben ueberhaupt eine server-gerenderte Datenseite**, die uebrigen liefern eine JavaScript-Huelle: der Crawler antwortet dort mit **204** (gefragt, nichts da — endgueltig) statt 404 (nicht zu holen — wiederholen), und `ChessArbiterDetailVersion` vermerkt es. Die `Url` im Herkunftsvermerk heisst weiter GELESEN, nicht bloss gefragt |
| Kanada (CFC) | `ca`+Hash | 2 Abrufe | **171 kuenftige gegen 68** — chess-results wird dort kaum als Kalender benutzt. Der Datensatz liegt als STATISCHE Datei `/ext/cfc-data.<hash>.js` (`window.ws_cfc_data`), der Hash wechselt bei jedem Site-Build und wird aus dem HTML gelesen. KEINE Kennung: `oid` ist die Listenposition, also Schluessel aus Termin + Ort + NAME — Termin und Ort allein haben 13 Kollisionen (zwei Turniere am selben Tag in derselben Stadt), und eine Anschrift gibt es nicht. Gefiltert wird ueber die PROVINZ, nicht den Typ (ein FIDE-Turnier in Usbekistan traegt `type=OTB` und nur `prov=FO`). Ort ist eine Stadt ohne PLZ; 15 % der Namen tragen nur EIN unterscheidendes Wort und bekommen daher ihren eigenen Eintrag statt den chess-results-Treffer |
| Tschechien (chess.cz) | `cz`+Hash | 1 Abruf | Klein im Volumen (38 echte Turniere, 13 neu), aber sie liefert SPIELTERMINE: 33 Ligarunden werden zu drei Eintraegen mit je elf Runden — sonst je Turnier ein eigener `art=14`-Abruf. Ergaenzt nur FEHLENDE Runden. Ihre Kennung ist ein bis zu 57 Zeichen langer Slug, deshalb als Kurzwert gespeichert |

**Kein VPN-Ausgang erreicht alle Quellen — deshalb wird gewechselt und wiederholt.** Mehrere
Verbandsseiten sperren ganze Hosting-Netze, und zwar verschiedene: am 2026-09-09 gemessen kam vom
deutschen AirVPN-Ausgang (alle auf M247) keine TCP-Verbindung zu `federscacchi.com` (ITA) und
`frsah.ro` (ROU) zustande, vom niederlaendischen (Global Layer) keine zu `chess.sk` (SVK). Ein
fester Standort tauscht also nur ein Land gegen ein anderes. Der Crawler wiederholt einen
Quellen-Abruf deshalb bis zu fuenf Mal und wechselt zwischen den Versuchen den Ausgang
(`RotateOnConnectFailureHandler`, an allen 16 Quellen-Clients). Wiederholt wird NUR das
Nicht-Zustandekommen der Verbindung — eine Antwort der Quelle, auch 403 oder 500, ist eine Auskunft
und wird durchgereicht. Die Rotation selbst liegt in `VpnReadinessGate` und haelt dabei den
Crawl-Riegel (`CrawlerService.CrawlGate`): waehrend stop→pause→start ist der Tunnel unten.

Vollstaendige Messungen und die Rechtslage je Quelle: `docs/turnierquellen.md`.

**Publikum und Format eines Turniers — was aus der Quelle kommt und was aus dem Namen.**
`Kind` (Einzel/Mannschaft) ist QUELLENDATUM: die chess-results-Turniersuche hat ein Turnierart-Feld
(`combo_art`), und die Arten **2** („Rundenturnier fuer Mannschaften") und **3** („Schweizer System
fuer Mannschaften") sind genau die Mannschaftsturniere. Der Sweep fragt sie deshalb in **zwei
zusaetzlichen Durchgaengen** je Foederation ab (`&art=2`, `&art=3`) — also drei Crawler-Requests
statt einem. Faellt ein solcher Durchgang aus ODER laeuft er in die 2000-Zeilen-Grenze, bleibt die
gespeicherte Art **unangetastet** (`Unknown` heisst „noch nicht geklaert"): ein Netzausfall darf
nicht den halben Bestand auf „Einzel" umschreiben.

`AgeGroups`, `Gender` und `IsLeague` sind ABLEITUNGEN (`Services/Tournaments/TournamentClassifier.cs`, rein
statisch, Wortlisten an EINER Stelle zum Nachruesten). Alter und Geschlecht stehen nirgends als
Spalte — auch nicht auf der Turnierseite — sondern nur im Namen, und der traegt sie erstaunlich
verlaesslich („Landesmeisterschaft U12 weiblich", „UNDER 16 GIRLS"). Zwei Fallen sind dort bewusst
geschlossen und je mit einem Test festgenagelt: **„U2000" ist eine Ratinggrenze, kein Alter**
(hoechstens zwei Ziffern + Wortgrenze), und **„men" steckt in „women" und „Damen", „male" in
„female"** — als Teilzeichenkette gesucht war jedes Frauenturnier gleichzeitig ein Herrenturnier
und damit (beides gesetzt) wieder ein offenes; diese beiden zaehlen deshalb nur als ganzes Wort.
Ein freistehendes „w"/„m" ist in diesen Namen eine GRUPPE („Open Braunau 2026 B") und zaehlt nur
direkt hinter der Altersklasse („U12w"). Liga = Liga-Wort im Namen ODER Mannschaftsturnier ueber
mindestens 35 Tage (eine Saison laeuft Oktober bis April, ein Mannschaftsturnier am Wochenende
einen Tag); die Dauerregel gilt NUR fuer Mannschaftsturniere, sonst waere jede monatelange
Vereinsmeisterschaft eine Liga. Nachwuchs ohne genannte Klasse (`YouthUnspecified`) faengt eine
Wortliste, in der auch **regionale Eigennamen** stehen — „Schachrallye" ist in Tirol immer
Nachwuchs. Solche Kennungen kann niemand von aussen erraten; genau dafuer fragt das Melde-Formular
(`POST .../report`, Feld `namePattern`) danach, und `POST .../classify` wendet die erweiterte Liste
auf den Altbestand an.

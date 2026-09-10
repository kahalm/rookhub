# Weltweiter Ausbau des Turnierverzeichnisses — Plan

Stand der Messung: **2026-09-10, Dev auf v0.456.9**. Alle Zahlen sind aus dem Dev-Bestand gemessen,
nicht geschaetzt; wer sie nachrechnen will, findet die Abfragen in `docs/turnierquellen.md`.

Ausgangslage: 22 458 Turniere, **7 134 davon kuenftig**, 65 % verortet, alle 257
chess-results-Foederationen erstmals gesweept, 15 Verbandskalender angebunden — alle in Europa.

---

## 1. Der Befund, der den ganzen Plan bestimmt

| Region | Foed. | Turniere | kuenftig | **Vorlauf** | verortet |
|---|---|---|---|---|---|
| **Europa** | 54 | 9 447 | 4 843 | **1,05** | 67 % |
| Amerika | 31 | 4 887 | 1 106 | **0,29** | 64 % |
| Asien | 39 | 6 554 | 959 | **0,17** | 68 % |
| Afrika | 35 | 1 092 | 120 | **0,12** | 52 % |
| Ozeanien | 5 | 12 | **0** | — | 58 % |

**Vorlauf** = kuenftige Turniere geteilt durch die der letzten 30 Tage. Er sagt, wie weit
chess-results in einer Region nach vorn sieht.

**Ausserhalb Europas ist chess-results ein ARCHIV, kein Kalender.** Asien hat 6 554 Eintraege und
davon 959 kuenftige. Das ist keine Frage der Datenmenge, sondern der Veroeffentlichungspraxis: die
Turniersuche fuellt sich beim Swiss-Manager-Upload, also Tage vor dem Turnier. Ein Verzeichnis
lebt aber vom Vorlauf — ein Archiv hilft niemandem, der ein Turnier SUCHT.

Genau diesen Vorlauf liefern die Verbandskalender, und der Beleg steht in den europaeischen Zahlen:
Polen 13,94 · Italien 11,63 · Niederlande 8,22 · Frankreich 6,03 · Oesterreich 3,45 · England 3,44
— die sechs vordersten Plaetze halten Laender mit angebundenem Kalender.

---

## 2. Was „weltweit ausrollen" NICHT heisst

**Nicht mehr sweepen.** Alle 257 Foederationen sind erfasst, der naechtliche Lauf haelt sie frisch
(9 taeglich + 40 rotierend = volle Runde in fuenf Naechten). Ein weiterer Sweep bringt null.

**Nicht mehr Laender „anschalten".** Es gibt keinen Schalter. Jede zusaetzliche Quelle ist ein
eigener Parser mit eigener Rechtspruefung, eigenem Dedup-Schluessel und eigenen Tests.

Die Arbeit besteht aus genau zwei Dingen: **das Vorhandene sichtbar machen** (Verortung) und
**die vorausschauenden Quellen finden** (Kalender). In dieser Reihenfolge, weil das erste kein
einziges HTTP-Abruf nach draussen kostet.

---

## 3. Zwei Klassen von Laendern — und sie brauchen Verschiedenes

**Archivlaender**: viel Volumen, kaum Vorlauf. chess-results wird benutzt, aber spaet.
Ein Kalender ERGAENZT dort den Vorlauf.

```
IND 2 004 Eintraege /  416 kuenftig / Vorlauf 0,26     BRA 1 716 / 532 / 0,45
VIE   868 /  84 / 0,11      INA   814 /  35 / 0,04     ARG   668 / 123 / 0,23
RSA   593 /  48 / 0,09      PER   550 /  64 / 0,13     MEX   473 /  89 / 0,23
KAZ   458 /  50 / 0,12      PHI   446 /  57 / 0,15     IRI   371 /  64 / 0,21
COL   345 /  47 / 0,16      MAS   327 /  61 / 0,23     CHI   293 /  19 / 0,07
CHN   273 /  19 / 0,07      SRI   190 /   2 / 0,01
```

**Abwesende Laender**: chess-results wird dort praktisch nicht benutzt. Ein Kalender ERSETZT dort
alles.

```
USA  17 Eintraege (5 kuenftig)   ·  AUS 2 (0)  ·  NZL 1 (0)  ·  DEN 1 (0)
CAN 146 (68)  ·  CHN 273 (19)
```

**Die abwesenden Laender sind der hoehere Ertrag je Aufwand, und das ist gemessen, nicht
vermutet**: Norwegen hatte 0 kuenftige Turniere auf chess-results, der Verbandsfeed lieferte 81 —
der Zugewinn war rechnerisch vollstaendig. Die USA mit rund 90 000 gewerteten Spielern und
17 Eintraegen im Verzeichnis sind derselbe Fall in groesserem Format.

---

## 4. Phase 1 — die Karte reparieren, ohne einen einzigen Abruf nach draussen

**7 769 Turniere liegen im Bestand und stehen NICHT auf der Karte** — ein Drittel. Sie sind schon
bezahlt: gecrawlt, gespeichert, in Liste und Kalender sichtbar. Nur die Karte laesst sie aus.

Nach URSACHE aufgeschluesselt, weil jede eine andere Behebung braucht:

| Ursache | betroffen | Behebung | Stand |
|---|---|---|---|
| Kein GeoNames-PLZ-Datensatz | IRI 227 · KAZ 201 · GRE 116 · ISR 51 · ARM 47 · MGL 27 | `cities1000` NUR fuer diese Laender (Laenderfilter beim Parsen) | gemessen, in `TODO.md` |
| Verstreute Siedlungsnamen im PLZ-Feld | PER 297 · INA 234 · PHI 165 · MAS 163 · COL · CHI | eigener Weg noetig — Stadtzeile vor PLZ-Zeile bei Streuung | gemessen, in `TODO.md` |
| Mehrdeutigkeit (gleichnamige Orte) | RUS 348 · GER 251 · POL 207 | Haufen statt Spannweite, **dominanter** Haufen | Schwelle gemessen (8 km), in `TODO.md` |
| Han-Schrift | CHN 216 (201 mit Han-Zeichen) | Pinyin-Umschrift — die einzige echte Schriftluecke weltweit | ungemessen |
| Strukturell ohne Spielort | NED 204 | nichts zu tun, bewusst so | erledigt |
| Pseudo-Foederation ohne Land | XXX 224 · ACC 72 | kein Landfilter moeglich, kein Fehler | erledigt |
| **Ursache unbekannt** | **BRA 544 · VIE 414 · RSA 248 · ARG 192** | **zuerst messen** | offen |

**Die vier „unbekannt" sind der erste Arbeitsschritt des ganzen Plans**, nicht der letzte: 1 398
Turniere, und niemand weiss, warum sie keinen Pin haben. Brasilien und Vietnam allein sind mehr als
der gesamte italienische Bestand. Vorgehen wie bei Peru: `LocationText` der unverorteten ansehen,
`GeoSource` aufschluesseln (`None` = kein Kandidat, `Ambiguous` = zu viele), das Lexikon des Landes
gegenpruefen. Kostet eine Stunde und entscheidet, ob es ein Import, ein Parser oder eine Regel ist.

**Warum Phase 1 vor Phase 2 kommt**: sie braucht kein Netz, kein Recht, keine robots.txt und keinen
neuen Dienst. Und sie wirkt auf jedes Turnier, das eine spaetere Quelle liefert, gleich mit.

---

## 5. Phase 2 — die abwesenden Laender (hoechster Ertrag je Aufwand)

Reihenfolge nach erwartetem Zugewinn, alle vier mit derselben Methodik wie Runde 1–3
(`docs/turnierquellen.md`).

1. **USA** — `new.uschess.org/upcoming-tournaments` ist in Runde 1 geprueft und als ⚠️ mit einem
   Vorbehalt vermerkt; der Vermerk ist der Startpunkt, nicht die Recherche. 17 Eintraege im
   Bestand heisst: praktisch das ganze Land fehlt. US Chess fuehrt den Kalender selbst, und die
   Landesverbaende (State Associations) darunter.
2. **Australien + Neuseeland** — 3 Eintraege zusammen, noch nie recherchiert. Beide Verbaende
   fuehren eigene Turnierverwaltungen. Erst pruefen, ob es eine Schnittstelle gibt („The Events
   Calendar" zuerst probieren, es kostet einen Abruf).
3. **Kanada** — `chess.ca` ist geprueft: sauberer JSON-Volldump, aber **ohne stabile Kennung**.
   Das ist derselbe Fall wie Wales (Schluessel aus Termin + Anschrift) und damit geloest, sobald
   jemand ihn baut.
4. **China** — 273 Eintraege, 19 kuenftig, 21 % verortet. Hier haengen Quelle UND Schrift
   zusammen; ohne Pinyin-Umschrift bringt eine neue Quelle Turniere ohne Karte.

---

## 6. Phase 3 — die Archivlaender nach Volumen

Erst nach Phase 1, weil die Verortung dieser Laender teils selbst noch offen ist (Peru 46 %,
Vietnam 52 %, Malaysia 50 %). Eine Quelle anzubinden, deren Turniere anschliessend keinen Pin
bekommen, verschiebt das Problem nur.

Reihenfolge: **IND · BRA · INA · VIE · ARG · RSA · MEX · PHI**. Indien zuerst, aus zwei Gruenden:
2 004 Eintraege sind der groesste Bestand ausserhalb Europas, und mit **99 % Verortung** ist es das
einzige grosse Land, in dem die Karte schon jetzt traegt. Der Zugewinn waere sofort sichtbar.

Fuer jedes Land dieselben vier Fragen wie bisher, in dieser Reihenfolge:
1. Gibt es einen gepflegten Kalender mit Vorlauf — und wie viele kuenftige Turniere stehen darin?
2. Wie viele davon stehen NICHT auf chess-results? (Die Vergleichszahl kommt aus unserem eigenen
   Bestand, sie ist jetzt fuer alle 257 Foederationen da.)
3. Traegt die Quelle einen Ort, den unser Lexikon aufloest? **Vorher messen, nicht danach** —
   die Lehre aus Norwegen.
4. Robots.txt in ZWEI Fragen (darf ClaudeBot, darf unser Crawler unter `User-agent: *`) plus
   Rechtsvorbehalt/`Content-Signal`.

---

## 7. Phase 4 — Prod

Das Verzeichnis lebt bis heute ausschliesslich auf Dev. Ein weltweiter Bestand, den niemand sieht,
ist keine Auslieferung. Der Weg steht fest und ist nicht der naive:

- Prod laeuft auf **v0.395.1** und hat die Verzeichnis-Tabellen nicht.
- Der Bestand wird **aus Dev kopiert**, nicht neu gecrawlt — 22 458 Turniere neu zu holen waere
  Tage hinter dem Rate-Limiter und wuerde die Quellen ohne Not belasten.
- **Vor dem ersten Start einmalig `scripts/gazetteer-import.sh`**, sonst bleibt die Karte leer.
- Danach einmal `gazetteer/transcribe` (die Umschrift-Spalte ist nicht im Dump enthalten, wenn
  der Dump vor 0.456.0 gezogen wurde) und **`geocode-missing` ZWEIMAL** ohne `limit`.
  **Der zweite Durchgang ist nicht Vorsicht, er bringt etwas** (gemessen am 2026-09-10: erster
  Lauf 583, zweiter 60, dritter 0). Grund ist die Mehrdeutigkeits-Entscheidung ueber die
  Turnierdichte: sie zaehlt VERLAESSLICH verortete Nachbarn, und die entstehen erst im Lauf davor.
  Von den 60 des zweiten Durchgangs waren 22 philippinische Kleinstaedte, die im ersten noch
  unentscheidbar waren. Zwei Durchgaenge genuegen — der dritte fand null.
- Ein Tag erzeugt Prod-Images UND die Store-Einreichung, und Watchtower macht ihn in der Nacht
  live. Also erst taggen, wenn Phase 1 abgeschlossen ist — sonst geht eine Karte mit einem Drittel
  fehlender Pins raus.

---

## 8. Kapazitaet: was der Nachtlauf traegt

Gemessen in der Nacht zum 2026-09-10: **03:00 bis 03:55**, davon 49 Foederationen (je drei
Crawler-Abrufe, ~26 s) bis 03:37, danach die 15 Kalender in 18 Minuten.

Zwanzig weitere Kalender kosten geschaetzt 20–30 Minuten, der Lauf endete dann gegen 04:25. Das
passt, aber drei Dinge sind vorher zu klaeren:

- **Der Deckel liegt nicht am Nachtfenster, sondern am Rate-Limiter des Crawlers** (1 500 ms plus
  VPN-Rotation nach jedem Abruf, gemessen ~5,7 s je Anfrage). Wer parallelisieren will, muss dort
  ansetzen — und das beruehrt auch die Turnier-Crawls der Nutzer.
- **Die langsamste Quelle bestimmt das Zeitlimit**: ECF braucht 200 s wegen `Crawl delay: 10`,
  deshalb steht `DefaultCrawlerTimeoutSeconds` auf 600. Eine noch hoeflichere Quelle verschiebt
  diese Grenze.
- **`WeeklyBatchSize` = 40** ergibt bei 257 Foederationen eine Runde in fuenf Naechten. Fuer
  Archivlaender, deren Turniere Tage vorher erscheinen, ist das grenzwertig — dort ist eine
  taegliche Abfrage sinnvoller als bei einem Land mit einem Jahr Vorlauf. Der Schalter dafuer
  existiert (`DailyFederations`), die Zuordnung ist aber heute europaeisch.

---

## 9. Nicht-Ziele

- **Kein Land „nur der Vollstaendigkeit wegen".** Der Massstab ist Liechtenstein aus Runde 1: ein
  eigener Dienst mit Tests, Zeitplan und Nachtlauf rechnet sich nicht fuer drei Zeilen, die von
  Hand schneller eingetragen sind. Montenegro (3 gegen 1) und Albanien (3 gegen 2) stehen deshalb
  bis heute auf ⚠️.
- **Keine Quelle gegen ihre robots.txt.** Die Schweiz hat die technisch beste Schnittstelle der
  ganzen Reihe und bleibt ungenutzt, weil `cms.swisschess.ch` pauschal `Disallow: /` sagt. Eine
  Anfrage nach Freigabe ist der Weg, nicht ein anderer User-Agent.
- **Keine Anti-Bot-Umgehung.** Cloudflare-Turnstile beim Weiterblaettern (Schweiz) ist eine Grenze,
  keine Huerde.
- **Keine Alles-oder-nichts-Auslieferung.** Jede Phase ist fuer sich nuetzlich; Phase 1 verbessert
  die Karte auch dann, wenn keine einzige neue Quelle dazukommt.

---

## 10. Was zuerst passiert, in einem Satz

**Die vier unbekannten Verortungsursachen messen** (Brasilien 544, Vietnam 414, Suedafrika 248,
Argentinien 192 fehlende Pins) — eine Stunde Arbeit, kein Netzabruf, und danach steht fest, ob der
Rest von Phase 1 ein Import, ein Parser oder eine Regel ist.

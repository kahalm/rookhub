# Gemeinfreie Lehrbücher als Punktepartien

Zwei Umsetzer, die aus einem **gemeinfreien Schachbuch** PGN mit Zug-Kommentaren machen. Das ist
die Quelle für den kuratierten Bestand der Punktepartie: eine Meisterpartie ohne Anmerkungen kann
man nachspielen, aber die Anmerkungen sind der Grund, sie zu spielen.

| Datei | Buch | Kommentiert von | Wo liegen die Anmerkungen |
|---|---|---|---|
| `capa2pgn.py` | Capablanca, *Chess Fundamentals* (1921) | Capablanca selbst | überwiegend an SEINEN Zügen — er verliert mehrere der Beispielpartien |
| `strategy2pgn.py` | Edward Lasker, *Chess Strategy* (1915) | ein Dritter | auf **beiden** Seiten (gemessen: 59 an Weiß-, 71 an Schwarz-Zügen) |

Beide Bücher stehen bei Project Gutenberg (#33870 bzw. #5614) und sind gemeinfrei.

## Das eigentliche Problem: beschreibende Notation

Die Bücher schreiben `P - Q 4`, `Kt - K B 3`, `B x B P` — aus **Sicht des Ziehenden** und ohne
eindeutige Felder. Übersetzt wird das hier nicht, sondern **aufgelöst**: zu jedem Token werden die
legalen Züge der Stellung gefiltert (Figur, Zielfeld, Schlagen ja/nein, Umwandlung, Seite). Bleibt
mehr als einer, entscheiden weiche Merkmale, und wo auch das nicht reicht, läuft eine
Rückverfolgung über die ganze Partie. Passt am Ende nichts, bricht die Partie **mit Meldung** ab,
statt einen falschen Zug zu raten — eine still falsch gelesene Partie wäre das Schlimmste, was
hier passieren kann.

Die Fallen, die dabei Zeit gekostet haben, stehen als Kommentar an den betreffenden Funktionen:
`Kt` ist der Springer und nicht „König irgendwas" (daran scheiterten im ersten Anlauf alle 14
Partien), `Q Kt` ist der Damenspringer, `Castles` ist die Rochade, `Resigns.` ist kein Zug — und
in *Chess Strategy* klebt das Schachzeichen an der Ziffer (`B-R5ch`) und die Grundreihe heißt `sq`
statt `1` (`QR-Q sq`).

## Aufruf

```bash
# Buch holen und umsetzen (die Zahl am Ende: so viele Partien, die sauber durchlaufen)
curl -sL -o chess-strategy.txt https://www.gutenberg.org/cache/epub/5614/pg5614.txt
python3 strategy2pgn.py chess-strategy.txt lasker-10.pgn 10

# Als Punktepartien anlegen (kuratierter Bestand, Zieltiefe 20)
python3 ../seed_game_analyses.py --user 3 --depth 20 --public --manifest - --pgn lasker-10.pgn \
  | docker exec -i rookhub-mariadb-dev sh -c 'mariadb -uroot -p"$MARIADB_ROOT_PASSWORD" rookhub'
```

`--manifest -` heißt „ohne handgepflegte Titel-Liste": der Titel entsteht dann aus den PGN-Headern.
Die ratende Seite wird **nicht** mitgegeben — die leitet der Server ab (Ergebnis, sonst die
Bewertung der letzten gerechneten Stellung, siehe `GuessSessionService.WinnerSideAsync`).

## Was man von einem Lauf erwarten darf

Nicht jede Partie geht durch, und das ist Absicht. Von den 47 Partien in *Chess Strategy* liefen
beim ersten vollständigen Lauf 10 der ersten 18 sauber durch; die übrigen scheitern an Stellen, an
denen das Buch selbst mehrdeutig oder fehlerhaft gesetzt ist (`Kt-P` ohne Reihe, `P-Kt` ohne
Reihe). Der Umsetzer meldet je Partie, woran es lag, und überspringt sie.

**Die Gegenprobe, die wirklich trägt**: das Buch nennt zu jeder Partie die Eröffnung. Stimmt die
umgesetzte Zugfolge damit überein (Ruy Lopez ⇒ `e4 e5 Nf3 Nc6 Bb5`), ist die Auflösung mit hoher
Sicherheit die echte Partie und nicht eine zufällig legale Lesart. Bei den zehn eingespielten
Partien stimmten alle zehn.

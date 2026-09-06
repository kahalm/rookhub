# Startpaket Punktepartien

16 kuratierte Klassiker als Grundstock fuer den Modus **Punktepartie** — Partien, die sich fuer
„errate den Zug" bewaehrt haben: der Gewinner spielt einen erkennbaren Plan durch, die Pointen sind
findbar, und die Partien sind kurz genug, dass ein Durchlauf in einer Sitzung passt.

## Inhalt

`starter-pack.pgn` — alle 16 Partien in einer Datei, Standard-PGN ohne Kommentare.
`starter-pack.json` — dieselbe Liste als Manifest, mit **Titel**, **ratender Seite** und **Halbzuegen**.

| # | Partie | Ratende Seite | Halbzuege |
|---|--------|---------------|-----------|
|  1 | Morphy - Herzog von Braunschweig & Graf Isouard, Paris 1858 | Weiß | 33 |
|  2 | Anderssen - Kieseritzky, London 1851 (Die Unsterbliche) | Weiß | 45 |
|  3 | Anderssen - Dufresne, Berlin 1852 (Die Immergruene) | Weiß | 47 |
|  4 | Steinitz - von Bardeleben, Hastings 1895 | Weiß | 49 |
|  5 | Lasker - Bauer, Amsterdam 1889 (Doppel-Laeuferopfer) | Weiß | 75 |
|  6 | Rotlewi - Rubinstein, Lodz 1907 (Rubinsteins Unsterbliche) | Schwarz | 50 |
|  7 | Capablanca - Tartakower, New York 1924 (Turmendspiel) | Weiß | 103 |
|  8 | Aljechin - Bogoljubow, Hastings 1922 | Weiß | 103 |
|  9 | Botwinnik - Capablanca, AVRO 1938 | Weiß | 81 |
| 10 | Byrne - Fischer, New York 1956 (Partie des Jahrhunderts) | Schwarz | 82 |
| 11 | Fischer - Spasski, Reykjavik 1972 (6. Partie) | Weiß | 81 |
| 12 | Geller - Euwe, Zuerich 1953 | Schwarz | 52 |
| 13 | Polugajewski - Neshmetdinow, Sotschi 1958 | Schwarz | 66 |
| 14 | Short - Timman, Tilburg 1991 (Koenigsmarsch) | Weiß | 67 |
| 15 | Kasparow - Topalow, Wijk aan Zee 1999 | Weiß | 87 |
| 16 | Tal - Smyslow, Kandidatenturnier 1959 | Weiß | 51 |

Summe: **1072 Halbzuege** in 16 Partien.

Die ratende Seite ist immer die Gewinnerseite — also die, deren Plan man nachvollziehen soll.

## Herkunft und Rechtliches

Die Zugfolgen stammen aus den frei angebotenen Spielerdateien von <https://www.pgnmentor.com/files.html>
(Morphy, Anderssen, Steinitz, Lasker, Rubinstein, Capablanca, Aljechin, Botwinnik, Fischer, Geller,
Polugajewski, Short, Kasparow, Tal). Jede Partie wurde mit `python-chess` auf Zug-Legalitaet geprueft
und auf die Standard-Header eingedampft.

**Bewusst ohne fremde Kommentare oder Punktetabellen.** Die Zuege einer Partie sind Tatsachen und
nicht schutzfaehig; geschuetzt waeren fremde Anmerkungen und fertige Punkteschluessel (etwa
Pandolfinis „Solitaire Chess" oder Daniel Kings „How Good Is Your Chess?"). Die Punkte rechnet
RookHub ohnehin selbst aus der eigenen Engine-Analyse — ein fremder Schluessel wird nicht gebraucht.

## Verwendung

Jede Partie wird wie jede andere ueber **Analyse - Partie-Analysen** eingeworfen; sobald die
Hintergrund-Engine durch ist, laesst sich darauf eine Punktepartie starten. Die ratende Seite aus
dem Manifest ist ein Vorschlag, kein Zwang — die Seite waehlt der Spieler beim Start.

## Offen

- **Sichtbarkeit**: `GameAnalysis` haengt heute fest an einem Nutzer (`UserId`), es gibt kein
  Oeffentlich-Kennzeichen. Damit das Paket fuer alle Nutzer da ist (analog zum oeffentlichen Kurs
  „Matt in 1/2/3"), braucht es ein Seed-Konto plus ein Sichtbarkeits-Kennzeichen.
- **Analysetiefe**: die Vorgabe 30 ist fuer ein ganzes Paket teuer. Gemessen auf dem Deploy-Host
  (Ryzen 7 5700G, 12 Threads, 4 GB Hash, 5 Linien, Stockfish im `rookhub-engine-provider`):

  | Tiefe | Mittelspiel (Morphy) | Mittelspiel (Kasparow) | hochgerechnet auf 1072 Stellungen |
  |------:|---------------------:|-----------------------:|----------------------------------:|
  |    22 |               28,0 s |                 22,6 s |                            ~7,5 h |
  |    26 |              142,3 s |                  71,8 s |                             ~32 h |
  |    30 |              391,4 s |                      - |                    ~5 Tage |

  Fuer die Frage, um die es hier geht — war der geratene Zug besser oder schlechter als der
  Partiezug —, reicht eine deutlich kleinere Tiefe als fuer eine Eroeffnungsanalyse. Das Paket ist
  deshalb mit **Tiefe 22** angesetzt; eine Eroeffnungsstellung war damit in 13 s durch.

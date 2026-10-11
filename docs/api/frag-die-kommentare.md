# „Frag die Kommentare" — semantische Suche im Rohbestand (0.536.0)

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → „Frag die Kommentare" — semantische Suche im Rohbestand (0.536.0)“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.


Dritte Suchart im Dialog „Partie anfordern" (Reiter „Namen & Turniere" | „Frag die Kommentare"): eine Idee, ein Plan
oder ein Motiv in Worten („Läuferopfer auf h7", „Minoritätsangriff") → Partien, deren ANMERKUNGEN davon sprechen, in jeder
Sprache (das Embedding-Modell ist mehrsprachig), je Partie der passendste Auszug.

| Methode | Endpoint | Auth | Zweck |
|---|---|---|---|
| GET | `/api/library-games/semantic?q=&take=` | **AllowAnonymous** + RL | `{ available, indexed, items[{ game (wie die Namenssuche, mit inPool/requested), matches[{ fromPly, text, score }] }] }`. Ohne `q` nur `available`/`indexed` — der Dialog zeigt den Reiter nur mit Modell UND eingebettetem Bestand |

* **Stücke statt Partien** (`CommentChunks`, Tabelle `CommentEmbeddings`): Kopfzeile (Spieler, Turnier, Jahr, Kommentator)
  plus aufeinanderfolgende kommentierte Halbzüge („17. Bxh7+: …", Figurenschrift aufgelöst) bis ~900 Zeichen. Der Text
  des Stücks ist zugleich der Auszug der Trefferliste (ohne Kopfzeile).
* **Vektor in MariaDB** (≥ 11.7, Prod/Dev 11.8): Spalte `VECTOR(512)` (EF-seitig `byte[]`, float32 little endian — so nimmt
  MariaDB den Parameter an), Kosinus-Index per SQL in der Migration. Gesucht wird mit rohem SQL
  `ORDER BY VEC_DISTANCE_COSINE(`Vector`, @q) LIMIT 200` — genau diese Form benutzt den Index; InMemory rechnet in C#
  (`VectorMath`). **`VECTOR` ist ein Schlüsselwort — die Spalte steht in SQL immer in Backticks** (ohne: Syntaxfehler im
  `CREATE VECTOR INDEX`). Die CI-MariaDB (`mariadb:11`) muss dafür ≥ 11.7 sein; `CommentSearchSqlTests` prüft Speichern
  und Suche gegen echtes MariaDB.
* **Embedding-Modell** (`OpenAiTextEmbedder`, `Embedding:BaseUrl`/`ApiKey`/`Model`, Compose `EMBEDDING_*`): OpenAI-kompatibles
  `POST /embeddings` — gedacht ist ein Pooling-Modell auf dem DGX Spark, z. B. `Qwen/Qwen3-Embedding-0.6B`. Angefordert werden
  512 Werte (`dimensions`, Matryoshka); mehr wird gekürzt und neu normiert, weniger ist ein Fehler. Suchfragen bekommen die
  Qwen3-Anweisung vorangestellt (`QueryInstruction`), Dokumente nicht. **Auf dem Spark läuft `qwen3-embedding-4b`** (2560
  Werte, seit 2026-09-26) — OHNE Matryoshka-Schalter, vLLM antwortet auf `dimensions` mit 400. Der Client fragt dann einmal
  ohne nach und kürzt selbst (0.539.1; bei Qwen3-Embedding dasselbe wie serverseitig). `EMBEDDING_MODEL` gehört GESETZT:
  `/v1/models` des Spark-Proxys zeigt auf den Chat-Server und nennt das Embedding-Modell gar nicht (ohne Einstellung nimmt der
  Client das erste mit „embed" im Namen, sonst das erste).
* **Befüllen**: `tools/LibraryImport embed [--limit n] [--batch 32]` (Env `Embedding__BaseUrl` …): noch nicht eingebettete
  kommentierte Partien, beste Note zuerst, je Partie ganz oder gar nicht; wiederholbar. Die Auswahl
  (`CommentSearchService.PendingGameIdsAsync`) läuft EINMAL je Lauf mit 10-min-Timeout und wird dann in Portionen zu 500
  abgearbeitet (`EmbedGamesAsync`, überspringt schon eingebettete) — auf Prod dauerte sie bei ~10 000 eingebetteten
  Partien 37 s, und als sie je Portion lief, riss der 30-s-Standard-Timeout den Lauf ab (2026-09-26, 0.550.2).
  `--batch 256` = 256 Texte je Anfrage an den Spark (~18 Stücke/s gemessen; mit 32 rund ein Zehntel). Dev: ~130 000 Partien, ~800 000
  kommentierte Halbzüge, ~95 Mio. Zeichen → geschätzt 150–250 000 Stücke, ~0,5 GB Vektoren.

---
paths:
  - "**/Models/Book*.cs"
  - "**/Data/AppDbContext.cs"
  - "**/Services/*Book*.cs"
  - "**/Services/Course*.cs"
---

# `SourcePgn` liegt in `BookSource`, nie an `Book`

> Lädt automatisch, sobald eine passende Datei gelesen/bearbeitet wird. Ausgelagert aus `CLAUDE.md` „Wichtige Konventionen“ (2026-10-11).

- **`SourcePgn` liegt in `BookSource` (Tabellensplitting auf `Books`), nie an `Book`** (seit 0.508.3) – Das Roh-PGN
  eines Buchs (Ø ~480 KB, bis 6 MB) hing als Property an `Book` und kam mit JEDEM `.Include(bp => bp.Book)` mit:
  `GET /api/courses/{id}/puzzles` zog 6 MB × 1.881 Linien = 11 GB aus der DB für einen Request, die Prod-API stand
  bei 23 GB RAM. Regeln:
  - `.Include(b => b.Source)` bzw. `_db.BookSources.Where(s => s.Id == id).Select(s => s.SourcePgn)` NUR dort, wo der
    Text wirklich gebraucht wird (Download, Import, Reprocess) — **niemals in Listen-Queries**. Die erlaubten
    Include-Stellen stehen in `BookSourceIncludeGuardTests` (Quelltext-Scan; ein neues Include wird dort rot).
  - Ohne Include ist `book.Source` `null` — solange die BookSource nicht ohnehin im selben Kontext getrackt ist
    (dann setzt der Fixup sie). Mit Include ist sie relational IMMER eine Instanz (Pflicht-Navigation, auch bei
    `SourcePgn = NULL`); unter InMemory nur, wenn die BookSource-Zeile existiert.
  - Jedes `new Book { … }` (auch in Tests) setzt `Source = new BookSource { … }`. EF selbst verlangt das NICHT
    (relational: INSERT ohne die Spalte → `NULL`; InMemory: Include liefert `null` → NullReferenceException, der
    Lösch-Stub in `DeleteBookAsync` wirft `DbUpdateConcurrencyException`) — deshalb wirft `AppDbContext` eine
    `InvalidOperationException`, sobald ein Book ohne Source in den Zustand Added kommt.
  - Schreiben nur über eine GELADENE Source. Löschen: das Buch löschen, die Source geht mit der Zeile.
  - **Fallen (alle still ein `UPDATE Books SET SourcePgn = …`, gegen MariaDB geprüft):** `Source { get; set; } = new();`
    als Initialisierer an `Book` (der naheliegende „Fix" für null = Datenverlust: jedes ohne Include geladene Buch
    bekäme eine leere Added-Source → `SourcePgn = NULL`); `book.Source = new BookSource { … }` an ein geladenes Buch
    hängen (ersetzt den Text); `_db.BookSources.Remove(src)` und `book.Source = null` — KEIN Delete, sondern ein
    Blanking (`SourcePgn = NULL`). Alles nie tun.
  - Rückfallschutz: `BookSourceSplitSqlTests` (SQL der Puzzle-Abfragen enthält kein `SourcePgn`, `Book` hat keine
    `SourcePgn`-Spalte), `BookSourceIncludeGuardTests` (Include-Allowlist) + `BookSourceSplitTests` (MariaDB).

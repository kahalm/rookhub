# Langer Kurs-Kommentar im Fenster (0.696.1)

> Ausgelagert aus `src/frontend/CLAUDE.md` am 2026-10-11. Neue Doku zu diesem Bereich gehört HIERHER.


Im Buch-/Kurs-Löser (`book-puzzle.component`) steht der Kommentar seit 0.696.1 IMMER ganz im Kasten — die frühere
Höhengrenze (40 vh mit eigenem Scrollbalken) ist weg (Wunsch 2026-10-07: „trotzdem alles anzeigen, damit ich nicht klicken
muss"). Ab `LONG_COMMENT_CHARS` (300 Zeichen aller Absätze, `commentLength`) öffnet ein Klick auf den Text (nicht auf einen
Zug-Chip, nicht bei markiertem Text) oder auf „Im Fenster lesen" zusätzlich `CommentDialogComponent`
(`features/puzzles/comment-dialog.component.ts`): Titel + Kapitel/Kurs, größere Schrift, Zeilenlänge 68ch, dieselben
Zug-Chips — ein Zug schließt das Fenster und geht an `previewVariationMove`. Gilt für alle Modi des Buch-Lösers (Kurs, Buch,
Tagespuzzle, Wochenpost).

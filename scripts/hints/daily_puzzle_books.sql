-- Daily-Puzzle-Bücher (17 „1001 Chess Exercises", 40 „Ultimate Chess Puzzle Book",
-- 41 „Checkmate Patterns Manual"): Nach-(Re-)Import-Fixup — NACH JEDEM NEUIMPORT EINSPIELEN.
-- Macht zwei Dinge:
--   1) Spoiler in Kapitel-/Puzzle-Titeln entschärfen (Thema/Taktik-/Mattname verrät die Lösung).
--   2) Vorberechnete Tipps wiederherstellen (Sicherungsnetz; Block wird nach der Generierung angehängt).
-- Matcht per Buchdatei (portabel Dev/Prod). Alle UPDATEs idempotent.
--
-- Anwenden (Prod):
--   docker exec -i rookhub-mariadb mariadb -u<user> -p'<pw>' rookhub < scripts/hints/daily_puzzle_books.sql

-- 1) Kapitelnamen entschärfen → „Chapter N"
--    17 & 41: Format „N. <Thema>" → „Chapter N"
UPDATE BookPuzzles SET Chapter = CONCAT('Chapter ', SUBSTRING_INDEX(Chapter, '.', 1))
  WHERE BookFileName IN ('chessable-u5-21646.pgn','chessable-u5-243265.pgn')
    AND Chapter REGEXP '^[0-9]+[.]';
--    40: Format „Chapter N: <Thema>" → „Chapter N"
UPDATE BookPuzzles SET Chapter = SUBSTRING_INDEX(Chapter, ':', 1)
  WHERE BookFileName = 'chessable-u5-49693.pgn' AND Chapter LIKE 'Chapter %:%';

-- 2) Spoiler-Titel entschärfen
--    40: die 15 Lehr-Titel in Chapter 1 sind reine Taktiknamen (keine Ziffer) → neutralisieren.
--        (Partie-Titel beginnen mit „N) …" und bleiben unberührt.)
UPDATE BookPuzzles SET Title = 'Tactical example'
  WHERE BookFileName = 'chessable-u5-49693.pgn' AND Title NOT REGEXP '[0-9]';
--    41: Titel ohne „vs" sind Mattnamen („Anastasia's Mate") → neutralisieren.
--        (Partie-Titel „A vs. B (Jahr)" bleiben unberührt.)
UPDATE BookPuzzles SET Title = 'Checkmate puzzle'
  WHERE BookFileName = 'chessable-u5-243265.pgn' AND Title NOT LIKE '% vs%';

-- 3) Tipps (wiederherstellen / sichern): wird nach der Generierung angehängt.

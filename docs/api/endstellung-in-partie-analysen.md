# Endstellung in Partie-Analysen (0.689.0)

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Endstellung in Partie-Analysen (0.689.0)“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.

Wunsch 2026-10-06 („die letzte Stellung hat keine Linien"): `GameAnalysisService.CreateAsync` legt nach den Zugzeilen eine
weitere `GameAnalysisPosition` mit `Ply = PlyCount`, der Stellung NACH dem letzten Zug und LEEREM `GameMoveUci`/`GameMoveSan`
an — nur wenn dort noch gezogen werden kann (`FinalPositionOf`, kein Matt/Patt). Bestand nicht nachgezogen (Entscheidung des
Users). Pumpe, Vertiefung und Fertig-Erkennung behandeln sie wie jede Zeile. **Leser, die je Zeile einen Partiezug brauchen,
lassen sie weg** — `p.GameMoveUci != ""` (Punktepartie: alle Abfragen in `GuessSessionService`; Erklärungen, Roast,
Nacherzählung, Taktik-Ernte, Detailliste `GetAsync`, „gleiche Partie") bzw. `Ply < PlyCount` (Zähler `Analyzed` in
`GameEvalsStore`, Restdauer). `GameEvals.FinalOfPlies`: die Bewertung nach dem letzten Zug kommt aus der Endstellung, sonst wie
bisher aus dem gespielten Kandidaten — Kurve, Genauigkeit (Server `GameAccuracy` und Client `evals.final`) nutzen sie. Die
Computer-Linien der Partieseite zeigen sie am letzten Zug von selbst (`computerLinesAt` sucht `ply = currentIndex + 1`).
Neue Abfrage auf `GameAnalysisPositions`, die je Zeile einen Zug erwartet → die Bedingung mitnehmen.

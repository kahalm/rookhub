# Zweite Analyse: Lc0-Spalte + uneinige Züge (0.683.0)

> Ausgelagert aus `src/frontend/CLAUDE.md` am 2026-10-11. Neue Doku zu diesem Bereich gehört HIERHER.


Im Rückblick bei „Beide" (Umschalter seit 0.682.0) bekommt die Genauigkeitstabelle eine eigene Spalte der zweiten
Analyse (`rows()[].altAccuracy`, orange); die Zugklassen bleiben die der ersten. Darunter „Uneinig bei n Zügen":
`features/games/engine-disagreement.util.ts` (rein, Vektoren-Spec) vergleicht die GRUNDklassen — uneinig nur, wenn die
eine Fehler/groben Fehler sieht und die andere höchstens „gut", oder nur eine einen groben Fehler bei ≥ 2 Stufen Abstand.
Bewusst streng: an sechs Prod-Partien (488 Züge) war die Klasse bei 164 verschieden, fast alle an einer Bandgrenze; mit der
Regel blieben 5. Knöpfe/◀ ▶ springen über `moveClicked`, die Kurve trägt dort einen orangen Punkt (`kind: 'disagree'`)
statt des Klassen-Punkts. Im Fehler-Training aus.

**Am Teilen-Link** (0.819.0, Wunsch 2026-10-21: „sollte gleich sein wie bei /games/117 — Standard beim Teilen"): die Seite
gibt `GameReviewComponent` die Kennung (`[sharedToken]`), und die zweite Analyse kommt vom BESITZER —
`GET /api/game-analyses/shared/{token}/alternatives` und `…/alternatives/{id}/evals`, ohne Anmeldung. Die Zugfolge
nimmt der Server aus der verknüpften Analyse der Partie, nicht vom Aufrufer (sonst ließen sich andere Analysen des
Besitzers erfragen); gezeigt werden nur seine Analysen mit ausdrücklich gewählter Engine. Hat der Besitzer keine, sucht
die Leiste für einen angemeldeten Betrachter wie bisher dessen eigene (`same-game`).

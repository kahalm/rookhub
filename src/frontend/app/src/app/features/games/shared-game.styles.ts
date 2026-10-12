/**
 * Stile der Nachspiel-Seite einer Partie (`SharedGameComponent`) — herausgezogen, weil die Komponente über
 * 1 000 Zeilen lang war (Hausregel „große Dateien beim Anfassen verkleinern"); Inhalt unverändert.
 */
export const SHARED_GAME_STYLES = `
    /* Breiter als die übrigen Seiten (0.714.0): Brett, Zugliste und Auswertung stehen nebeneinander. */
    .shared-page { max-width: min(1760px, 96vw); margin: 0 auto; padding: 16px; }
    .center { display: flex; justify-content: center; padding: 40px; }
    .empty-state { display: flex; flex-direction: column; align-items: center; gap: 8px; padding: 32px; text-align: center; }
    .empty-state mat-icon { font-size: 40px; width: 40px; height: 40px; opacity: 0.5; }
    /* Die Karte umschließt ihren Inhalt und steht mittig. Das Brett wächst mit dem Fenster: so hoch, dass
       Kopfzeile und Steuerleiste noch Platz haben, und so breit, dass Zugliste (und am PC die Auswertung) daneben
       passen — nie unter 360 px (gemeldet 2026-09-23: 400 px auf 2250 px Breite) und nie über 760 px. */
    .viewer {
      --board-size: clamp(360px, min(calc(100vh - 260px - var(--recap-room, 0px)), calc(100vw - 440px)), 760px);
      --moves-width: 280px;
      --side-width: clamp(420px, 30vw, 520px);
      width: fit-content; max-width: 100%; margin: 0 auto; padding: 18px 24px 22px; box-sizing: border-box;
      border-radius: 14px;
    }
    /* „Kurz erzählt“ steht über dem Brett — sein Platz geht von der Höhe ab, sonst ragt das Brett samt Leiste unten raus. */
    .viewer:has(> .recap) { --recap-room: 80px; }
    .header {
      display: grid; grid-template-columns: auto minmax(0, 1fr) auto; align-items: center;
      gap: 8px 16px; margin-bottom: 14px;
    }
    .header:not(:has(.back)) { grid-template-columns: minmax(0, 1fr) auto; }
    .header-main { display: flex; flex-direction: column; gap: 4px; min-width: 0; }
    .players { font-size: 1.2rem; line-height: 1.3; }
    .players strong { font-weight: 600; }
    .players .elo { font-weight: 400; font-size: 0.8em; color: color-mix(in srgb, currentColor 60%, transparent); }
    .meta { display: flex; flex-wrap: wrap; align-items: center; gap: 6px; font-size: 0.82rem; }
    .meta > span {
      padding: 1px 8px; border-radius: 999px;
      background: color-mix(in srgb, currentColor 7%, transparent);
      color: color-mix(in srgb, currentColor 75%, transparent);
    }
    .meta > .result { background: color-mix(in srgb, var(--rh-accent) 16%, transparent); color: var(--rh-accent); font-weight: 600; }
    .header-actions { display: flex; align-items: center; justify-content: flex-end; gap: 8px; flex-wrap: wrap; }
    /* Breite 0 + Mindestbreite 100 %: der Satz trägt nichts zur Breite der Karte bei — sie umschließt Brett und Zugliste,
       ein langer Absatz dehnte sie sonst auf die ganze Seite. */
    .recap {
      width: 0; min-width: 100%; box-sizing: border-box; margin: 0 0 14px; padding: 10px 14px;
      display: flex; gap: 10px; align-items: flex-start; border-radius: 10px;
      background: color-mix(in srgb, var(--rh-accent) 7%, transparent);
      font-size: 0.92rem; line-height: 1.5; color: color-mix(in srgb, currentColor 85%, transparent);
    }
    .recap.roast { background: color-mix(in srgb, #e64a19 9%, transparent); white-space: pre-wrap; }
    .roast-icon { flex: 0 0 auto; font-size: 18px; line-height: 1.3; }
    .recap mat-icon { flex: 0 0 auto; font-size: 20px; width: 20px; height: 20px; margin-top: 1px; color: var(--rh-accent); }
    .original, .analyze { white-space: nowrap; }
    /* Am Handy wandern „Originalpartie öffnen" und „Teilen-Link kopieren" ins ⋮ (v1-rk-first-view-lead-long-mobile). */
    .narrow-only { display: none; }
    /* UI-Sweep 2026-10-11 (r2-rg-analyze-disabled): eigene Zeile unter den Knöpfen, leise. */
    .analyze-no-engine { flex: 1 1 100%; margin: -2px 0 0; text-align: right; font-size: 12px;
      color: color-mix(in srgb, currentColor 60%, transparent); }
    .body { display: flex; flex-wrap: wrap; gap: 20px; align-items: flex-start; }
    .side-section { display: flex; flex-direction: column; gap: 12px; flex: 1 1 100%; min-width: 0; }
    /* Die drei Blöcke rechts als ruhige Karten mit gleichem Rahmen; ein leerer Rückblick (noch keine Analyse) fällt weg. */
    .side-section > .review-slot,
    .side-section > .pr-slot,
    .side-section > .similar-slot {
      box-sizing: border-box; border-radius: 10px;
      border: 1px solid color-mix(in srgb, currentColor 12%, transparent);
    }
    .side-section > .review-slot { padding: 12px 14px; }
    .side-section > .review-slot:empty, .side-section > .pr-slot:empty { display: none; }
    /* Breit genug für drei Spalten: ein FESTES Raster (Brett | Zugliste | Auswertung). Die Spaltenbreiten hängen
       nicht am Inhalt — vorher wuchs die Karte, sobald die Auswertung erschien, und lange Linien wurden rechts
       abgeschnitten (gemeldet 2026-10-10). */
    @media (min-width: 1280px) {
      .viewer { --board-size: clamp(360px, min(calc(100vh - 250px - var(--recap-room, 0px)), calc(100vw - var(--moves-width) - var(--side-width) - 140px)), 760px); }
      .body {
        display: grid; flex-wrap: nowrap; gap: 20px;
        grid-template-columns: var(--board-size) var(--moves-width) var(--side-width);
      }
      .side-section {
        max-height: calc(var(--board-size) + 56px); overflow-y: auto; overflow-x: hidden;
        scrollbar-gutter: stable; padding-right: 2px;
      }
    }
    .board-section { width: var(--board-size); display: flex; flex-direction: column; align-items: center; gap: 8px; flex-shrink: 0; }
    .board-wrap { position: relative; width: var(--board-size); }
    .board-wrap app-chess-board { display: block; width: var(--board-size); }
    .board-tapnav { -webkit-user-select: none; user-select: none; -webkit-touch-callout: none; }
    /* Reihenfolge überall (seit 0.667.0 auch am PC, vorher nur am Handy): Drehen · Anfang ‖ ◀ ▶ ‖ Ende · Live · ⋮ —
       die häufigen Knöpfe groß in der Mitte, die Sprünge mit Abstand daneben: dicht nebeneinander traf man statt
       „einen Zug" oft „ganz an den Anfang/das Ende" (gemeldet 2026-09-24 am Handy, 2026-10-05 am PC). */
    .nav { display: flex; align-items: center; gap: 4px; }
    .nav .nav-flip { order: 0; }
    .nav .nav-start { order: 1; margin-right: 20px; opacity: 0.7; }
    .nav .nav-prev { order: 2; }
    .nav .nav-next { order: 3; }
    .nav .nav-end { order: 4; margin-left: 20px; opacity: 0.7; }
    .nav .live-toggle { order: 5; }
    .nav .nav-menu { order: 6; }
    .nav .nav-prev, .nav .nav-next {
      width: 64px; height: 44px; border-radius: 10px; overflow: hidden;
      background: color-mix(in srgb, currentColor 8%, transparent);
    }
    .nav .nav-prev mat-icon, .nav .nav-next mat-icon { font-size: 30px; width: 30px; height: 30px; }
    .pr-slot, .review-slot, .trainer-slot, .live-slot, .similar-slot { display: block; width: 100%; }
    .live-toggle.on { color: #42a5f5; }
    /* Die Zugliste ist so hoch wie das Brett und scrollt in sich; eine feste Breite, damit die zwei Zugspalten
       nebeneinander stehen statt — bei einer Spalte, die den Rest der Karte füllt — mit einer Handbreit Luft
       dazwischen. */
    .moves-section {
      width: var(--moves-width); height: var(--board-size); flex-shrink: 0; box-sizing: border-box;
      border: 1px solid color-mix(in srgb, currentColor 12%, transparent); border-radius: 10px; overflow: auto;
    }
    @media (max-width: 768px) {
      .shared-page { padding: 0; }
      .viewer { width: auto; padding: 0; border-radius: 0; }
      .header { position: relative; grid-template-columns: auto minmax(0, 1fr); padding: 12px 52px 12px 16px; margin-bottom: 4px; }
      /* Eine Zeile statt einer 48-px-Zeile je Knopf (v1-rk-first-view-lead-long-mobile, DESIGN 4.6 Regel 3): das
         Brett begann sonst erst bei 49 % der Höhe. Selten Gebrauchtes steht am Handy im ⋮ (.wide-only/.narrow-only). */
      .header-actions { grid-column: 1 / -1; flex-direction: row; flex-wrap: wrap; align-items: center; justify-content: flex-start; }
      .header-actions > .analyze, .header-actions > .mistakes { flex: 1 1 auto; }
      .wide-only { display: none; }
      .narrow-only { display: flex; }
      .analyze-no-engine { text-align: left; }
      /* Nur das ⋮ (es sitzt absolut oben rechts): die leere Zeile samt Abstand fällt weg. */
      .header-actions:not(:has(> button:not(.game-menu):not(.wide-only), > a:not(.wide-only))) { margin-top: -8px; }
      /* Das ⋮ oben rechts neben den Namen statt allein in einer eigenen Zeile. */
      .header .game-menu { position: absolute; top: 8px; right: 8px; }
      .recap { margin: 0 16px 12px; width: auto; min-width: 0; cursor: pointer; }
      /* „Kurz erzählt" höchstens drei Zeilen (4.6 Regel 5), Antippen zeigt alles. */
      .recap:not(.open) > .recap-text {
        display: -webkit-box; -webkit-line-clamp: 3; -webkit-box-orient: vertical; overflow: hidden;
      }
      .side-section { padding: 0 12px; }
      /* nowrap: ein mehrzeiliger Spalten-Flex wird so breit wie sein breitester Inhalt (Genauigkeitstabelle) —
         die Seite war am Handy 438 statt 390 px breit (v1-rk-analysis-card-overflow-mobile). */
      .body { flex-direction: column; flex-wrap: nowrap; align-items: stretch; }
      .side-section { order: 1; }   /* am Handy: Auswertung vor der Zugliste, wie bisher */
      .moves-section { order: 2; }
      .board-section { width: 100%; max-width: 100%; align-items: center; }
      .board-wrap { width: 100%; }
      .board-wrap app-chess-board { width: 100%; }
      /* Reihenfolge am Handy: Drehen · Anfang ‖ ◀ ▶ ‖ Ende · Live — die häufigen Knöpfe groß in der Mitte, die
         Sprünge an den Rand mit Abstand, damit ein daneben getroffener Tipp nicht die ganze Partie überspringt. */
      .nav { width: 100%; box-sizing: border-box; padding: 6px 8px; }
      .nav .nav-start { margin-right: 14px; }
      .nav .nav-end { margin-left: 14px; }
      .nav .nav-prev, .nav .nav-next { flex: 1 1 0; width: auto; height: 48px; }
      .nav .nav-prev mat-icon, .nav .nav-next mat-icon { font-size: 32px; width: 32px; height: 32px; }
      .moves-section {
        width: 100%; height: auto; max-height: 40vh;
        border-left: none; border-right: none; border-radius: 0; border-bottom: none;
      }
    }
`;

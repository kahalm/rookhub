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
    .empty-state p { margin: 0; }
    /* a1-rk-vereinspartie-fehler-sackgasse: Weg zurück als Knopfreihe (DESIGN 5.11 Sperrkarte); Ladefehler mit „Erneut versuchen". */
    .gate-actions { display: flex; flex-wrap: wrap; justify-content: center; gap: 8px; margin-top: 8px; }
    .load-card { padding: 8px 24px; }
    /* b1-rk-vereinspartie-sperrkarte-ultrawide: Sperr-/Fehlerkarte bleibt bei der Seitenbreite (DESIGN 4.1/5.11) — die
       breite Hülle (bis 2 560 px) ist nur für Brett + Zugliste + Auswertung gedacht. */
    .shared-page > .empty-state, .shared-page > .load-card {
      max-width: min(var(--page-max-width, 1240px), 96vw); margin-inline: auto; box-sizing: border-box;
    }
    /* Die Karte umschließt ihren Inhalt und steht mittig. Das Brett wächst mit dem Fenster: so hoch, dass
       Kopfzeile und Steuerleiste noch Platz haben, und so breit, dass Zugliste (und am PC die Auswertung) daneben
       passen — nie unter 360 px (gemeldet 2026-09-23: 400 px auf 2250 px Breite) und nie über 760 px. */
    .viewer {
      /* 400 px = Zugliste 280 + Abstand 20 + Kartenpolster 48 + Seitenpolster 32 + Rollbalken ≈ 20 (a1-rk-partie-tablet-brett:
         mit 440 px blieb das Brett am Tablet bei ~380 px und rechts ein Streifen der Karte leer). */
      --board-size: clamp(360px, min(calc(100vh - 260px - var(--recap-room, 0px)), calc(100vw - 400px)), 760px);
      --moves-width: 280px;
      --side-width: clamp(420px, 30vw, 520px);
      width: fit-content; max-width: 100%; margin: 0 auto; padding: 18px 24px 22px; box-sizing: border-box;
      border-radius: 14px;
    }
    /* „Kurz erzählt“ steht über dem Brett — sein Platz geht von der Höhe ab, sonst ragt das Brett samt Leiste unten raus. */
    .viewer:has(> app-game-recap.has-text) { --recap-room: 80px; }
    .header {
      display: grid; grid-template-columns: auto minmax(0, 1fr) auto; align-items: center;
      gap: 8px 16px; margin-bottom: 14px;
    }
    .header:not(:has(.back)) { grid-template-columns: minmax(0, 1fr) auto; }
    .header-main { display: flex; flex-direction: column; gap: 4px; min-width: 0; }
    .players { font-size: 1.2rem; line-height: 1.3; }
    .players strong { font-weight: 600; overflow-wrap: anywhere; }
    /* a1-rk-elo-verwaist: Name und Elo brechen nur gemeinsam um, „(Elo 1689)" nie in sich (DESIGN 5.13). */
    .players .pl { display: inline-block; max-width: 100%; }
    .players .elo { font-weight: 400; font-size: 0.8em; white-space: nowrap; color: color-mix(in srgb, currentColor 60%, transparent); }
    .meta { display: flex; flex-wrap: wrap; align-items: center; gap: 6px; font-size: 0.82rem; }
    /* nowrap: „1-0" und das Datum brachen am Tablet in sich um (a1-rk-partie-kopf-tablet). */
    .meta > span {
      white-space: nowrap;
      padding: 1px 8px; border-radius: 999px;
      background: color-mix(in srgb, currentColor 7%, transparent);
      color: color-mix(in srgb, currentColor 75%, transparent);
    }
    .meta > .result { background: color-mix(in srgb, var(--rh-accent) 16%, transparent); color: var(--rh-accent); font-weight: 600; }
    .header-actions { display: flex; align-items: center; justify-content: flex-end; gap: 8px; flex-wrap: wrap; }
    /* d3-rk-partie-grund-neben-knopf-versetzt: mit dem Grund unter „Partie analysieren“ ist die Hülle ~30 px höher als
       die Nachbarn; mittig ausgerichtet stand der Knopf ~15 px über „Teilen-Link kopieren“ und dem ⋮. Dann oben
       bündig — ohne Grundzeile bleibt alles mittig zum Titel. */
    .header-actions:has(.analyze-no-engine) { align-items: flex-start; }
    /* „Kurz erzählt“ samt Handy-Kürzung: game-recap.component.ts. */
    .original, .analyze { white-space: nowrap; }
    /* Am Handy wandern „Originalpartie öffnen" und „Teilen-Link kopieren" ins ⋮ (v1-rk-first-view-lead-long-mobile). */
    .narrow-only { display: none; }
    /* UI-Sweep 2026-10-11 (r2-rg-analyze-disabled): der Grund leise unter dem gesperrten Knopf. Seit a1-rk-shared-kopf-grund
       in einer eigenen Hülle mit dem Knopf (höchstens 280 px breit): das ⋮ bleibt in der Knopfzeile, die Kopfspalte schmal. */
    /* b1-rk-partie-grund-unter-knopf: die Hülle ist so breit wie der Knopf (mindestens 220 px), nicht wie der Satz —
       der Grund nimmt keine eigene Breite (width 0 + min-width 100 %) und bricht ausgewogen (keine Waise „later.“);
       so steht das ⋮ direkt neben dem Knopf statt ~120 px abgesetzt. */
    .analyze-wrap { display: flex; flex-direction: column; align-items: flex-end; gap: 2px; min-width: 220px; max-width: 280px; }
    .analyze-no-engine { margin: 0; text-align: right; font-size: 12px; width: 0; min-width: 100%; text-wrap: balance;
      color: color-mix(in srgb, currentColor 60%, transparent); }
    /* a1-rk-partie-kopf-tablet: zwischen Handy und PC stehen die Knöpfe in einer eigenen Zeile unter dem Titel — als
       dritte Rasterspalte fraßen sie fast die ganze Breite, der Titel brach Wort für Wort. */
    @media (min-width: 769px) and (max-width: 1279.98px) {
      .header { grid-template-columns: auto minmax(0, 1fr); }
      .header:not(:has(.back)) { grid-template-columns: minmax(0, 1fr); }
      .header-actions { grid-column: 1 / -1; justify-content: flex-start; align-items: flex-start; }
      /* Nachprüfung a1: steht dort nur das ⋮ (Vereinspartie), bleibt es rechts neben dem Titel statt eine eigene Zeile zu belegen. */
      .header:not(:has(.header-actions > :not(.game-menu, mat-menu))) { grid-template-columns: auto minmax(0, 1fr) auto; }
      .header:not(:has(.back)):not(:has(.header-actions > :not(.game-menu, mat-menu))) { grid-template-columns: minmax(0, 1fr) auto; }
      .header-actions:not(:has(> :not(.game-menu, mat-menu))) { grid-column: auto; }
      /* b1-rk-partie-grund-unter-knopf (Nachprüfung): die Mindestbreite 220 px machte die Hülle breiter als den Knopf —
         das ⋮ stand ~65 px rechts der Knopfkante. Hier so breit wie der Knopf, der Grund bricht darunter in seiner
         Breite; das ⋮ rückt auf 4 px an den Knopf heran. */
      .analyze-wrap { align-items: flex-start; width: max-content; min-width: 0; }
      .analyze-wrap + .game-menu { margin-left: -4px; }
      .analyze-no-engine { text-align: left; }
    }
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
    /* u1-rk-partie-brett: ab 2400 px vier Spalten — Brett bis 1 080 px (höchstens Fensterhöhe), Zugliste 280 px, die
       Auswertung zweispaltig (Kurve/Fehler | Repertoire/Meisterpartien, je 400–520 px). Breite: Brett + Zugliste +
       2 × Seitenspalte + 140 px Abstände/Ränder ≤ 96vw bzw. 2 560 px. */
    @media (min-width: 2400px) {
      .shared-page { max-width: min(2560px, 96vw); }
      .viewer {
        --side-width: clamp(400px, 16vw, 520px);
        --board-size: clamp(360px, min(calc(100vh - 250px - var(--recap-room, 0px)), calc(96vw - var(--moves-width) - 2 * var(--side-width) - 140px)), 1080px);
      }
      .body { grid-template-columns: var(--board-size) var(--moves-width) calc(2 * var(--side-width) + 20px); }
      /* Zweite Zeile flexibel (a1-rk-ultrawide-seitenspalten): die Überhöhe der überspannenden Auswertung geht in sie,
         „Ähnliche Meisterpartien" steht direkt unter „In welchen Repertoires?" statt ~90 px tiefer. */
      .side-section {
        display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 1fr); grid-template-rows: min-content 1fr;
        align-content: start; align-items: start; gap: 12px 20px;
      }
      .side-section > .review-slot { grid-column: 1; grid-row: 1 / span 2; }
      .side-section > .pr-slot, .side-section > .similar-slot { grid-column: 2; }
      /* Noch keine Auswertung: Repertoire und Meisterpartien nebeneinander statt einer leeren linken Hälfte. */
      .side-section:has(> .review-slot:empty) > .pr-slot,
      .side-section:has(> .review-slot:empty) > .similar-slot { grid-column: auto; }
    }
    .board-section { width: var(--board-size); min-width: 0; display: flex; flex-direction: column; align-items: center; gap: 8px; flex-shrink: 0; }
    .board-wrap { position: relative; width: var(--board-size); }
    .board-wrap app-chess-board { display: block; width: var(--board-size); }
    .board-tapnav { -webkit-user-select: none; user-select: none; -webkit-touch-callout: none; }
    /* Reihenfolge überall (seit 0.667.0 auch am PC, vorher nur am Handy): Drehen · Anfang ‖ ◀ ▶ ‖ Ende · Live · ⋮ —
       die häufigen Knöpfe groß in der Mitte, die Sprünge mit Abstand daneben: dicht nebeneinander traf man statt
       „einen Zug" oft „ganz an den Anfang/das Ende" (gemeldet 2026-09-24 am Handy, 2026-10-05 am PC). */
    /* a1-rk-brettleiste-plus / a1-rk-partie-tablet-brett: seit dem „+" (0.749.0) acht Knöpfe — die Leiste ist nie breiter als
       das Brett: ◀ ▶ dürfen bis 44 px schrumpfen, die Sprung-Abstände wachsen mit dem Brett (6–20 px). Rechnung mit den
       40-px-Nebenknöpfen (Material, Tippfläche 48 px): 6 × 40 + 2 × 44 + 2 × 6 + 7 × 2 = 354 px ≤ Brett ab 360 px. */
    .nav { display: flex; align-items: center; justify-content: center; gap: 2px; max-width: 100%; box-sizing: border-box; }
    .nav .nav-flip { order: 0; }
    .nav .nav-start { order: 1; margin-right: clamp(6px, calc(var(--board-size) / 10 - 30px), 20px); opacity: 0.7; }
    .nav .nav-prev { order: 2; }
    .nav .nav-next { order: 3; }
    .nav .nav-end { order: 4; margin-left: clamp(6px, calc(var(--board-size) / 10 - 30px), 20px); opacity: 0.7; }
    .nav .live-toggle { order: 5; }
    .nav .nav-menu { order: 6; }
    .nav .nav-prev, .nav .nav-next {
      flex: 0 1 64px; width: 64px; min-width: 44px; height: 44px; border-radius: 10px; overflow: hidden;
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
      /* b1-rk-partie-zugliste-schatten: Weiterscroll-Schatten am unteren Rand, nur solange noch Züge folgen (DESIGN 5.3,
         skriptlos wie die Repertoire-Linienliste). Die Abdeckung (local) wandert mit dem Inhaltsende und hat die
         Kartenfarbe des .viewer; der Schatten (scroll) bleibt am Kastenrand. */
      background:
        linear-gradient(to bottom, transparent, var(--mat-sys-surface-container-low) 70%) center bottom / 100% 32px no-repeat local,
        radial-gradient(farthest-side at 50% 100%, color-mix(in srgb, currentColor 30%, transparent), transparent)
          center bottom / 100% 14px no-repeat scroll;
    }
    /* b1-rk-partie-zugliste-schatten (Nachprüfung): die innere .move-list (height 100 %, overflow auto) war der eigentliche
       Scroller, der Schatten auf .moves-section griff so nur am Handy. Als Block ohne feste Höhe bleibt die Liste so hoch
       wie ihr Inhalt — es scrollt .moves-section samt Schattenmuster (Laptop, PC, Breitbild). */
    .moves-section > app-move-list { display: block; }
    @media (max-width: 768px) {
      .shared-page { padding: 0; }
      .viewer { width: auto; padding: 0; border-radius: 0; }
      .header { position: relative; grid-template-columns: auto minmax(0, 1fr); padding: 12px 52px 12px 16px; margin-bottom: 4px; }
      /* Eine Zeile statt einer 48-px-Zeile je Knopf (v1-rk-first-view-lead-long-mobile, DESIGN 4.6 Regel 3): das
         Brett begann sonst erst bei 49 % der Höhe. Selten Gebrauchtes steht am Handy im ⋮ (.wide-only/.narrow-only). */
      .header-actions { grid-column: 1 / -1; flex-direction: row; flex-wrap: wrap; align-items: center; justify-content: flex-start; }
      .header-actions > .analyze-wrap, .header-actions > .mistakes { flex: 1 1 auto; }
      .analyze-wrap { align-items: stretch; max-width: none; }
      .wide-only { display: none; }
      .narrow-only { display: flex; }
      .analyze-no-engine { text-align: left; }
      /* Nur das ⋮ (es sitzt absolut oben rechts): die leere Zeile samt Abstand fällt weg. */
      .header-actions:not(:has(> .analyze-wrap, > button:not(.game-menu):not(.wide-only), > a:not(.wide-only))) { margin-top: -8px; }
      /* Das ⋮ oben rechts neben den Namen statt allein in einer eigenen Zeile. */
      .header .game-menu { position: absolute; top: 8px; right: 8px; }
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
      .nav .nav-start { margin-right: 6px; }
      .nav .nav-end { margin-left: 6px; }
      .nav .nav-prev, .nav .nav-next { flex: 1 1 0; width: auto; height: 48px; }
      .nav .nav-prev mat-icon, .nav .nav-next mat-icon { font-size: 32px; width: 32px; height: 32px; }
      .moves-section {
        width: 100%; height: auto; max-height: 40vh;
        border-left: none; border-right: none; border-radius: 0; border-bottom: none;
      }
    }
    /* a1-rk-brettleiste-plus: am Handy rücken die Nebenknöpfe auf 36 px zusammen (wie im Analysebrett), damit ◀ ▶ je
       ≥ 56 px breit bleiben — mit dem „+" waren es bei 390 px nur ~30 px. */
    @media (max-width: 480px) {
      .nav ::ng-deep .mat-mdc-icon-button:not(.nav-prev):not(.nav-next) {
        width: 36px; height: 36px; padding: 6px; --mat-icon-button-state-layer-size: 36px;
      }
      .nav ::ng-deep .mat-mdc-icon-button:not(.nav-prev):not(.nav-next) .mat-mdc-button-touch-target { width: 36px; height: 36px; }
    }
`;

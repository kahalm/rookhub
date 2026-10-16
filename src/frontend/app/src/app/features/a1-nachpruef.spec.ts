import { Type } from '@angular/core';
import { GamesListComponent } from './games/games-list.component';
import { LeaderboardsComponent } from './leaderboards/leaderboards.component';
import { SharedGameComponent } from './games/shared-game.component';
import { GameAnalysesComponent } from './analysis/game-analyses.component';

/** Nachprüfung der a1-Umsetzung (Lauf a1-nachpruef, 0.763.0): Rest- und Folgefehler, gelesen am kompilierten CSS. */
function css(cmp: Type<unknown>): string {
  return (((cmp as unknown as { ɵcmp: { styles?: string[] } }).ɵcmp.styles) ?? []).join('\n').replace(/%NS%/g, '').replace(/\s/g, '');
}

describe('Nachprüfung a1 (0.763.1)', () => {
  it('Partien-Leerkarte: nur das große Symbol oben wird 40 px, nicht das Symbol im Knopf', () => {
    const c = css(GamesListComponent);
    expect(c).toMatch(/\.empty-state(\[[^\]]*\])?>mat-icon(\[[^\]]*\])?\{font-size:40px/);
    expect(c).not.toMatch(/\.empty-state(\[[^\]]*\])?mat-icon/);
  });

  // b1-rk-bestenlisten-namen-laptop: die breite Seite greift schon ab 1 280 px (Laptop 1 366 px kürzte die Namen noch).
  it('Bestenlisten: vier Spalten ab 1 100 px, Seite ab 1 280 px bis 1 400 px breit (Namen nicht gekürzt)', () => {
    const c = css(LeaderboardsComponent);
    expect(c).toMatch(/@media\(min-width:1280px\)\{\.lb-page(\[[^\]]*\])?\{max-width:min\(1400px,96vw\)/);
  });

  it('Partie-Kopf am Tablet: steht nur das ⋮, bleibt es neben dem Titel (keine eigene Zeile)', () => {
    const c = css(SharedGameComponent);
    expect(c).toContain(':not(:has(>:not(.game-menu,mat-menu)))');
    expect(c).toMatch(/grid-template-columns:auto minmax\(0,1fr\)auto|grid-template-columns:autominmax\(0,1fr\)auto/);
  });

  // Falz-Runde 16.10.: die Einleitung steht jetzt in der Formularkarte (0 0 12px), den Abstand unter der Knopfzeile hält
  // der Kopf selbst (margin-bottom 12px).
  it('Partie-Analysen: 12 px zwischen Knopfzeile und Inhalt', () => {
    expect(css(GameAnalysesComponent)).toMatch(/\.header(\[[^\]]*\])?\{margin-bottom:12px/);
    expect(css(GameAnalysesComponent)).toMatch(/\.intro(\[[^\]]*\])?\{margin:0012px/);
  });
});

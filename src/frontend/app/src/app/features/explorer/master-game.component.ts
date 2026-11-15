import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslatePipe } from '@ngx-translate/core';
import { LoadErrorComponent } from '../../shared/load-error/load-error.component';
import { pgnHeaderText } from '../../shared/pgn-viewer/pgn-header.util';
import { SummaryGameBoardComponent, SummaryJump } from '../games/summary/summary-game-board.component';

/** Antwort von `GET /api/explorer/master-games/{id}`. */
export interface ExplorerMasterGame { id: string; pgn: string; }

/** Kennung einer Partie im Explorer — dieselbe Regel wie der Server (`LocalExplorerClient.IsGameId`). */
export const MASTER_GAME_ID = /^[A-Za-z0-9]{8}$/;

/** Kopfzeilen eines PGN als Tabelle (`[White "Carlsen, Magnus"]`); Platzhalter wie „?“ zählen als fehlend. */
export function masterGameHeaders(pgn: string): Record<string, string> {
  const out: Record<string, string> = {};
  for (const m of pgn.matchAll(/^\[(\w+)\s+"((?:[^"\\]|\\.)*)"\]\s*$/gm)) {
    const value = pgnHeaderText(m[2].replace(/\\(["\\])/g, '$1'));
    if (value) out[m[1]] = value;
  }
  return out;
}

/**
 * „Meisterpartie“ (0.980.0, `/explorer/game/masters/:id`): eine Partie aus dem lokalen Explorer zum Nachspielen — das
 * Linkziel der Beispielpartien in der Partie-Zusammenfassung („Kosakowski – Nie (2025)“). Ohne Anmeldung, weil die
 * Zusammenfassung auch über Teilen-Links gelesen wird; die Adresse trägt nur die Kennung der Meisterpartie, nichts über
 * die Partie, von der man kommt. `?ply=` öffnet die Stellung nach so vielen Halbzügen.
 *
 * Brett, Blättern, Zugliste, Engine und „+“ kommen vom Seitenbrett der Zusammenfassung (`SummaryGameBoardComponent`).
 */
@Component({
  selector: 'app-master-game',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, MatButtonModule, MatCardModule, MatIconModule, MatProgressSpinnerModule, TranslatePipe, LoadErrorComponent, SummaryGameBoardComponent],
  template: `
    <div class="master-game-page">
      @if (loading()) {
        <div class="center"><mat-spinner diameter="40"></mat-spinner></div>
      } @else if (notFound()) {
        <mat-card class="empty-state not-found">
          <mat-icon>link_off</mat-icon>
          <p>{{ 'games.masterGame.notFound' | translate }}</p>
          <div class="actions"><a mat-stroked-button routerLink="/" class="to-home">{{ 'app.notFound.home' | translate }}</a></div>
        </mat-card>
      } @else if (failed()) {
        <mat-card class="load-card"><app-load-error [messageKey]="'games.loadError'" (retry)="load()" /></mat-card>
      } @else if (game(); as g) {
        <mat-card class="head-card">
          <h1 class="players"><strong>{{ head().white || '?' }}</strong>@if (head().whiteElo) { <span class="elo">({{ head().whiteElo }})</span> }
            – <strong>{{ head().black || '?' }}</strong>@if (head().blackElo) { <span class="elo">({{ head().blackElo }})</span> }
            @if (head().result) { <span class="result">{{ head().result }}</span> }</h1>
          <p class="meta">{{ 'games.masterGame.title' | translate }}@if (head().meta) { · {{ head().meta }} }</p>
        </mat-card>
        <div class="board"><app-summary-game-board [pgn]="g.pgn" [jump]="jump" /></div>
      }
    </div>
  `,
  styles: [`
    .master-game-page { max-width: 720px; margin: 0 auto; padding: 16px; }
    .center { display: flex; justify-content: center; padding: 48px; }
    .head-card, .load-card { padding: 16px; }
    @media (max-width: 599.98px) { .head-card, .load-card { padding: 12px; } }
    .players { font-size: 1.1rem; font-weight: 400; margin: 0; overflow-wrap: anywhere; }
    .elo { color: var(--mat-sys-on-surface-variant); margin-left: 4px; }
    .result { margin-left: 8px; white-space: nowrap; }
    .meta { color: var(--mat-sys-on-surface-variant); font-size: 0.875rem; margin: 4px 0 0; overflow-wrap: anywhere; }
    .actions { display: flex; justify-content: center; margin-top: 8px; }
    .board { margin-top: 16px; }
  `],
})
export class MasterGameComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly http = inject(HttpClient);

  readonly loading = signal(true);
  readonly failed = signal(false);
  readonly notFound = signal(false);
  readonly game = signal<ExplorerMasterGame | null>(null);
  /** Startstellung aus `?ply=` (Halbzüge ab Partieanfang); ohne den Parameter die Anfangsstellung. */
  jump: SummaryJump | null = null;

  /** Kopf der Seite aus den Kopfzeilen des PGN: Spieler, Elo, Ergebnis, „Turnier · Ort · Jahr“. */
  readonly head = computed(() => {
    const h = masterGameHeaders(this.game()?.pgn ?? '');
    const result = h['Result'] === '1/2-1/2' ? '½–½' : (h['Result'] ?? '').replace('-', '–');
    const year = /^\d{4}/.exec(h['Date'] ?? '')?.[0] ?? '';
    return {
      white: h['White'] ?? '', black: h['Black'] ?? '', whiteElo: h['WhiteElo'] ?? '', blackElo: h['BlackElo'] ?? '',
      result: result === '*' ? '' : result,
      meta: [h['Event'], h['Site'], year].filter(Boolean).join(' · '),
    };
  });

  ngOnInit(): void {
    const ply = Number(this.route.snapshot.queryParamMap.get('ply'));
    if (Number.isInteger(ply) && ply > 0) this.jump = { ply };
    this.load();
  }

  load(): void {
    const id = this.route.snapshot.paramMap.get('id') ?? '';
    this.failed.set(false);
    this.notFound.set(false);
    // Eine Kennung, die der Server ohnehin ablehnt, fragt gar nicht erst an.
    if (!MASTER_GAME_ID.test(id)) { this.notFound.set(true); this.loading.set(false); return; }
    this.loading.set(true);
    this.http.get<ExplorerMasterGame>(`/api/explorer/master-games/${id}`).subscribe({
      next: g => { this.game.set(g); this.loading.set(false); },
      error: (e: { status?: number } | null) => {
        // 400/404 = gibt es nicht (kein „Wiederholen“); 503/Netz/429 = Explorer gerade weg, erneut versuchen hilft.
        const status = e?.status ?? 0;
        (status === 404 || status === 400 ? this.notFound : this.failed).set(true);
        this.loading.set(false);
      },
    });
  }
}

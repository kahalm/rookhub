import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth.service';
import { HandoffService } from '../../core/handoff.service';
import { SnackbarService } from '../../core/snackbar.service';
import { downloadBlob } from '../../shared/download.util';
import { GamesService } from '../games/games.service';
import { AnalysisGameDialogComponent, ClubChoice, GameActionKind, GameDialogData, GameDialogResult } from './analysis-game-dialog.component';
import { detailsFromHeaders, GameDetails, gameFileName, isStandardStart, treeToPgn } from './analysis-game-pgn';
import { AnalysisNode } from './analysis-tree';

/** Die Vereine des Kontos, `GET /api/league/me`. */
interface LeagueMe { clubs: ClubChoice[]; current: number | null; }

/**
 * „PGN herunterladen", „In Meine Partien", „In die Vereinsdatenbank" am Analysebrett (Wunsch 2026-11-09).
 *
 * Die Vereinsdatenbank bekommt die Partie NICHT direkt: das PGN wird als gewöhnlicher Entwurf abgelegt (`POST
 * /api/league/club/drafts`, wie jede andere Liste in LeagueHub) und LeagueHub öffnet ihn über seine Nummer in der bekannten
 * Übersicht — Spieler zuordnen, Duplikate, Importieren. Das Ziel kommt aus der eigenen Host-Tabelle (`leagueHubUrl`), eine
 * Rückkehr-Adresse von außen gibt es nicht; in der URL steht nur die Nummer.
 */
@Component({
  selector: 'app-analysis-game-actions',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatIconModule, TranslatePipe],
  template: `
    @if (clubEntry()) {
      <p class="entry" role="status">{{ (canClub ? 'analysis.gameActions.entryHint' : 'analysis.gameActions.entryNoRight') | translate }}</p>
    }
    <div class="actions">
      @if (canClub && clubEntry()) {
        <button mat-flat-button color="primary" type="button" [disabled]="!clubReady()" (click)="run('club')"><mat-icon>groups</mat-icon> {{ 'analysis.gameActions.club' | translate }}</button>
      }
      <button mat-stroked-button type="button" [disabled]="!hasMoves()" (click)="run('download')"><mat-icon>download</mat-icon> {{ 'common.downloadPgn' | translate }}</button>
      @if (auth.isLoggedIn) {
        <button mat-stroked-button type="button" [disabled]="!hasMoves()" (click)="run('save')"><mat-icon>save</mat-icon> {{ 'analysis.gameActions.save' | translate }}</button>
      }
      @if (canClub && !clubEntry()) {
        <button mat-stroked-button type="button" [disabled]="!clubReady()" (click)="run('club')"><mat-icon>groups</mat-icon> {{ 'analysis.gameActions.club' | translate }}</button>
      }
    </div>
    @if (!hasMoves()) {
      <p class="hint">{{ 'analysis.gameActions.needMoves' | translate }}</p>
    } @else if (canClub && !standardStart()) {
      <p class="hint">{{ 'analysis.gameActions.clubNeedsStart' | translate }}</p>
    }
  `,
  styles: [`
    .actions { display: flex; flex-wrap: wrap; gap: 8px; margin-bottom: 8px; }
    .hint { color: var(--mat-sys-on-surface-variant); font-size: 13px; margin: 0 0 8px; }
    .entry { margin: 0 0 8px; font-weight: 500; }
  `],
})
export class AnalysisGameActionsComponent {
  readonly auth = inject(AuthService);
  private readonly handoff = inject(HandoffService);
  private readonly games = inject(GamesService);
  private readonly http = inject(HttpClient);
  private readonly dialog = inject(MatDialog);
  private readonly router = inject(Router);
  private readonly snackbar = inject(SnackbarService);
  private readonly translate = inject(TranslateService);

  readonly root = input.required<AnalysisNode>();
  /** Zählt Änderungen am Baum (der Knoten bleibt derselbe, wenn nur Züge dazukommen). */
  readonly version = input(0);
  /** Kopfdaten des geladenen PGN — Vorbelegung des Dialogs. */
  readonly headers = input<Record<string, string> | null>(null);
  /** Aus LeagueHub gekommen (`?verein=1`): das Übernehmen steht vorn. */
  readonly clubEntry = input(false);
  /** Verein, aus dem der Nutzer kam (`?club=`, Vorauswahl im Dialog). */
  readonly clubId = input<number | null>(null);

  readonly hasMoves = computed(() => { this.version(); return this.root().children.length > 0; });
  /** Die Vereinsdatenbank nimmt nur Partien aus der Grundstellung (Server: `fromPosition`). */
  readonly standardStart = computed(() => { this.version(); return isStandardStart(this.root()); });
  readonly clubReady = computed(() => this.hasMoves() && this.standardStart());

  /** Dasselbe Recht wie auf der Partieseite, und nur, wenn es ein LeagueHub zu diesem RookHub gibt. */
  get canClub(): boolean { return this.auth.has('league.contribute') && !!this.handoff.leagueHubUrl; }

  async run(kind: GameActionKind): Promise<void> {
    if (!this.hasMoves()) return;
    let clubs: ClubChoice[] = [];
    let clubId: number | null = null;
    if (kind === 'club') {
      const me = await this.leagueMe();
      if (!me?.clubs?.length) { this.snackbar.warn(this.translate.instant('analysis.gameActions.noClub')); return; }
      clubs = me.clubs;
      clubId = clubs.some(c => c.id === this.clubId()) ? this.clubId() : me.current;
    }
    const data: GameDialogData = { kind, details: detailsFromHeaders(this.headers()), clubs, clubId };
    const ref = this.dialog.open<AnalysisGameDialogComponent, GameDialogData, GameDialogResult>(AnalysisGameDialogComponent,
      { data, width: '440px', maxWidth: '94vw' });
    const result = await firstValueFrom(ref.afterClosed());
    if (!result) return;
    if (kind === 'download') this.download(result.details);
    else if (kind === 'save') this.save(result.details);
    else await this.toClub(result.details, result.clubId);
  }

  private download(details: GameDetails): void {
    const blob = new Blob([treeToPgn(this.root(), details)], { type: 'application/x-chess-pgn' });
    if (!downloadBlob(blob, gameFileName(details))) this.snackbar.warn(this.translate.instant('analysis.gameActions.downloadFailed'));
  }

  private save(details: GameDetails): void {
    this.games.importPgn(treeToPgn(this.root(), details, { mainlineOnly: true })).subscribe({
      next: r => {
        const id = r.ids?.[0];
        if (r.imported > 0 && id) this.offerGame('analysis.gameActions.saved', id);
        else if (r.duplicates > 0 && id) this.offerGame('analysis.gameActions.duplicate', id);
        else this.snackbar.warn(this.translate.instant('analysis.gameActions.saveFailed'));
      },
      error: () => this.snackbar.warn(this.translate.instant('analysis.gameActions.saveFailed')),
    });
  }

  private offerGame(messageKey: string, id: number): void {
    const ref = this.snackbar.show(this.translate.instant(messageKey), { action: 'analysis.gameActions.openGame', duration: 8000 });
    ref.onAction().subscribe(() => void this.router.navigate(['/games', id]));
  }

  private leagueMe(): Promise<LeagueMe | null> {
    return firstValueFrom(this.http.get<LeagueMe>('/api/league/me')).catch(() => null);
  }

  /** Als Entwurf ablegen und in LeagueHub weiter prüfen; dort geht es wie bei jeder anderen Liste weiter. */
  private async toClub(details: GameDetails, clubId: number | null): Promise<void> {
    if (clubId == null) return;
    const pgn = treeToPgn(this.root(), details, { mainlineOnly: true });
    const label = `Analysebrett: ${details.white.trim()} – ${details.black.trim()}`;
    try {
      const draft = await firstValueFrom(this.http.post<{ id: number }>('/api/league/club/drafts', { pgn, source: 'rookhub', label },
        { params: { club: String(clubId) } }));
      await this.handoff.jumpToLeagueHub(`verein/neu?entwurf=${draft.id}&club=${clubId}`);
    } catch (err) {
      const tooMany = err instanceof HttpErrorResponse && err.error?.reason === 'tooManyDrafts';
      this.snackbar.warn(this.translate.instant(tooMany ? 'analysis.gameActions.tooManyDrafts' : 'analysis.gameActions.clubFailed'));
    }
  }
}

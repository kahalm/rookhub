import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { ProfileService } from '@rh/core/profile.service';
import { SnackbarService } from '@rh/core/snackbar.service';
import {
  PlayerSearchItem, PlayerSearchResult,
} from '@rh/shared/profile-identity-form/profile-identity-form.component';
import { TrackedPlayer } from './tournament-history.model';
import { TournamentHistoryService } from './tournament-history.service';

/**
 * „Spieler verfolgen": Vor- und Nachname eingeben, suchen, einen Treffer waehlen — daraus wird
 * ein Reiter im Turnierverlauf.
 *
 * <p><b>Warum eine SUCHE und nicht zwei Textfelder.</b> Der Verlauf haengt an der Kennung: ohne
 * FIDE- oder chess-results-Nummer sucht er ueber den blossen Namen und zeigt Namensgleiche mit
 * (bei „Mueller" sind das viele). Die Nummer kennt niemand auswendig — die Trefferliste bringt
 * sie mit, und mit ihr wird der Reiter eindeutig. Ein Treffer OHNE Nummer bleibt erlaubt, die
 * Ansicht sagt dann, dass Namensgleiche dabei sein koennen.</p>
 *
 * <p>Gesucht wird ueber denselben Endpunkt wie im Profil (<c>GET /api/profile/player-search</c>,
 * chess-results UND FIDE parallel). Angelegt wird HIER, nicht beim Aufrufer: scheitert es
 * (Deckel erreicht, Netz weg), bleibt der Dialog offen und sagt es — statt dass sich ein Fenster
 * schliesst und danach nichts passiert.</p>
 */
@Component({
  selector: 'app-track-player-dialog',
  standalone: true,
  imports: [
    FormsModule, MatButtonModule, MatDialogModule, MatFormFieldModule, MatIconModule,
    MatInputModule, MatProgressSpinnerModule, TranslatePipe,
  ],
  template: `
    <h2 mat-dialog-title>{{ 'turnier.history.track.title' | translate }}</h2>

    <mat-dialog-content>
      <p class="lead">{{ 'turnier.history.track.lead' | translate }}</p>

      <form class="names" (ngSubmit)="search()">
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>{{ 'profile.firstName' | translate }}</mat-label>
          <input matInput name="firstName" autocomplete="off" [(ngModel)]="firstName">
        </mat-form-field>
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>{{ 'profile.lastName' | translate }}</mat-label>
          <input matInput name="lastName" autocomplete="off" [(ngModel)]="lastName">
        </mat-form-field>
        <button mat-stroked-button type="submit" class="search-btn"
                [disabled]="!searchable() || searching()">
          <!-- Lupe und Spinner je ALLEIN in ihrem Zweig, der Text ausserhalb: nur ein Block mit
               genau einem Wurzelelement landet in MatButtons Icon-Slot. -->
          @if (searching()) {
            <mat-spinner matButtonIcon diameter="18" />
          } @else {
            <mat-icon>search</mat-icon>
          }
          {{ 'profile.searchPlayer' | translate }}
        </button>
      </form>

      @if (results(); as found) {
        @if (!found.chessResultsResults.length && !found.fideResults.length) {
          <p class="muted">{{ 'profile.noResults' | translate }}</p>
        }
        @if (found.chessResultsResults.length) {
          <h3>ChessResults</h3>
          <div class="hits">
            @for (p of found.chessResultsResults; track p.name + p.chessResultsId) {
              <button type="button" class="hit" [disabled]="saving()" (click)="choose(p)">
                <span class="who">
                  @if (p.title) { <strong>{{ p.title }}</strong> }
                  <span class="name">{{ p.name }}</span>
                  @if (p.elo) { <span class="muted">({{ p.elo }})</span> }
                  @if (p.country) { <span class="muted">{{ p.country }}</span> }
                  @if (p.fideId) { <span class="muted">FIDE: {{ p.fideId }}</span> }
                </span>
                <mat-icon>add</mat-icon>
              </button>
            }
          </div>
        }
        @if (found.fideResults.length) {
          <h3>FIDE</h3>
          <div class="hits">
            @for (p of found.fideResults; track p.name + p.fideId) {
              <button type="button" class="hit" [disabled]="saving()" (click)="choose(p)">
                <span class="who">
                  @if (p.title) { <strong>{{ p.title }}</strong> }
                  <span class="name">{{ p.name }}</span>
                  @if (p.elo) { <span class="muted">({{ p.elo }})</span> }
                  @if (p.country) { <span class="muted">{{ p.country }}</span> }
                  @if (p.fideId) { <span class="muted">FIDE: {{ p.fideId }}</span> }
                </span>
                <mat-icon>add</mat-icon>
              </button>
            }
          </div>
        }
      }

      @if (failed()) { <p class="error">{{ failed() }}</p> }
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>{{ 'common.cancel' | translate }}</button>
    </mat-dialog-actions>
  `,
  styles: [`
    mat-dialog-content { display: flex; flex-direction: column; gap: 0.75rem; }
    .lead { margin: 0; color: var(--mat-sys-on-surface-variant); }
    .names { display: flex; flex-wrap: wrap; align-items: center; gap: 0.5rem; }
    .names mat-form-field { flex: 1 1 10rem; min-width: 0; }
    .search-btn { flex: 0 0 auto; }
    h3 { margin: 0.25rem 0 0; font-size: 0.9rem; color: var(--mat-sys-on-surface-variant); }
    .hits { display: flex; flex-direction: column; gap: 0.25rem; }
    .hit {
      display: flex; align-items: center; justify-content: space-between; gap: 0.5rem;
      width: 100%; padding: 0.6rem 0.75rem; border: 1px solid var(--mat-sys-outline-variant);
      border-radius: 8px; background: transparent; color: inherit; cursor: pointer;
      font: inherit; text-align: left; min-height: 44px;
    }
    .hit:hover:not(:disabled) { background: var(--mat-sys-surface-container-high); }
    .hit:disabled { opacity: 0.6; cursor: default; }
    .who { display: flex; flex-wrap: wrap; align-items: baseline; gap: 0.35rem; min-width: 0; }
    .name { overflow-wrap: anywhere; }
    .muted { color: var(--mat-sys-on-surface-variant); font-size: 0.85em; }
    .error { color: var(--mat-sys-error); margin: 0; }
    /* Auf dem Handy zoomt Safari bei Schrift unter 16px in das Feld hinein. */
    @media (pointer: coarse) { .names input { font-size: 16px; } }
  `],
})
export class TrackPlayerDialogComponent {
  private readonly profiles = inject(ProfileService);
  private readonly history = inject(TournamentHistoryService);
  private readonly snackbar = inject(SnackbarService);
  private readonly translate = inject(TranslateService);
  private readonly dialogRef = inject(MatDialogRef<TrackPlayerDialogComponent, TrackedPlayer>);

  firstName = '';
  lastName = '';

  readonly searching = signal(false);
  readonly saving = signal(false);
  readonly results = signal<PlayerSearchResult | null>(null);
  readonly failed = signal('');

  /** Der Nachname traegt die Suche — chess-results kennt keine Suche ueber eine Nummer. */
  searchable(): boolean {
    return this.lastName.trim().length >= 2;
  }

  search(): void {
    if (!this.searchable() || this.searching()) return;
    this.searching.set(true);
    this.failed.set('');
    this.results.set(null);

    this.profiles.searchPlayer<PlayerSearchResult>(this.lastName.trim(), this.firstName.trim() || undefined)
      .subscribe({
        next: found => {
          this.results.set(found);
          this.searching.set(false);
        },
        error: () => {
          this.failed.set(this.translate.instant('turnier.history.track.searchError'));
          this.searching.set(false);
        },
      });
  }

  /** Einen Treffer verfolgen und den Dialog mit dem angelegten Eintrag schliessen. */
  choose(hit: PlayerSearchItem): void {
    if (this.saving()) return;
    this.saving.set(true);
    this.failed.set('');

    const { lastName, firstName } = splitName(hit.name, this.lastName.trim(), this.firstName.trim());
    this.history.track({
      lastName,
      firstName: firstName || null,
      fideId: hit.fideId || null,
      chessResultsId: hit.chessResultsId || null,
      displayName: hit.name,
    }).subscribe({
      next: player => {
        this.snackbar.success(this.translate.instant('turnier.history.track.added', { name: player.displayName }));
        this.dialogRef.close(player);
      },
      error: err => {
        // Der Deckel ist der einzige Fehler, den der Nutzer selbst aufloesen kann — deshalb steht
        // er als eigener Satz da und nicht als „hat nicht geklappt".
        this.failed.set(this.translate.instant(
          err?.error?.limit ? 'turnier.history.track.limitReached' : 'turnier.history.track.addError',
          { count: err?.error?.limit }));
        this.saving.set(false);
      },
    });
  }
}

/**
 * „Nachname, Vorname" der Quelle in seine zwei Teile zerlegen.
 *
 * <p>Gespeichert wird die Schreibweise der QUELLE, nicht die getippte: der Verlauf sucht spaeter
 * genau mit diesen Woertern wieder bei chess-results, und die Eingabe darf abgekuerzt sein
 * („berschmid" findet ueber die Platzhalter-Suche „Oberschmid"). Nur wenn der Treffer kein Komma
 * traegt, gilt wieder das Getippte.</p>
 */
export function splitName(
  full: string, typedLast: string, typedFirst: string,
): { lastName: string; firstName: string } {
  const comma = (full ?? '').indexOf(',');
  if (comma < 0) {
    const name = (full ?? '').trim();
    return { lastName: name.length >= 2 ? name : typedLast, firstName: typedFirst };
  }
  return {
    lastName: full.slice(0, comma).trim() || typedLast,
    firstName: full.slice(comma + 1).trim(),
  };
}

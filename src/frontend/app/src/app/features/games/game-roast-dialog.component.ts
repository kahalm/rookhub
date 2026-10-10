import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpErrorResponse } from '@angular/common/http';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { GameRoast, GameRoasts, GamesService, RoastStyle } from './games.service';

export interface GameRoastData {
  gameId: number;
  /** Öffentlicher Link der Partie — geht beim Kopieren und Teilen mit. */
  shareUrl: string | null;
  /** Teilen-Kennung der Partie — für „Partie mit diesem Roast teilen" (0.742.0). */
  shareToken?: string | null;
}

/**
 * „Roast my game" (0.535.0): ein frecher Kommentar zur eigenen Partie, geschrieben vom Sprachmodell auf eigener
 * Hardware (Server `GameRoastService`). Drei Stile — freundlich, frech, russisch (der gnadenlose sowjetische Trainer).
 * Nichts wird automatisch veröffentlicht: Kopieren bzw. Teilen macht der Nutzer selbst.
 *
 * „Partie mit diesem Roast teilen" (0.742.0, Wunsch 2026-10-10): gibt GENAU diesen Text frei und kopiert/teilt den Link
 * `/g/{token}?roast={id}` — die geteilte Seite zeigt den Roast über der Partie, die Link-Vorschau nimmt ihn als Beschreibung.
 * Neu würfeln nimmt die Freigabe zurück (der Link zeigt nie still einen anderen Text); „Nicht mehr teilen" ebenso.
 */
@Component({
  selector: 'app-game-roast-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatDialogModule, MatButtonModule, MatIconModule, MatProgressSpinnerModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>🔥 {{ 'games.roast.title' | translate }}</h2>
    <div mat-dialog-content class="content">
      @if (state(); as s) {
        @if (!s.available) {
          <p class="note">{{ 'games.roast.notConfigured' | translate }}</p>
        } @else if (!s.hasAnalysis) {
          <p class="note">{{ 'games.roast.noAnalysis' | translate }}</p>
        } @else {
          <div class="styles" role="radiogroup" [attr.aria-label]="'games.roast.style' | translate">
            @for (st of styles; track st.key) {
              <button type="button" mat-stroked-button class="style" [class.on]="style() === st.key" role="radio"
                      [attr.aria-checked]="style() === st.key" (click)="pick(st.key)" [disabled]="busy()">
                {{ st.emoji }} {{ ('games.roast.styles.' + st.key) | translate }}
              </button>
            }
          </div>
          @if (busy()) {
            <div class="center"><mat-spinner diameter="32"></mat-spinner>
              <span>{{ 'games.roast.busy' | translate }}</span></div>
          } @else if (current(); as r) {
            <blockquote class="text">{{ r.text }}</blockquote>
            @if (r.shared && data.shareToken) {
              <p class="shared">
                <mat-icon>public</mat-icon>
                <span>{{ 'games.roast.sharedNote' | translate }}</span>
                <button mat-button type="button" class="unshare" (click)="unshareGame(r)" [disabled]="busy()">
                  {{ 'games.roast.unshare' | translate }}</button>
              </p>
            }
            @if (linkCopied()) { <p class="ok">{{ 'games.roast.linkCopied' | translate }}</p> }
          }
          @if (error(); as e) { <p class="error">{{ ('games.roast.error.' + e) | translate }}</p> }
        }
      } @else {
        <div class="center"><mat-spinner diameter="32"></mat-spinner></div>
      }
    </div>
    <div mat-dialog-actions align="end">
      @if (current(); as r) {
        @if (data.shareToken) {
          <button mat-button class="share-game" (click)="shareGame(r)" [disabled]="busy()">
            <mat-icon>public</mat-icon> {{ 'games.roast.shareGame' | translate }}</button>
        }
        <button mat-button (click)="copy(r)" [disabled]="busy()"><mat-icon>content_copy</mat-icon> {{ 'common.copy' | translate }}</button>
        @if (canShare) {
          <button mat-button (click)="share(r)" [disabled]="busy()"><mat-icon>share</mat-icon> {{ 'games.roast.share' | translate }}</button>
        }
      }
      @if (state()?.available && state()?.hasAnalysis) {
        <button mat-flat-button (click)="roast()" [disabled]="busy()">
          <mat-icon>local_fire_department</mat-icon>
          {{ (current() ? 'games.roast.again' : 'games.roast.go') | translate }}
        </button>
      }
      <button mat-button mat-dialog-close>{{ 'common.close' | translate }}</button>
    </div>
  `,
  styles: [`
    .content { min-width: min(520px, 86vw); }
    .styles { display: flex; flex-wrap: wrap; gap: 6px; margin-bottom: 12px; }
    .style.on { background: color-mix(in srgb, var(--mat-sys-primary, #3f51b5) 18%, transparent); font-weight: 600; }
    .center { display: flex; align-items: center; gap: 10px; padding: 12px 0; }
    .text { margin: 0; padding: 10px 14px; border-left: 3px solid #e64a19; white-space: pre-wrap; line-height: 1.45; }
    .note { margin: 4px 0; }
    .error { color: #c62828; }
    .shared { display: flex; align-items: center; gap: 6px; flex-wrap: wrap; margin: 8px 0 0; font-size: .85rem; opacity: .85; }
    .shared mat-icon { font-size: 18px; width: 18px; height: 18px; }
    .ok { color: #2e7d32; margin: 6px 0 0; font-size: .85rem; }
  `],
})
export class GameRoastDialogComponent implements OnInit {
  readonly data = inject<GameRoastData>(MAT_DIALOG_DATA);
  private games = inject(GamesService);
  private translate = inject(TranslateService);
  private destroyRef = inject(DestroyRef);

  readonly styles: { key: RoastStyle; emoji: string }[] = [
    { key: 'friendly', emoji: '🙂' }, { key: 'cheeky', emoji: '😏' }, { key: 'russian', emoji: '🐻' },
  ];
  readonly state = signal<GameRoasts | null>(null);
  readonly style = signal<RoastStyle>('friendly');
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly linkCopied = signal(false);
  /** Sperrzeit der Spark (0.546.0): bis wann gewürfelt werden kann — leer = frei. */
  readonly canShare = typeof navigator !== 'undefined' && typeof navigator.share === 'function';

  private lang(): string {
    return this.translate.currentLang() || this.translate.getFallbackLang() || 'en';
  }

  current(): GameRoast | null {
    return this.state()?.items.find(i => i.style === this.style()) ?? null;
  }

  ngOnInit(): void {
    this.games.roasts(this.data.gameId, this.lang()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: s => this.state.set(s),
      error: () => this.state.set({ available: false, hasAnalysis: false, items: [] }),
    });
  }

  pick(style: RoastStyle): void {
    this.style.set(style);
    this.error.set(null);
    this.linkCopied.set(false);
  }

  roast(): void {
    const style = this.style();
    this.busy.set(true);
    this.error.set(null);
    this.linkCopied.set(false);
    this.games.roast(this.data.gameId, style, this.lang()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: r => {
        this.state.update(s => s ? { ...s, items: [...s.items.filter(i => i.style !== r.style), r] } : s);
        this.busy.set(false);
      },
      error: (e: HttpErrorResponse) => {
        this.error.set(e.error?.reason ?? 'failed');
        this.busy.set(false);
      },
    });
  }

  private textWithLink(r: GameRoast): string {
    return this.data.shareUrl ? `${r.text}\n\n${this.data.shareUrl}` : r.text;
  }

  copy(r: GameRoast): void {
    void navigator.clipboard?.writeText(this.textWithLink(r)).then(
      () => this.error.set(null), () => this.error.set('copyFailed'));
  }

  share(r: GameRoast): void {
    void navigator.share({ text: r.text, url: this.data.shareUrl ?? undefined }).catch(() => undefined);
  }

  private setShared(style: RoastStyle, shared: boolean): void {
    this.state.update(s => s ? { ...s, items: s.items.map(i => i.style === style ? { ...i, shared } : i) } : s);
  }

  /** Den Roast freigeben und den Link `/g/{token}?roast={id}` teilen (Handy) bzw. kopieren. */
  shareGame(r: GameRoast): void {
    const token = this.data.shareToken;
    if (!token) return;
    this.busy.set(true);
    this.error.set(null);
    this.linkCopied.set(false);
    this.games.shareRoast(this.data.gameId, r.style, this.lang()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: ({ roastId }) => {
        this.busy.set(false);
        this.setShared(r.style, true);
        const url = this.games.roastShareUrl(token, roastId);
        if (this.canShare) {
          void navigator.share({ url }).catch(() => undefined);
        } else {
          void navigator.clipboard?.writeText(url).then(
            () => this.linkCopied.set(true), () => this.error.set('copyFailed'));
        }
      },
      error: () => {
        this.busy.set(false);
        this.error.set('shareFailed');
      },
    });
  }

  unshareGame(r: GameRoast): void {
    this.busy.set(true);
    this.linkCopied.set(false);
    this.games.unshareRoast(this.data.gameId, r.style, this.lang()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => { this.busy.set(false); this.setShared(r.style, false); },
      error: () => { this.busy.set(false); this.error.set('shareFailed'); },
    });
  }
}

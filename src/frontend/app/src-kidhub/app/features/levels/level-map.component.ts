import {
  ChangeDetectionStrategy, Component, DestroyRef, ElementRef, Injector, afterNextRender, computed, effect, inject, signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink, Scroll } from '@angular/router';
import { filter, take } from 'rxjs';
import { TranslatePipe } from '@ngx-translate/core';
import { KidsApiService, KidsLevel } from '../../core/kids-api.service';
import { KidsProgressStore } from '../../core/kids-progress.store';
import { themeIcon, themeNameKey } from '../../core/kids-themes';
import { KID_BACK } from '../../shared/kids-layout';
import { KidsErrorComponent } from '../../shared/kids-error.component';

/** So lange wackelt eine gesperrte Stufe nach dem Tippen und steht der Hinweis da. */
export const NUDGE_MS = 2500;

/** Ab dieser aktuellen Stufe springt die Karte beim Oeffnen zu ihr — die ersten vier stehen auch am Handy oben im Bild. */
export const SCROLL_FROM_LEVEL = 5;

/**
 * Alle Stufen als grosse Knoepfe mit Thema und Sternen. Gesperrt ist, was hinter der ersten noch
 * nicht geschafften Stufe liegt — ein Kind soll nicht in Stufe 30 landen und die Lust verlieren.
 */
@Component({
  selector: 'kid-level-map',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, TranslatePipe, KidsErrorComponent],
  template: `
    <header class="head">
      <a class="back" routerLink="/">← {{ 'kids.back' | translate }}</a>
      <h1>{{ 'kids.levels.title' | translate }}</h1>
      <span class="stars">⭐ {{ progress.totalStars() }}</span>
    </header>

    @if (loading()) {
      <p class="info">{{ 'kids.loading' | translate }}</p>
    } @else if (failed()) {
      <kid-error (retry)="load()" />
    } @else if (levels().length === 0) {
      <p class="info">{{ 'kids.levels.empty' | translate }}</p>
    } @else {
      <ol class="grid">
        @for (l of tiles(); track l.level) {
          <li>
            @if (l.unlocked) {
              <a class="level" [class.done]="l.stars > 0" [class.current]="l.level === current()"
                 [routerLink]="['/levels', l.level]"
                 [attr.aria-label]="('kids.levels.level' | translate: { level: l.level }) + ' – ' + (l.nameKey | translate)">
                <span class="num">{{ l.level }}</span>
                <span class="icon" aria-hidden="true">{{ l.icon }}</span>
                <span class="name">{{ l.nameKey | translate }}</span>
                <span class="row" aria-hidden="true">
                  @for (s of [1, 2, 3]; track s) { <span [class.on]="s <= l.stars">★</span> }
                </span>
              </a>
            } @else {
              <!-- Ein Knopf, kein stummes span: ein Tipp wackelt die Kachel und sagt, warum es nicht geht. -->
              <button type="button" class="level locked" [class.nudge]="nudged() === l.level" aria-disabled="true"
                      [attr.aria-label]="('kids.levels.level' | translate: { level: l.level }) + ' – ' + ('kids.levels.locked' | translate)"
                      (click)="nudge(l.level)">
                <span class="num">{{ l.level }}</span>
                <span class="icon" aria-hidden="true">🔒</span>
                <span class="name">{{ l.nameKey | translate }}</span>
              </button>
            }
          </li>
        }
      </ol>
    }
    <!-- Immer da (leer), damit Vorleseprogramme den Hinweis beim Erscheinen ansagen. -->
    <div class="toast-slot" role="status">
      @if (nudged() !== null) { <p class="toast">🔒 {{ 'kids.levels.lockedHint' | translate }}</p> }
    </div>
  `,
  styles: [KID_BACK, `
    :host { display: block; max-width: 980px; margin: 0 auto; padding: 16px; }
    .head { display: flex; align-items: center; gap: 12px; margin-bottom: 16px; }
    .head h1 { flex: 1; margin: 0; font-size: 1.9rem; color: var(--kid-title); text-align: center; }
    .stars { font-size: 1.15rem; font-weight: 800; text-decoration: none; color: inherit; white-space: nowrap; }
    .info { text-align: center; font-size: 1.2rem; }
    .grid { list-style: none; margin: 0; padding: 0; display: grid; gap: 14px;
            grid-template-columns: repeat(auto-fill, minmax(132px, 1fr)); }
    .level {
      position: relative; display: flex; flex-direction: column; align-items: center; gap: 2px;
      padding: 14px 8px 10px; border-radius: 24px; text-decoration: none; color: inherit;
      background: var(--kid-card); box-shadow: 0 5px 0 var(--kid-shadow); min-height: 138px; box-sizing: border-box;
    }
    a.level:hover { transform: translateY(-2px); }
    .level.done { background: var(--kid-good-bg); }
    .level.current { outline: 4px solid var(--kid-green-strong); animation: pulse 1.6s ease-in-out infinite; }
    .level.locked { opacity: .55; filter: grayscale(.7); }
    button.level { width: 100%; font: inherit; color: inherit; border: 0; cursor: pointer; }
    .level.locked.nudge { animation: wiggle .45s ease-in-out; }
    .toast-slot { position: fixed; left: 0; right: 0; bottom: 16px; display: flex; justify-content: center;
                  padding: 0 16px; pointer-events: none; z-index: 10; }
    .toast { margin: 0; max-width: 520px; padding: 14px 20px; border-radius: 20px; background: var(--kid-info-bg);
             box-shadow: 0 5px 0 var(--kid-shadow); font-size: 1.15rem; font-weight: 800; text-align: center; }
    .num { position: absolute; top: 8px; left: 12px; font-weight: 800; font-size: 1.05rem; }
    .icon { font-size: 2.6rem; line-height: 1.2; }
    .name { font-weight: 700; font-size: .98rem; text-align: center; }
    .row { font-size: 1.35rem; letter-spacing: 2px; color: #c9c9c9; }
    .row .on { color: #ffb300; }
    @keyframes pulse { 50% { transform: scale(1.04); } }
    @keyframes wiggle { 25% { transform: translateX(-6px) rotate(-3deg); } 75% { transform: translateX(6px) rotate(3deg); } }
    @media (prefers-reduced-motion: reduce) { .level.locked.nudge { animation: none; } }
  `],
})
export class LevelMapComponent {
  private readonly api = inject(KidsApiService);
  readonly progress = inject(KidsProgressStore);

  readonly levels = signal<KidsLevel[]>([]);
  readonly loading = signal(true);
  readonly failed = signal(false);
  /** Die gesperrte Stufe, auf die gerade getippt wurde — sie wackelt, unten steht der Hinweis. */
  readonly nudged = signal<number | null>(null);
  private nudgeTimer: ReturnType<typeof setTimeout> | undefined;
  /**
   * Erst springen, wenn der Router fertig ist: er stellt nach jeder Navigation an den Seitenanfang
   * (`scrollPositionRestoration: 'top'`, `Scroll`-Ereignis) und holte die Karte sonst gleich wieder nach oben.
   * Ohne laufende Navigation gibt es nichts abzuwarten.
   */
  private readonly routerScrolled = signal(inject(Router).currentNavigation() === null);
  private jumped = false;

  readonly current = computed(() => this.progress.currentLevel(this.levels().map(l => l.level)));
  readonly tiles = computed(() => this.levels().map(l => ({
    level: l.level,
    icon: themeIcon(l.theme),
    nameKey: themeNameKey(l.theme),
    stars: this.progress.level(l.level).stars,
    unlocked: this.progress.isUnlocked(l.level),
  })));

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.nudgeTimer));
    inject(Router).events.pipe(filter(e => e instanceof Scroll), take(1), takeUntilDestroyed())
      .subscribe(() => this.routerScrolled.set(true));
    // Am Handy ist die Karte ueber 3 000 px lang (40 Stufen, 2 Spalten): wer bei Stufe 25 steht, sah oben nur die
    // fertigen Stufen 1–10 und musste fast 2 000 px wischen (Codereview 2026-09-29, UX-063). Einmal je Oeffnen.
    const host = inject<ElementRef<HTMLElement>>(ElementRef);
    const injector = inject(Injector);
    effect(() => {
      const level = this.current();
      if (this.jumped || !this.routerScrolled() || level === null || level < SCROLL_FROM_LEVEL) return;
      this.jumped = true;
      afterNextRender(() => host.nativeElement.querySelector('.level.current')?.scrollIntoView({ block: 'center' }), { injector });
    });
    this.load();
  }

  /** Tipp auf eine gesperrte Stufe: kurz wackeln und den Hinweis zeigen, statt nichts zu tun. */
  nudge(level: number): void {
    clearTimeout(this.nudgeTimer);
    this.nudged.set(level);
    this.nudgeTimer = setTimeout(() => this.nudged.set(null), NUDGE_MS);
  }

  /** Stufen holen — beim Oeffnen und ueber „Nochmal" der Fehlerkachel. */
  load(): void {
    this.loading.set(true);
    this.failed.set(false);
    this.api.levels().subscribe({
      next: levels => { this.levels.set(levels); this.loading.set(false); },
      error: () => { this.failed.set(true); this.loading.set(false); },
    });
  }
}

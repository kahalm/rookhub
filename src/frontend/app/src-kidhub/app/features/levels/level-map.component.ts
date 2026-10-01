import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { KidsApiService, KidsLevel } from '../../core/kids-api.service';
import { KidsProgressStore } from '../../core/kids-progress.store';
import { themeIcon, themeNameKey } from '../../core/kids-themes';

/**
 * Alle Stufen als grosse Knoepfe mit Thema und Sternen. Gesperrt ist, was hinter der ersten noch
 * nicht geschafften Stufe liegt — ein Kind soll nicht in Stufe 30 landen und die Lust verlieren.
 */
@Component({
  selector: 'kid-level-map',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, TranslatePipe],
  template: `
    <header class="head">
      <a class="back" routerLink="/">← {{ 'kids.back' | translate }}</a>
      <h1>{{ 'kids.levels.title' | translate }}</h1>
      <span class="stars">⭐ {{ progress.totalStars() }}</span>
    </header>

    @if (loading()) {
      <p class="info">{{ 'kids.loading' | translate }}</p>
    } @else if (failed()) {
      <p class="info">{{ 'kids.loadError' | translate }}</p>
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
              <span class="level locked" [attr.aria-label]="'kids.levels.locked' | translate">
                <span class="num">{{ l.level }}</span>
                <span class="icon" aria-hidden="true">🔒</span>
                <span class="name">{{ l.nameKey | translate }}</span>
              </span>
            }
          </li>
        }
      </ol>
    }
  `,
  styles: [`
    :host { display: block; max-width: 980px; margin: 0 auto; padding: 16px; }
    .head { display: flex; align-items: center; gap: 12px; margin-bottom: 16px; }
    .head h1 { flex: 1; margin: 0; font-size: 1.9rem; color: var(--kid-title); text-align: center; }
    .back, .stars { font-size: 1.15rem; font-weight: 800; text-decoration: none; color: inherit; white-space: nowrap; }
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
    .num { position: absolute; top: 8px; left: 12px; font-weight: 800; font-size: 1.05rem; }
    .icon { font-size: 2.6rem; line-height: 1.2; }
    .name { font-weight: 700; font-size: .98rem; text-align: center; }
    .row { font-size: 1.35rem; letter-spacing: 2px; color: #c9c9c9; }
    .row .on { color: #ffb300; }
    @keyframes pulse { 50% { transform: scale(1.04); } }
  `],
})
export class LevelMapComponent {
  private readonly api = inject(KidsApiService);
  readonly progress = inject(KidsProgressStore);

  readonly levels = signal<KidsLevel[]>([]);
  readonly loading = signal(true);
  readonly failed = signal(false);

  readonly current = computed(() => this.progress.currentLevel(this.levels().map(l => l.level)));
  readonly tiles = computed(() => this.levels().map(l => ({
    level: l.level,
    icon: themeIcon(l.theme),
    nameKey: themeNameKey(l.theme),
    stars: this.progress.level(l.level).stars,
    unlocked: this.progress.isUnlocked(l.level),
  })));

  constructor() {
    this.api.levels().subscribe({
      next: levels => { this.levels.set(levels); this.loading.set(false); },
      error: () => { this.failed.set(true); this.loading.set(false); },
    });
  }
}

import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { AuthService } from '@rh/core/auth.service';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { KidsApiService, KidsCourse, KidsLevel } from '../../core/kids-api.service';
import { KidsProgressStore } from '../../core/kids-progress.store';
import { KidsEndlessStore } from '../../core/kids-endless.store';
import { KidsStarsStore } from '../../core/kids-stars.store';
import { KidsErrorComponent } from '../../shared/kids-error.component';
import { KID_PAGE_WIDTH } from '../../shared/kids-layout';

/** Ab dieser Fensterbreite steht die Startseite senkrecht mittig mit grossen Kacheln (k-start-space). */
export const KID_HOME_WIDE = '(width >= 900px) and (height >= 640px)';

/**
 * Startseite: ein grosser „Los geht's"-Knopf zur naechsten offenen Stufe, darunter die zwei Wege —
 * Puzzles (Stufen) und, wenn es welche gibt, Kurse. Kein Menue, kein Konto.
 */
@Component({
  selector: 'kid-home',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, TranslatePipe, KidsErrorComponent],
  template: `
    <section class="hero">
      <h1>{{ 'kids.home.title' | translate }}</h1>
      <p>{{ 'kids.home.subtitle' | translate }}</p>
      @if (current(); as level) {
        <a class="go" [routerLink]="['/levels', level]">
          {{ (done() === 0 ? 'kids.home.start' : 'kids.home.continue') | translate: { level } }} ▶
        </a>
      } @else if (failed()) {
        <!-- An der Stelle des Startknopfs, nicht klein unter dem Speicherhinweis: ohne Stufen fehlte der Knopf
             sonst ersatzlos (Codereview 2026-09-29, UX-062). -->
        <kid-error (retry)="load()" />
      }
    </section>

    <section class="tiles">
      <a class="tile puzzles" routerLink="/levels">
        <span class="icon" aria-hidden="true">🧩</span>
        <span class="name">{{ 'kids.home.puzzles' | translate }}</span>
        @if (levels().length > 0) {
          <span class="meta">
            {{ 'kids.home.levelsDone' | translate: { done: done(), total: levels().length } }}
            · ⭐ {{ progress.totalStars() }}
          </span>
        }
      </a>
      <a class="tile endless" routerLink="/endless">
        <span class="icon" aria-hidden="true">♾️</span>
        <span class="name">{{ 'kids.endless.title' | translate }}</span>
        <span class="meta">
          @if (endless.best() > 0) { 🏆 {{ 'kids.endless.best' | translate: { count: endless.best() } }} }
          @else { {{ 'kids.endless.tileHint' | translate }} }
        </span>
      </a>
      <a class="tile stars" routerLink="/stars">
        <span class="icon" aria-hidden="true">🌟</span>
        <span class="name">{{ 'kids.stars.title' | translate }}</span>
        <span class="meta">
          @if (stars.done() > 0) { ✅ {{ stars.done() }}/{{ stars.total }} } @else { {{ 'kids.stars.tileHint' | translate }} }
        </span>
      </a>
      @if (courses().length > 0) {
        <a class="tile courses" [routerLink]="courseLink()">
          <span class="icon" aria-hidden="true">📚</span>
          <span class="name">{{ 'kids.home.courses' | translate }}</span>
          <span class="meta">
            {{ courses().length === 1 ? courses()[0].title : ('kids.home.courseCount' | translate: { count: courses().length }) }}
          </span>
        </a>
      }
    </section>

    <!-- Wo der Fortschritt liegt — angemeldet im Konto (KidsProgressSync), sonst nur auf dem Geraet. -->
    <p class="saved">
      @if (user()) { ☁️ {{ 'kids.home.savedAccount' | translate }} } @else { 💾 {{ 'kids.home.savedDevice' | translate }} }
    </p>

  `,
  styles: [`
    :host { display: block; max-width: ${KID_PAGE_WIDTH.home}px; box-sizing: border-box; margin: 0 auto; padding: 16px; }
    .hero { text-align: center; padding: 24px 12px 8px; }
    h1 { font-size: clamp(2rem, 7vw, 3.4rem); margin: 0 0 6px; color: var(--kid-title); letter-spacing: .5px; }
    .hero p { font-size: 1.25rem; margin: 0 0 22px; }
    .go {
      display: inline-block; text-decoration: none; background: var(--kid-green-strong); color: #fff;
      font-size: 1.6rem; font-weight: 800; padding: 16px 32px; border-radius: 999px;
      box-shadow: 0 6px 0 var(--kid-shadow);
    }
    .go:active { transform: translateY(4px); box-shadow: 0 2px 0 var(--kid-shadow); }
    .tiles { display: grid; grid-template-columns: repeat(auto-fit, minmax(240px, 1fr)); gap: 18px; margin-top: 28px; }
    .tile {
      display: flex; flex-direction: column; align-items: center; gap: 6px; padding: 26px 18px;
      border-radius: 28px; text-decoration: none; color: inherit; background: var(--kid-card);
      box-shadow: 0 6px 0 var(--kid-shadow); transition: transform .12s;
    }
    .tile:hover { transform: translateY(-3px); }
    .tile.puzzles { background: var(--kid-sky); }
    .tile.courses { background: var(--kid-peach); }
    .tile.endless { background: #e8e0ff; }
    .tile.stars { background: #fff3c4; }
    .icon { font-size: 3.4rem; line-height: 1.1; }
    .name { font-size: 1.7rem; font-weight: 800; }
    .meta { font-size: 1.05rem; opacity: .85; text-align: center; }
    .saved { text-align: center; margin: 22px 0 0; font-size: .95rem; opacity: .8; }
    /* PC (UI-Sweep 2026-10-10, k-start-space): die Kacheln endeten bei y ≈ 550, darunter fast 500 px leer. Jetzt steht
       der Inhalt senkrecht mittig zwischen Kopf- und Fusszeile (~140 px), die Kacheln sind ~360 × 260 px gross. */
    @media ${KID_HOME_WIDE} {
      :host { display: flex; flex-direction: column; justify-content: center;
              min-height: calc(var(--kid-vh, 1vh) * 100 - 140px); }
      .hero { padding-top: 0; }
      .tiles { grid-template-columns: repeat(auto-fit, minmax(300px, 360px)); justify-content: center; gap: 22px; }
      .tile { min-height: 260px; box-sizing: border-box; justify-content: center; gap: 10px; padding: 28px 22px; }
      .icon { font-size: 64px; }
      .name { font-size: 32px; }
      .meta { font-size: 1.15rem; }
    }
  `],
})
export class KidsHomeComponent {
  private readonly api = inject(KidsApiService);
  readonly progress = inject(KidsProgressStore);
  readonly endless = inject(KidsEndlessStore);
  readonly stars = inject(KidsStarsStore);
  private readonly translate = inject(TranslateService);
  readonly user = toSignal(inject(AuthService).currentUser$, { initialValue: null });

  readonly levels = signal<KidsLevel[]>([]);
  readonly courses = signal<KidsCourse[]>([]);
  readonly failed = signal(false);

  readonly current = computed(() => this.progress.currentLevel(this.levels().map(l => l.level)));
  /** Geschaffte Stufen der aktuellen Leiter — nie mehr als sie hat (F7-014). */
  readonly done = computed(() => this.progress.completedOf(this.levels().map(l => l.level)));
  readonly courseLink = computed(() => {
    const list = this.courses();
    return list.length === 1 ? ['/courses', list[0].bookId] : ['/courses'];
  });

  constructor() {
    this.load();
  }

  /** Stufen und Kurse holen — beim Oeffnen und ueber „Nochmal" der Fehlerkachel (was schon kam, liefert der
   *  Zwischenspeicher von `KidsApiService` ohne neuen Abruf). */
  load(): void {
    this.failed.set(false);
    this.api.levels().subscribe({
      next: levels => this.levels.set(levels),
      error: () => this.failed.set(true),
    });
    // Ohne Kurse bleibt die Kachel einfach weg — kein Fehlertext fuer etwas, das es nicht gibt.
    this.api.courses(this.translate.currentLang() ?? undefined).subscribe({ next: courses => this.courses.set(courses), error: () => {} });
  }
}

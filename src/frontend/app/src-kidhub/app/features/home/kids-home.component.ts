import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { AuthService } from '@rh/core/auth.service';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { KidsApiService, KidsCourse, KidsLevel } from '../../core/kids-api.service';
import { KidsProgressStore } from '../../core/kids-progress.store';
import { KidsEndlessStore } from '../../core/kids-endless.store';
import { KidsStarsStore } from '../../core/kids-stars.store';
import { KidsErrorComponent } from '../../shared/kids-error.component';
import { KID_HOME_WIDTH } from '../../shared/kids-layout';

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
          <!-- UI-Sweep 2026-10-11 (r2-k-level-run): angefangener Durchgang mit der nächsten Aufgabe -->
          <!-- e1-k-start-continue-dot-wrap: zwei Teile, der Trenner als ::before des zweiten — bricht der Knopf am Handy
               um, steht „· Aufgabe 5 ▶" geschlossen in Zeile 2 statt „·" allein am Zeilenende (DESIGN 5.13). -->
          @if (currentRun() > 0) {
            <span>{{ 'kids.home.continue' | translate: { level } }}</span>&ngsp;<span class="part">{{ 'kids.home.puzzleN' | translate: { n: currentRun() + 1 } }} ▶</span>
          } @else {
            {{ (done() === 0 ? 'kids.home.start' : 'kids.home.continue') | translate: { level } }} ▶
          }
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
          <!-- z3-k-start-kachel-umbruch: Stand und Sterne als eigene Blöcke — bricht es um, dann zwischen ihnen
               (vorher stand „⭐ 0" verwaist in Zeile 2). -->
          <span class="meta">
            <!-- e1-k-start-levels-done-stars-glued: &ngsp; hält das Leerzeichen (reiner Zeilenumbruch fällt weg), der Trenner
                 kommt als ::before von .nw. -->
            <span class="done">{{ 'kids.home.levelsDone' | translate: { done: done(), total: levels().length } }}</span>&ngsp;<span class="nw">⭐ {{ progress.totalStars() }}</span>
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
          <!-- UI-Sweep 2026-10-11 (r2-k-course-tile): Stand wie bei den Puzzles — ein Kurs „Titel · 8 von 287" mit Balken,
               mehrere „1 von 3 Kursen angefangen". -->
          @if (courses().length === 1) {
            <span class="meta">{{ 'kids.home.courseProgress' | translate: { title: courses()[0].title, done: courseDone(courses()[0]), total: courses()[0].puzzleCount } }}</span>
            <span class="tile-bar" aria-hidden="true">
              <span [style.width.%]="coursePercent(courses()[0])" [class.started]="coursePercent(courses()[0]) > 0"></span>
            </span>
          } @else if (coursesStarted() > 0) {
            <span class="meta">{{ 'kids.home.coursesStarted' | translate: { started: coursesStarted(), count: courses().length } }}</span>
          } @else {
            <span class="meta">{{ 'kids.home.courseCount' | translate: { count: courses().length } }}</span>
          }
        </a>
      }
    </section>

    <!-- Wo der Fortschritt liegt — angemeldet im Konto (KidsProgressSync), sonst nur auf dem Geraet. -->
    <p class="saved">
      @if (user()) { ☁️ {{ 'kids.home.savedAccount' | translate }} } @else { 💾 {{ 'kids.home.savedDevice' | translate }} }
    </p>

  `,
  styles: [`
    :host { display: block; max-width: ${KID_HOME_WIDTH}; box-sizing: border-box; margin: 0 auto; padding: 16px; }
    .hero { text-align: center; padding: 24px 12px 8px; }
    h1 { font-size: clamp(2rem, 7vw, 3.4rem); margin: 0 0 6px; color: var(--kid-title); letter-spacing: .5px; }
    .hero p { font-size: 1.25rem; margin: 0 0 22px; }
    .go {
      display: inline-block; text-decoration: none; background: var(--kid-green-strong); color: #fff;
      font-size: 1.6rem; font-weight: 800; padding: 16px 32px; border-radius: 999px;
      box-shadow: 0 6px 0 var(--kid-shadow);
    }
    .go .part { white-space: nowrap; }
    .go .part::before { content: '· '; }
    .go:active { transform: translateY(4px); box-shadow: 0 2px 0 var(--kid-shadow); }
    .tiles { display: grid; grid-template-columns: repeat(auto-fit, minmax(240px, 1fr)); gap: 18px; margin-top: 28px; }
    /* Tablet (a1-k-home-tablet-orphan-card): bei 600–899 px passten drei Kacheln in eine Reihe, „Kurse" stand allein in
       Zeile 2. Jetzt 2 × 2; ohne Kurs (drei Kacheln) nimmt die letzte die ganze Zeile. */
    @media (600px <= width < 900px) {
      .tiles { grid-template-columns: repeat(2, 1fr); }
      .tile:last-child:nth-child(odd) { grid-column: 1 / -1; }
    }
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
    .meta { font-size: 1.05rem; opacity: .85; text-align: center; text-wrap: balance; }
    .meta .done { display: inline-block; max-width: 100%; }
    .meta .nw { white-space: nowrap; }
    .meta .nw::before { content: '· '; }
    /* Dünner Fortschrittsbalken der Kurs-Kachel (r2-k-course-tile). */
    /* d3-k-kurskachel-balken-feste-breite: Spur und Füllung wie auf der Kurse-Liste (kräftiger auf Pfirsich); die Breite
       bleibt, weil die Kachel mittig gesetzt ist. */
    .tile-bar { display: block; width: min(220px, 80%); height: 6px; border-radius: 3px; background: rgba(120, 60, 0, .22);
                overflow: hidden; margin-top: 4px; }
    .tile-bar span { display: block; height: 100%; border-radius: 3px; background: #b45f06; min-width: 0; }
    .tile-bar span.started { min-width: 6px; }
    .saved { text-align: center; margin: 22px 0 0; font-size: .95rem; opacity: .8; }
    /* PC (UI-Sweep 2026-10-10, k-start-space): die Kacheln endeten bei y ≈ 550, darunter fast 500 px leer. Jetzt steht
       der Inhalt senkrecht mittig zwischen Kopf- und Fusszeile (~140 px), die Kacheln sind ~360 × 260 px gross. */
    @media ${KID_HOME_WIDE} {
      :host { display: flex; flex-direction: column; justify-content: center;
              min-height: calc(var(--kid-vh, 1vh) * 100 - 140px); }
      .hero { padding-top: 0; }
      /* d2-k-home-tiles: mit der Kurskachel sind es vier — bei minmax(300px, 360px) stand „Kurse" allein in Zeile 2, am
         Laptop unter der Falz. Jetzt teilen sich alle Kacheln eine Reihe (bei 1160 px je ~265 px), drei bleiben breit. */
      .tiles { grid-template-columns: repeat(auto-fit, minmax(240px, 1fr)); justify-content: center; gap: 22px; }
      /* Oben ausgerichtet statt mittig (z2-k-kacheln-versatz): Kacheln mit einzeiliger Unterzeile saßen sonst ≈ 10 px
         tiefer; so stehen Symbole und Titel auf einer Linie, die gemeinsame Kachelhöhe gleicht unten aus. */
      .tile { min-height: 220px; box-sizing: border-box; justify-content: flex-start; gap: 10px; padding: 28px 22px; }
      .icon { font-size: 64px; }
      .name { font-size: 32px; }
      .meta { font-size: 1.15rem; }
    }
    /* Großer Bildschirm: die Seite wächst bis 1520 px (KID_HOME_WIDTH), die Kacheln ≈ 360 × 260 px (z2-k-start-kacheln-breit). */
    @media ${KID_HOME_WIDE} and (width >= 1600px) {
      .tiles { gap: 28px; }
      .tile { min-height: 260px; }
      .icon { font-size: 72px; }
    }
    /* Breitbild-Stufe (a1-k-ultrawide-raster): ab 2400 px ist die Seite 1880 px breit (--kid-wide-max der App-Huelle) —
       die Kacheln werden groesser (≈ 450 × 320 px) statt nur breiter. */
    @media ${KID_HOME_WIDE} and (width >= 2400px) {
      .tiles { gap: 32px; }
      .tile { min-height: 320px; padding: 36px 26px; }
      .icon { font-size: 88px; }
      .name { font-size: 38px; }
      .meta { font-size: 1.3rem; }
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
  /** Schon gelöste Aufgaben im laufenden Durchgang der aktuellen Stufe (0 = nicht angefangen;
   *  UI-Sweep 2026-10-11, r2-k-level-run). */
  readonly currentRun = computed(() => {
    const level = this.current();
    if (level === null) return 0;
    const count = this.levels().find(l => l.level === level)?.puzzleCount ?? 0;
    return Math.min(this.progress.level(level).runIndex, Math.max(count - 1, 0));
  });
  /** Geschaffte Stufen der aktuellen Leiter — nie mehr als sie hat (F7-014). */
  readonly done = computed(() => this.progress.completedOf(this.levels().map(l => l.level)));
  /** Angefangene Kurse (mindestens eine Linie gelöst; r2-k-course-tile). */
  readonly coursesStarted = computed(() => this.courses().filter(c => this.courseDone(c) > 0).length);
  readonly courseLink = computed(() => {
    const list = this.courses();
    return list.length === 1 ? ['/courses', list[0].bookId] : ['/courses'];
  });

  /** Sprache, in der die Kurse zuletzt geholt wurden — bei einem Wechsel in der Sprachpille neu holen. */
  private coursesLang: string | undefined;

  constructor() {
    this.load();
    // b1-k-kursname-sprachwechsel: der Kursname kommt vom Server in der angefragten Sprache — nach einem Wechsel auf
    // English stand sonst weiter „Erste Endspiele" in der Kachel.
    effect(() => {
      const lang = this.translate.currentLang() ?? undefined;
      untracked(() => { if (lang !== this.coursesLang) this.loadCourses(); });
    });
  }

  /** Gelöste Linien eines Kurses, höchstens so viele, wie er hat (dieselbe Regel wie die Kursliste, F7-014). */
  courseDone(c: KidsCourse): number {
    return Math.min(this.progress.course(c.bookId).solved.length, c.puzzleCount);
  }

  /** Anteil in Prozent für den Balken. */
  coursePercent(c: KidsCourse): number {
    return c.puzzleCount > 0 ? Math.round((this.courseDone(c) / c.puzzleCount) * 1000) / 10 : 0;
  }

  /** Stufen und Kurse holen — beim Oeffnen und ueber „Nochmal" der Fehlerkachel (was schon kam, liefert der
   *  Zwischenspeicher von `KidsApiService` ohne neuen Abruf). */
  load(): void {
    this.failed.set(false);
    this.api.levels().subscribe({
      next: levels => this.levels.set(levels),
      error: () => this.failed.set(true),
    });
    this.loadCourses();
  }

  /** Kurse in der aktuellen Sprache holen. Ohne Kurse bleibt die Kachel einfach weg — kein Fehlertext fuer etwas, das
   *  es nicht gibt. */
  private loadCourses(): void {
    const lang = this.translate.currentLang() ?? undefined;
    this.coursesLang = lang;
    this.api.courses(lang).subscribe({ next: courses => { if (lang === this.coursesLang) this.courses.set(courses); }, error: () => {} });
  }
}

import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { BehaviorSubject, of, throwError } from 'rxjs';
import { CoursePlayComponent, nextUnsolved } from './course-play.component';
import { KidsApiService, KidsCourseLine } from '../../core/kids-api.service';

describe('nextUnsolved (Kinderkurs)', () => {
  const lines = [{ id: 10 }, { id: 11 }, { id: 12 }];

  it('nimmt die erste ungeloeste ab der Stelle', () => {
    expect(nextUnsolved(lines, new Set(), 0)).toBe(0);
    expect(nextUnsolved(lines, new Set([10]), 0)).toBe(1);
    expect(nextUnsolved(lines, new Set([11]), 1)).toBe(2);
  });

  it('faengt am Ende von vorn an', () => {
    expect(nextUnsolved(lines, new Set([12]), 2)).toBe(0);
    expect(nextUnsolved(lines, new Set([10, 12]), 3)).toBe(1);
  });

  it('alles geloest → -1', () => {
    expect(nextUnsolved(lines, new Set([10, 11, 12]), 0)).toBe(-1);
    expect(nextUnsolved([], new Set(), 0)).toBe(-1);
  });
});

/** Codereview 2026-09-29, F7-011: im Fehlerfall stand nur ein Satz da — ohne Knopf, mit dem das Kind weiterkommt. */
describe('CoursePlayComponent — Ladefehler', () => {
  const line: KidsCourseLine = {
    id: 1, bookTitle: 'Matt in einem Zug', fen: '1R6/8/8/8/6p1/8/r6k/5K2 b - - 3 73', moves: 'g4g3 b8h8', startPly: 0,
    title: null, chapter: null, comment: null, moveComments: null, altMoves: null,
  };

  it('Fehlerkachel mit „Nochmal", das die Linien des Kurses neu holt', () => {
    localStorage.removeItem('rh-kids-progress-v1');
    const api = jasmine.createSpyObj<KidsApiService>('KidsApiService', ['coursePuzzles']);
    api.coursePuzzles.and.returnValues(throwError(() => new Error('500')), of([line]));
    TestBed.configureTestingModule({
      imports: [CoursePlayComponent],
      providers: [
        provideRouter([]), provideTranslateService({ fallbackLang: 'en' }),
        { provide: KidsApiService, useValue: api },
        { provide: ActivatedRoute, useValue: { paramMap: new BehaviorSubject(convertToParamMap({ bookId: '7' })) } },
      ],
    });
    const f = TestBed.createComponent(CoursePlayComponent);
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    const again = el.querySelector<HTMLButtonElement>('kid-error button.again');
    expect(again).withContext('Knopf „Nochmal"').not.toBeNull();

    again!.click();
    f.detectChanges();

    expect(api.coursePuzzles).toHaveBeenCalledTimes(2);
    expect(api.coursePuzzles.calls.mostRecent().args[0]).toBe(7);
    expect(el.querySelector('kid-error')).toBeNull();
    expect(f.componentInstance.current()?.id).toBe(1);
  });
});

/** b1-k-kursname-sprachwechsel: Kurstitel und Texte folgen der Sprachpille, die laufende Aufgabe bleibt. */
describe('CoursePlayComponent — Sprachwechsel', () => {
  it('holt die Linien in der neuen Sprache, Titel neu, Aufgabe auf dem Brett unverändert', async () => {
    localStorage.removeItem('rh-kids-progress-v1');
    const make = (title: string, id: number): KidsCourseLine => ({
      id, bookTitle: title, fen: '1R6/8/8/8/6p1/8/r6k/5K2 b - - 3 73', moves: 'g4g3 b8h8', startPly: 0,
      title: null, chapter: null, comment: null, moveComments: null, altMoves: null,
    });
    const api = jasmine.createSpyObj<KidsApiService>('KidsApiService', ['coursePuzzles']);
    api.coursePuzzles.and.callFake((_id: number, lang?: string) =>
      of([make(lang === 'en' ? 'First endgames' : 'Erste Endspiele', 1), make(lang === 'en' ? 'First endgames' : 'Erste Endspiele', 2)]));
    TestBed.configureTestingModule({
      imports: [CoursePlayComponent],
      providers: [
        provideRouter([]), provideTranslateService({ fallbackLang: 'en' }),
        { provide: KidsApiService, useValue: api },
        { provide: ActivatedRoute, useValue: { paramMap: new BehaviorSubject(convertToParamMap({ bookId: '7' })) } },
      ],
    });
    const translate = TestBed.inject(TranslateService);
    await translate.use('de');
    const f = TestBed.createComponent(CoursePlayComponent);
    f.detectChanges();
    f.componentInstance.index.set(1);
    f.detectChanges();
    const task = f.componentInstance.task();
    expect(f.componentInstance.title()).toBe('Erste Endspiele');

    await translate.use('en');
    f.detectChanges();
    expect(api.coursePuzzles.calls.mostRecent().args).toEqual([7, 'en']);
    expect(f.componentInstance.title()).toBe('First endgames');
    expect(f.componentInstance.index()).withContext('dieselbe Aufgabe').toBe(1);
    expect(f.componentInstance.task()).withContext('Brett beginnt nicht neu').toBe(task);
  });
});

/** UI-Sweep 2026-10-11 (r2-k-course-heading): eine angeklebte Zwischenüberschrift steht fett über dem Text. */
describe('CoursePlayComponent — Einleitung', () => {
  it('setzt die Zwischenüberschrift als eigene fette Zeile ab', () => {
    localStorage.removeItem('rh-kids-progress-v1');
    const line: KidsCourseLine = {
      id: 1, bookTitle: 'Endspiel', fen: '1R6/8/8/8/6p1/8/r6k/5K2 b - - 3 73', moves: 'g4g3 b8h8', startPly: 0,
      title: null, chapter: null, comment: 'Den Weg verstellenSchauen wir uns nun Stellung 1.3 an.', moveComments: null, altMoves: null,
    };
    const api = jasmine.createSpyObj<KidsApiService>('KidsApiService', ['coursePuzzles']);
    api.coursePuzzles.and.returnValue(of([line]));
    TestBed.configureTestingModule({
      imports: [CoursePlayComponent],
      providers: [
        provideRouter([]), provideTranslateService({ fallbackLang: 'en' }),
        { provide: KidsApiService, useValue: api },
        { provide: ActivatedRoute, useValue: { paramMap: new BehaviorSubject(convertToParamMap({ bookId: '50' })) } },
      ],
    });
    const f = TestBed.createComponent(CoursePlayComponent);
    f.detectChanges();
    const intro = (f.nativeElement as HTMLElement).querySelector('.intro')!;
    expect(intro.querySelector('.hl')?.textContent).toBe('Den Weg verstellen');
    expect(intro.querySelectorAll('p')[1].textContent).toBe('Schauen wir uns nun Stellung 1.3 an.');
    expect(getComputedStyle(intro.querySelector('.hl')!).fontWeight).toBe('800');
  });
});

/** UI-Sweep 2026-10-11 (r2-k-course-scroll): ein langer Einleitungstext steht nicht mehr in einem eigenen Scrollkasten,
 *  sondern wird unten ausgeblendet und mit „Weiterlesen" ganz aufgeklappt. */
describe('CoursePlayComponent — langer Einleitungstext', () => {
  const long = Array.from({ length: 60 }, (_, i) => `Absatz ${i + 1}: Dann hängt alles von der Stellung der Könige ab.`).join('\n\n');
  const line = (id: number, comment: string | null): KidsCourseLine => ({
    id, bookTitle: 'Erste Endspiele', fen: '1R6/8/8/8/6p1/8/r6k/5K2 b - - 3 73', moves: 'g4g3 b8h8', startPly: 0,
    title: null, chapter: null, comment, moveComments: null, altMoves: null,
  });

  it('„Weiterlesen" klappt auf, die nächste Linie beginnt wieder zugeklappt; ein kurzer Text braucht keinen Knopf', () => {
    localStorage.removeItem('rh-kids-progress-v1');
    const api = jasmine.createSpyObj<KidsApiService>('KidsApiService', ['coursePuzzles']);
    api.coursePuzzles.and.returnValue(of([line(1, long), line(2, 'Kurz.')]));
    TestBed.configureTestingModule({
      imports: [CoursePlayComponent],
      providers: [
        provideRouter([]), provideTranslateService({ fallbackLang: 'en' }),
        { provide: KidsApiService, useValue: api },
        { provide: ActivatedRoute, useValue: { paramMap: new BehaviorSubject(convertToParamMap({ bookId: '7' })) } },
      ],
    });
    const f = TestBed.createComponent(CoursePlayComponent);
    f.detectChanges();
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    const intro = el.querySelector<HTMLElement>('.intro')!;
    expect(getComputedStyle(intro).overflowY).toBe('hidden');               // kein eigener Scrollbalken mehr
    // z2-k-course-intro-halbzeile: kein Polster am Text — sonst läuft die nächste Zeile im Polster halb sichtbar weiter.
    expect(getComputedStyle(intro).paddingBottom).toBe('0px');
    expect(getComputedStyle(el.querySelector('.intro-wrap')!).paddingTop).toBe('12px');
    expect(f.componentInstance.introClipped()).toBeTrue();
    // z3-k-kurs-einleitung-pc: zugeklappt rastet die Höhe auf ganze Zeilen ein (PC: 40vh abgerundet, Handy: drei Zeilen).
    const lines = parseFloat(getComputedStyle(intro).maxHeight) / parseFloat(getComputedStyle(intro).lineHeight);
    expect(Math.abs(lines - Math.round(lines))).withContext(`${lines} Zeilen`).toBeLessThan(0.02);
    const more = el.querySelector<HTMLButtonElement>('button.more')!;
    expect(more.getAttribute('aria-expanded')).toBe('false');
    more.click();
    f.detectChanges();
    expect(intro.classList).toContain('open');
    expect(intro.scrollHeight).toBeLessThanOrEqual(intro.clientHeight + 1);
    expect(el.querySelector('button.more')!.getAttribute('aria-expanded')).toBe('true');

    f.componentInstance.index.set(1);
    f.detectChanges();
    f.detectChanges();
    expect(f.componentInstance.introOpen()).toBeFalse();
    expect(el.querySelector('button.more')).toBeNull();
  });

  function render(): ComponentFixture<CoursePlayComponent> {
    localStorage.removeItem('rh-kids-progress-v1');
    const api = jasmine.createSpyObj<KidsApiService>('KidsApiService', ['coursePuzzles']);
    api.coursePuzzles.and.returnValue(of([line(1, long), line(2, long)]));
    TestBed.configureTestingModule({
      imports: [CoursePlayComponent],
      providers: [
        provideRouter([]), provideTranslateService({ fallbackLang: 'en' }),
        { provide: KidsApiService, useValue: api },
        { provide: ActivatedRoute, useValue: { paramMap: new BehaviorSubject(convertToParamMap({ bookId: '7' })) } },
      ],
    });
    const f = TestBed.createComponent(CoursePlayComponent);
    f.detectChanges();
    f.detectChanges();
    return f;
  }

  /** Wunsch zu d2-k-course-intro: „Schließen" neben „Weiterlesen" blendet die Karte aus und holt das Brett ins Bild. */
  it('„Schließen" blendet die Erklärkarte aus und scrollt zum Brett; „Text zeigen" holt sie zurück, die nächste Linie zeigt sie wieder', async () => {
    const scroll = spyOn(Element.prototype, 'scrollIntoView');
    const f = render();
    const el = f.nativeElement as HTMLElement;
    const close = el.querySelector<HTMLButtonElement>('.intro-actions .close-intro')!;
    expect(close).withContext('Schließen neben Weiterlesen').not.toBeNull();
    expect(close.textContent).toContain('common.close');
    close.click();
    f.detectChanges();
    await f.whenStable();
    expect(el.querySelector('.intro')).toBeNull();
    expect(scroll).toHaveBeenCalled();
    expect((scroll.calls.mostRecent().object as HTMLElement).classList).toContain('board');

    el.querySelector<HTMLButtonElement>('.show-intro')!.click();
    f.detectChanges();
    expect(el.querySelector('.intro')).not.toBeNull();

    f.componentInstance.closeIntro();
    f.componentInstance.index.set(1);
    f.detectChanges();
    expect(el.querySelector('.intro')).withContext('neue Linie: Karte wieder da').not.toBeNull();
  });

  /** d2-k-course-intro: am Handy zugeklappt nur drei Zeilen über dem Brett (vorher 30vh ≈ 8 Zeilen). */
  it('zeigt am Handy zugeklappt höchstens drei Zeilen', async () => {
    const frame = window.frameElement as HTMLElement | null;
    if (!frame) pending('Karma läuft nicht im iframe — der Viewport lässt sich nicht verstellen');
    const prev = frame!.style.width;
    frame!.style.width = '390px';
    await new Promise<void>(r => requestAnimationFrame(() => r()));
    try {
      const f = render();
      const intro = (f.nativeElement as HTMLElement).querySelector<HTMLElement>('.intro')!;
      const lineH = parseFloat(getComputedStyle(intro).lineHeight);
      expect(intro.getBoundingClientRect().height).toBeLessThanOrEqual(3 * lineH + 24 + 1);
    } finally {
      frame!.style.width = prev;
    }
  });
});

/** z1-k-course-intro: am Handy steht der Stand kurz neben dem Kurstitel; das Kapitel weicht dem Aufgabentitel. */
describe('CoursePlayComponent — Kopf am Handy', () => {
  it('zeigt den Stand kurz („0/2") neben dem langen Text und markiert das Kapitel neben einem Aufgabentitel', () => {
    localStorage.removeItem('rh-kids-progress-v1');
    const mk = (id: number): KidsCourseLine => ({
      id, bookTitle: 'Erste Endspiele', fen: '1R6/8/8/8/6p1/8/r6k/5K2 b - - 3 73', moves: 'g4g3 b8h8', startPly: 0,
      title: 'Bauer auf der sechsten Reihe', chapter: 'Endspiel 2', comment: null, moveComments: null, altMoves: null,
    });
    const api = jasmine.createSpyObj<KidsApiService>('KidsApiService', ['coursePuzzles']);
    api.coursePuzzles.and.returnValue(of([mk(1), mk(2)]));
    TestBed.configureTestingModule({
      imports: [CoursePlayComponent],
      providers: [
        provideRouter([]), provideTranslateService({ fallbackLang: 'en' }),
        { provide: KidsApiService, useValue: api },
        { provide: ActivatedRoute, useValue: { paramMap: new BehaviorSubject(convertToParamMap({ bookId: '7' })) } },
      ],
    });
    const f = TestBed.createComponent(CoursePlayComponent);
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    expect(el.querySelector('.count .count-short')!.textContent).toBe('0/2');
    expect(el.querySelector('.count .count-short')!.getAttribute('aria-hidden')).toBe('true');
    expect(el.querySelector('.count .count-long')).not.toBeNull();
    // a1-k-course-counter-plain: dieselbe grüne Zählerpille wie Stufen/Endlos/Sternenjagd, mit ✓ vorne.
    const count = el.querySelector<HTMLElement>('.count')!;
    expect(count.classList).toContain('kid-counter');
    expect(count.querySelector('.tick')!.textContent).toBe('✓');
    expect(count.querySelector('.tick')!.getAttribute('aria-hidden')).toBe('true');
    const cs = getComputedStyle(count);
    expect(cs.borderTopLeftRadius).withContext('Pille').toBe('999px');
    expect(cs.boxShadow).withContext('kein Knopf-Schatten').toBe('none');
    expect(el.querySelector('.chapter')!.classList).toContain('with-title');
  });
});

/** b1-k-kurstitel-handy-gekuerzt: am Handy (en) ließ „← Courses" vom Titel nur „First endga…". */
describe('CoursePlayComponent — Kurstitel am Handy', () => {
  it('Zurück-Pille nur „←" (44 px, Text für Vorleser), der Titel steht ganz da — lange Titel auf zwei Zeilen', async () => {
    const frame = window.frameElement as HTMLElement | null;
    if (!frame) pending('Karma läuft nicht im iframe — der Viewport lässt sich nicht verstellen');
    localStorage.removeItem('rh-kids-progress-v1');
    const mk = (title: string): KidsCourseLine => ({
      id: 1, bookTitle: title, fen: '1R6/8/8/8/6p1/8/r6k/5K2 b - - 3 73', moves: 'g4g3 b8h8', startPly: 0,
      title: null, chapter: null, comment: null, moveComments: null, altMoves: null,
    });
    const lines = new BehaviorSubject<KidsCourseLine[]>([mk('First endgames')]);
    const api = jasmine.createSpyObj<KidsApiService>('KidsApiService', ['coursePuzzles']);
    api.coursePuzzles.and.callFake(() => of(lines.value));
    const prev = frame!.style.width;
    frame!.style.width = '390px';
    await new Promise<void>(r => requestAnimationFrame(() => r()));
    try {
      TestBed.configureTestingModule({
        imports: [CoursePlayComponent],
        providers: [
          provideRouter([]), provideTranslateService({ fallbackLang: 'en' }),
          { provide: KidsApiService, useValue: api },
          { provide: ActivatedRoute, useValue: { paramMap: new BehaviorSubject(convertToParamMap({ bookId: '7' })) } },
        ],
      });
      TestBed.inject(TranslateService).setTranslation('en', { kids: { courses: { title: 'Courses', progress: '{{done}} of {{total}} done' } } });
      await TestBed.inject(TranslateService).use('en');
      const f = TestBed.createComponent(CoursePlayComponent);
      f.detectChanges();
      const el = f.nativeElement as HTMLElement;
      const back = el.querySelector<HTMLElement>('.head .back')!;
      // d3-k-kid-back-pill-kurse-pfeil: kein führendes Leerzeichen mehr im Label (der Abstand kommt aus dem gap der Pille).
      expect(back.querySelector('.back-label')!.textContent).withContext('Vorleser hören „Courses"').toBe('Courses');
      expect(back.querySelector('.back-label')!.getBoundingClientRect().width).toBeLessThanOrEqual(1);
      expect(back.getBoundingClientRect().width).toBeGreaterThanOrEqual(44);
      expect(back.getBoundingClientRect().width).toBeLessThan(60);
      const h1 = el.querySelector<HTMLElement>('.head h1')!;
      expect(h1.scrollWidth).withContext('„First endgames" nicht gekürzt').toBeLessThanOrEqual(h1.clientWidth);
      const lineH = parseFloat(getComputedStyle(h1).lineHeight);
      expect(h1.getBoundingClientRect().height).toBeLessThan(lineH * 1.5);

      lines.next([mk('Endspiele mit Turm und Bauer gegen Turm — die wichtigsten Stellungen für Einsteiger')]);
      f.componentInstance.load(7);
      f.detectChanges();
      const h1Long = el.querySelector<HTMLElement>('.head h1')!;
      expect(h1Long.textContent).toContain('Endspiele mit Turm');
      expect(h1Long.getBoundingClientRect().height).withContext('umgebrochen').toBeGreaterThan(lineH * 1.5);
      expect(h1Long.getBoundingClientRect().height).withContext('höchstens zwei Zeilen').toBeLessThanOrEqual(lineH * 2 + 1);
    } finally {
      frame!.style.width = prev;
    }
  });
});

/** d3: Kopf der Kursaufgabe am Tablet hochkant und am Laptop. */
describe('CoursePlayComponent — Kopf am Tablet und am Laptop', () => {
  const mk = (id: number): KidsCourseLine => ({
    id, bookTitle: 'Erste Endspiele', fen: '1R6/8/8/8/6p1/8/r6k/5K2 b - - 3 73', moves: 'g4g3 b8h8', startPly: 0,
    title: 'Stellung I.1', chapter: '1. Einführung', comment: null, moveComments: null, altMoves: null,
  });

  async function renderAt(width: number, height: number): Promise<{ el: HTMLElement; restore: () => void }> {
    const frame = window.frameElement as HTMLElement | null;
    if (!frame) pending('Karma läuft nicht im iframe — der Viewport lässt sich nicht verstellen');
    localStorage.removeItem('rh-kids-progress-v1');
    const prev = [frame!.style.width, frame!.style.height];
    frame!.style.width = `${width}px`;
    frame!.style.height = `${height}px`;
    await new Promise<void>(r => requestAnimationFrame(() => r()));
    const api = jasmine.createSpyObj<KidsApiService>('KidsApiService', ['coursePuzzles']);
    api.coursePuzzles.and.returnValue(of([mk(1), mk(2)]));
    TestBed.configureTestingModule({
      imports: [CoursePlayComponent],
      providers: [
        provideRouter([]), provideTranslateService({ fallbackLang: 'en' }),
        { provide: KidsApiService, useValue: api },
        { provide: ActivatedRoute, useValue: { paramMap: new BehaviorSubject(convertToParamMap({ bookId: '7' })) } },
      ],
    });
    TestBed.inject(TranslateService).setTranslation('de', { kids: { courses: { title: 'Kurse', progress: '{{done}} von {{total}} geschafft' } } });
    await TestBed.inject(TranslateService).use('de');
    const f = TestBed.createComponent(CoursePlayComponent);
    f.detectChanges();
    return { el: f.nativeElement as HTMLElement, restore: () => { [frame!.style.width, frame!.style.height] = prev; } };
  }

  // d3-k-zurueck-pille-tablet-nur-pfeil: die Handy-Kurzformen hingen am Hochformat — am Tablet hochkant „←", „✓ 0/2", kein Kapitel.
  it('Tablet hochkant (820 × 1180): „← Kurse", langer Stand und Kapitel über dem Aufgabentitel', async () => {
    const { el, restore } = await renderAt(820, 1180);
    try {
      expect(el.querySelector('.back-label')!.getBoundingClientRect().width).toBeGreaterThan(20);
      expect(el.querySelector('.count-long')!.getBoundingClientRect().width).toBeGreaterThan(40);
      expect(getComputedStyle(el.querySelector('.count-short')!).display).toBe('none');
      expect(getComputedStyle(el.querySelector('.chapter.with-title')!).display).not.toBe('none');
    } finally {
      restore();
    }
  });

  // d3-k-kid-back-pill-kurse-pfeil: „←Kurse" — der Pfeil klebte am Wort.
  it('Zurück-Pille: zwischen „←" und „Kurse" steht ein Abstand', async () => {
    const { el, restore } = await renderAt(1366, 768);
    try {
      const back = el.querySelector<HTMLElement>('.head .back')!;
      const arrow = back.querySelector('[aria-hidden=true]')!.getBoundingClientRect();
      const label = back.querySelector('.back-label')!.getBoundingClientRect();
      expect(label.left - arrow.right).toBeGreaterThanOrEqual(3);
    } finally {
      restore();
    }
  });
});

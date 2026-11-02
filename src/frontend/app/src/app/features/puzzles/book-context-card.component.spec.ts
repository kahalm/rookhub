import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { BookContextCardComponent } from './book-context-card.component';

describe('BookContextCardComponent (Zerlegung Z18)', () => {
  function make(inputs: Record<string, unknown>) {
    TestBed.configureTestingModule({
      imports: [BookContextCardComponent],
      providers: [provideTranslateService({ fallbackLang: 'en' }), provideRouter([])],
    });
    const fixture = TestBed.createComponent(BookContextCardComponent);
    for (const [k, v] of Object.entries(inputs)) fixture.componentRef.setInput(k, v);
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement, c: fixture.componentInstance };
  }

  it('rendert mit Puzzle die Kontext-Karte mit Titel und Buchname', () => {
    const { el } = make({ hasPuzzle: true, state: 'AWAITING_USER_MOVE', titleDisplay: 'Linie 1', bookName: 'Mein Buch' });
    expect(el.querySelector('.context-card .meta-title')?.textContent).toContain('Linie 1');
    expect(el.querySelector('.context-card .book-name')?.textContent).toContain('Mein Buch');
  });

  it('ohne Puzzle: nur im Kurs/Wochenpost und nicht beim Laden die Karte „kein Puzzle“', () => {
    expect(make({ hasPuzzle: false, state: 'LOADING', inCourse: true }).el.querySelector('.context-card')).toBeNull();
    TestBed.resetTestingModule();
    const { el, c } = make({ hasPuzzle: false, state: 'COURSE_DONE', inCourse: true, courseCompleted: true, courseModeKind: 'random' });
    expect(el.querySelector('.context-card .ctx-done')).not.toBeNull();
    let restarted = 0;
    c.restartCourse.subscribe(() => restarted++);
    (el.querySelector('.ctx-nav button[mat-flat-button]') as HTMLButtonElement).click();
    expect(restarted).toBe(1);
  });

  it('Rückpfeil und ⓘ melden sich als Ausgaben; Tagespuzzle-Chip und Partie-Link aus den Eingaben', () => {
    const { el, c } = make({
      hasPuzzle: true, state: 'AWAITING_USER_MOVE', inCourse: true, courseTotal: 5,
      sourceGame: { kind: 'club', id: 9, ply: 12 },
    });
    let back = 0, stats = 0;
    c.back.subscribe(() => back++);
    c.statsToggled.subscribe(() => stats++);
    (el.querySelector('.ctx-back') as HTMLButtonElement).click();
    (el.querySelector('.ctx-stats-btn') as HTMLButtonElement).click();
    expect([back, stats]).toEqual([1, 1]);
    expect(c.sourceGameLink()).toEqual(['/club-games', 9]);
    expect(c.day()).toBeNull();
    TestBed.resetTestingModule();
    const daily = make({ hasPuzzle: true, state: 'SOLVED', isDaily: true, dailyDate: '20260714' });
    expect(daily.c.day()?.toISOString()).toBe('2026-07-14T00:00:00.000Z');
    expect(daily.el.querySelector('.daily-chip-row')).not.toBeNull();
  });
  // 0.882.0: Woodpecker läuft im Kurs-Löser — der Plan-Kopf des alten Lösers steht in der Kontext-Karte.
  const wpPlan = (over: Record<string, unknown> = {}): any => ({
    id: 3, name: '100 Endspiele, die Sie kennen müssen — Band zwei', setSize: 10, availableCount: 287,
    currentCycle: 2, maxCycles: 7, status: 'solving', cycles: [], ...over,
  });

  it('Woodpecker: Plan-Chip, Zyklus · Fortschritt · Resttage, Fortschrittsbalken; Rückpfeil zur Übersicht', () => {
    const { el, c } = make({ hasPuzzle: true, state: 'AWAITING_USER_MOVE', inWoodpecker: true,
      woodpeckerPlan: wpPlan(), woodpeckerAttempted: 4, woodpeckerPercent: 40, woodpeckerDaysLeft: 3 });
    expect(el.querySelector('.wp-chip')?.textContent).toContain('100 Endspiele');
    const parts = Array.from(el.querySelectorAll('.wp-meta > span')).map(x => x.textContent!.trim());
    expect(parts).toEqual(['woodpecker.cycleOf', 'woodpecker.cycleProgress', 'woodpecker.daysLeft']);
    expect(el.querySelector('mat-progress-bar')).not.toBeNull();
    let back = 0;
    c.back.subscribe(() => back++);
    (el.querySelector('.ctx-back') as HTMLButtonElement).click();
    expect(back).toBe(1);
  });

  it('Woodpecker Zyklus 1 (d3-rk-wp-loeser-im-satz-beschriftung): spricht vom wachsenden Satz, ohne Balken', () => {
    const { el } = make({ hasPuzzle: true, state: 'AWAITING_USER_MOVE', inWoodpecker: true,
      woodpeckerPlan: wpPlan({ currentCycle: 1, setSize: 12 }), woodpeckerRepeatHint: true });
    expect(el.querySelector('.wp-meta')?.textContent).toContain('woodpecker.setGrowing');
    expect(el.querySelector('mat-progress-bar')).toBeNull();
    expect(el.querySelector('.wp-repeat')?.textContent).toContain('woodpecker.repeatHint');
  });

  it('Woodpecker ohne Aufgabe: Abschluss bzw. „gibt es nicht (mehr)“ (c1-wp-loeser-404-verbindung), je mit „Zur Übersicht“', () => {
    const done = make({ hasPuzzle: false, state: 'COURSE_DONE', inWoodpecker: true, woodpeckerDoneText: 'Zyklus 2 geschafft' });
    expect(done.el.querySelector('.wp-done')?.textContent).toContain('Zyklus 2 geschafft');
    let over = 0;
    done.c.woodpeckerOverview.subscribe(() => over++);
    (done.el.querySelector('.wp-overview') as HTMLButtonElement).click();
    expect(over).toBe(1);
    TestBed.resetTestingModule();
    const missing = make({ hasPuzzle: false, state: 'COURSE_DONE', inWoodpecker: true, woodpeckerMissing: true });
    expect(missing.el.querySelector('.wp-missing')?.textContent).toContain('woodpecker.notFound');
    expect(missing.el.querySelector('.wp-done')).toBeNull();
    expect(missing.el.querySelector('.wp-overview')).not.toBeNull();
  });
});

import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import { SnackbarService } from '../../core/snackbar.service';
import { ConfirmService } from '../../shared/confirm-dialog/confirm-dialog.component';
import { CourseListItem, CourseService } from '../courses/course.service';
import { WoodpeckerListComponent } from './woodpecker-list.component';
import { WoodpeckerCycle, WoodpeckerPlan, WoodpeckerService, daysLeft, startOfLocalDayIso, todayYmd } from './woodpecker.service';

const cycle = (over: Partial<WoodpeckerCycle> = {}): WoodpeckerCycle => ({
  number: 1, targetDays: 28, startedAt: new Date(Date.now() - 86_400_000).toISOString(),
  deadline: new Date(Date.now() + 27 * 86_400_000).toISOString(), finishedAt: null,
  attempted: 0, solved: 0, timeSeconds: 0, ...over,
});

const plan = (over: Partial<WoodpeckerPlan> = {}): WoodpeckerPlan => ({
  id: 7, name: 'The Woodpecker Method', createdAt: '', completedAt: null,
  books: [{ bookId: 88, displayName: 'The Woodpecker Method', puzzleCount: 1128 }],
  setSize: 250, availableCount: 1128, currentCycle: 1, maxCycles: 7, status: 'solving', nextCycleAt: null,
  cycles: [cycle({ attempted: 250, solved: 220, timeSeconds: 18_000 })], ...over,
});

describe('WoodpeckerListComponent', () => {
  let service: jasmine.SpyObj<WoodpeckerService>;
  let confirmAnswer = true;

  function render(plans: WoodpeckerPlan[], courses: Partial<CourseListItem>[] = [{ puzzleCount: 10, isCalculation: false }]) {
    service = jasmine.createSpyObj<WoodpeckerService>('WoodpeckerService', ['list', 'startNextCycle', 'finishSet', 'delete', 'archive', 'unarchive']);
    service.list.and.returnValue(of(plans));
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [WoodpeckerListComponent],
      providers: [
        provideRouter([]), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: WoodpeckerService, useValue: service },
        { provide: MatDialog, useValue: { open: () => ({ afterClosed: () => of(null) }) } },
        { provide: SnackbarService, useValue: { warn: () => {}, success: () => {}, info: () => {} } },
        { provide: ConfirmService, useValue: { ask: () => of(confirmAnswer) } },
        { provide: CourseService, useValue: { getCourses: () => of(courses) } },
      ],
    });
    const fixture = TestBed.createComponent(WoodpeckerListComponent);
    fixture.detectChanges();
    return { fixture, c: fixture.componentInstance, el: fixture.nativeElement as HTMLElement };
  }

  it('ohne Training: Leerzustand mit genau einem Knopf zum Anlegen', () => {
    const { el } = render([]);
    expect(el.querySelector('.empty-state')).not.toBeNull();
    expect(el.querySelectorAll('.empty-state button').length).toBe(1);
    expect(el.querySelector('.plan')).toBeNull();
  });

  it('d3-rk-woodpecker-leerdialog-luecke-abbrechen: ohne lösbaren Kurs direkt „Zu den Kursen“ statt „Neues Training“', () => {
    const { el } = render([], [{ puzzleCount: 0, isCalculation: false }, { puzzleCount: 30, isCalculation: true }]);
    const link = el.querySelector<HTMLAnchorElement>('.empty-state a');
    expect(link?.getAttribute('href')).toBe('/courses');
    expect(el.querySelectorAll('.empty-state button').length).toBe(0);
    expect(el.querySelector('.empty-state')!.textContent).toContain('woodpecker.create.noCourses');
  });

  it('d3-rk-woodpecker-balken-zeit-statt-satz: Zyklus 1 — Zeitbalken folgt der Fristzeile und ist beschriftet', () => {
    const { el } = render([plan({ setSize: 0 })]);
    const state = el.querySelector('.state')!;
    const kids = Array.from(state.children);
    const deadline = state.querySelector('.deadline')!;
    const bar = state.querySelector('mat-progress-bar.time-bar')!;
    expect(state.querySelectorAll('mat-progress-bar').length).toBe(1);
    expect(kids.indexOf(bar)).toBe(kids.indexOf(deadline) + 1);
    expect(bar.getAttribute('aria-label')).toBe('woodpecker.timeBar');
    // der Satz-Text steht darüber, ohne eigenen Balken
    expect(kids.indexOf(state.querySelector('p')!)).toBeLessThan(kids.indexOf(deadline));
  });

  it('laufender Zyklus 1: „Weiter lösen“ führt zum Löser, Fortschritt nach Satzgröße', () => {
    const { el } = render([plan()]);
    const link = el.querySelector<HTMLAnchorElement>('.actions a');
    expect(link?.getAttribute('href')).toBe('/woodpecker/7/solve');
    expect(el.querySelector('.state')?.textContent).toContain('woodpecker.setGrowing');
    // Noch kein Zyklus beendet → keine Zyklus-Tabelle.
    expect(el.querySelector('table.cycles')).toBeNull();
  });

  it('in der Pause ist „Zyklus starten“ gesperrt und nennt den Grund darunter', () => {
    const finished = cycle({ finishedAt: new Date().toISOString(), attempted: 250 });
    const { el } = render([plan({ status: 'break', nextCycleAt: new Date(Date.now() + 86_400_000).toISOString(), cycles: [finished] })]);
    const btn = el.querySelector<HTMLButtonElement>('.locked button');
    expect(btn?.disabled).toBeTrue();
    expect(el.querySelector('.locked .reason')).not.toBeNull();
    expect(el.querySelectorAll('table.cycles tbody tr').length).toBe(1);
  });

  it('Zyklus-Tabelle: „Gelöst“ in Prozentpunkten (220 von 250 → 88%, nicht 1%)', () => {
    const c1 = cycle({ finishedAt: new Date(Date.now() - 2 * 86_400_000).toISOString(), attempted: 250, solved: 220 });
    const c2 = cycle({ number: 2, targetDays: 14, finishedAt: new Date().toISOString(), attempted: 58, solved: 29 });
    const { el } = render([plan({ status: 'ready', cycles: [c1, c2] })]);
    // Ohne Leerzeichen verglichen: je Oberflächensprache „88%“ (en/hu) oder „88 %“ mit U+00A0 (de/hr) — und welche
    // Sprache gerade gilt, hängt im vollen Lauf von den Specs davor ab (CI rot bei 0.953.4, lokal im Teil-Lauf grün).
    const solved = Array.from(el.querySelectorAll('table.cycles tbody tr'))
      .map(tr => tr.querySelectorAll('td')[2].textContent!.replace(/[\s\u00a0]/g, ''));
    expect(solved).toEqual(['88%', '50%']);
  });

  it('bereit: Klick startet den nächsten Zyklus und ersetzt die Karte', () => {
    const finished = cycle({ finishedAt: new Date(Date.now() - 2 * 86_400_000).toISOString(), attempted: 250 });
    const started = plan({ currentCycle: 2, cycles: [finished, cycle({ number: 2, targetDays: 14 })] });
    const { fixture, el } = render([plan({ status: 'ready', cycles: [finished] })]);
    service.startNextCycle.and.returnValue(of(started));
    el.querySelector<HTMLButtonElement>('.actions button')!.click();
    fixture.detectChanges();
    expect(service.startNextCycle).toHaveBeenCalledWith(7);
    expect(el.querySelector<HTMLAnchorElement>('.actions a')?.getAttribute('href')).toBe('/woodpecker/7/solve');
  });

  it('Löschen fragt nach und entfernt die Karte nur bei Ja', () => {
    const { fixture, c, el } = render([plan()]);
    service.delete.and.returnValue(of(void 0));
    confirmAnswer = false;
    c.remove(c.plans()[0]);
    expect(service.delete).not.toHaveBeenCalled();
    confirmAnswer = true;
    c.remove(c.plans()[0]);
    fixture.detectChanges();
    expect(service.delete).toHaveBeenCalledWith(7);
    expect(el.querySelector('.plan')).toBeNull();
  });

  // ===== Ein Training + Archiv (0.958.0) =====

  const archivedPlan = (over: Partial<WoodpeckerPlan> = {}) => plan({
    id: 9, name: 'Alt', status: 'archived', archivedAt: '2026-11-01T10:00:00Z',
    cycles: [cycle({ attempted: 40, solved: 30 })], ...over,
  });

  /** ⋮ einer Archiv-Karte öffnen; die Menüeinträge liegen im Overlay am body. */
  function openArchiveMenu(fixture: ReturnType<typeof render>['fixture'], el: HTMLElement) {
    el.querySelector<HTMLButtonElement>('.archived-plan .plan-head button')!.click();
    fixture.detectChanges();
    return document.querySelector<HTMLButtonElement>('.restore-item')!;
  }

  it('Archiv-Abschnitt: zugeklappt unter den aktiven, mit Zahl; archivierte nicht als Karte oben', () => {
    const { el } = render([plan(), archivedPlan(), archivedPlan({ id: 10, archivedAt: '2026-11-05T10:00:00Z' })]);
    expect(el.querySelectorAll('.plans .plan').length).toBe(1);
    const details = el.querySelector<HTMLDetailsElement>('details.archive')!;
    expect(details).not.toBeNull();
    expect(details.open).toBeFalse();
    expect(details.querySelector('summary')!.textContent).toContain('woodpecker.archiveSection');
    const cards = Array.from(details.querySelectorAll('.archived-plan'));
    expect(cards.length).toBe(2);
    expect(cards[0].textContent).toContain('woodpecker.archivedAt');            // zuletzt archiviertes zuerst
    expect(cards[0].querySelector('table.cycles')).not.toBeNull();             // Zyklen-Tabelle bleibt lesbar
    expect(el.querySelector('.archived-plan .actions')).toBeNull();            // keine Hauptaktion
  });

  it('ohne archivierte kein Archiv-Abschnitt', () => {
    const { el } = render([plan()]);
    expect(el.querySelector('details.archive')).toBeNull();
  });

  it('Archivieren fragt nach (Handlung im Knopf) und legt die Karte ins Archiv', () => {
    const { fixture, c, el } = render([plan()]);
    const ask = spyOn(TestBed.inject(ConfirmService), 'ask').and.returnValue(of(true));
    service.archive.and.returnValue(of(plan({ status: 'archived', archivedAt: new Date().toISOString() })));
    c.archive(c.plans()[0]);
    fixture.detectChanges();
    expect(ask).toHaveBeenCalledOnceWith('woodpecker.archiveConfirm', { name: 'The Woodpecker Method' }, { confirm: 'woodpecker.archive' });
    expect(service.archive).toHaveBeenCalledWith(7);
    expect(el.querySelectorAll('.plans .plan').length).toBe(0);
    expect(el.querySelectorAll('details.archive .archived-plan').length).toBe(1);
    expect(el.querySelector('.empty-state')!.textContent).toContain('woodpecker.emptyArchived');
  });

  it('Archivieren abgelehnt → nichts passiert', () => {
    const { c } = render([plan()]);
    spyOn(TestBed.inject(ConfirmService), 'ask').and.returnValue(of(false));
    c.archive(c.plans()[0]);
    expect(service.archive).not.toHaveBeenCalled();
  });

  it('Wiederherstellen ist neben einem aktiven Training gesperrt und nennt den Grund im Eintrag', () => {
    const { fixture, el } = render([plan(), archivedPlan()]);
    el.querySelector<HTMLDetailsElement>('details.archive')!.open = true;
    const restore = openArchiveMenu(fixture, el);
    expect(restore.disabled).toBeTrue();
    expect(restore.querySelector('.why')?.textContent).toContain('woodpecker.restoreBlocked');
    expect(document.querySelector('.cdk-overlay-container .delete-item')).not.toBeNull();   // Löschen bleibt
  });

  it('Wiederherstellen ohne aktives Training: frei, holt den Plan zurück nach oben', () => {
    const { fixture, c, el } = render([archivedPlan()]);
    const restore = openArchiveMenu(fixture, el);
    expect(restore.disabled).toBeFalse();
    expect(restore.querySelector('.why')).toBeNull();
    service.unarchive.and.returnValue(of(archivedPlan({ status: 'solving', archivedAt: null })));
    c.restore(c.plans()[0]);
    fixture.detectChanges();
    expect(service.unarchive).toHaveBeenCalledWith(9);
    expect(el.querySelectorAll('.plans .plan').length).toBe(1);
    expect(el.querySelector('details.archive')).toBeNull();
  });

  it('Neues Training mit aktivem: Dialog bekommt das bisherige zum Archivieren, danach Liste neu geladen', () => {
    const { c } = render([plan()]);
    const created = plan({ id: 11, name: 'Neu' });
    const open = spyOn(TestBed.inject(MatDialog), 'open').and.returnValue({ afterClosed: () => of(created) } as never);
    service.list.calls.reset();
    service.list.and.returnValue(of([created, plan({ status: 'archived', archivedAt: new Date().toISOString() })]));
    c.create();
    expect((open.calls.mostRecent().args[1] as { data: { current: WoodpeckerPlan[] } }).data.current.map(p => p.id)).toEqual([7]);
    expect(service.list).toHaveBeenCalledTimes(1);
    expect(c.active().map(p => p.id)).toEqual([11]);
    expect(c.archived().map(p => p.id)).toEqual([7]);
  });

  it('c1-Nachprüfung: letztes Wort + (?) in einem nicht umbrechenden Teil (Tablet brach das (?) allein um)', () => {
    const { el, c } = render([]);
    const tail = el.querySelector('p.intro .intro-tail') as HTMLElement;
    expect(tail).not.toBeNull();
    expect(tail.querySelector('app-help-hint')).not.toBeNull();
    expect(getComputedStyle(tail).whiteSpace).toBe('nowrap');
    // Kopf + Schwanz ergeben den ganzen Satz, der Schwanz ist genau ein Wort.
    expect(c.introHead('Nach Smith und Tikkanen.') + c.introTail('Nach Smith und Tikkanen.')).toBe('Nach Smith und Tikkanen.');
    expect(c.introTail('Nach Smith und Tikkanen.')).toBe('Tikkanen.');
    expect(c.introTail('Einwort')).toBe('Einwort');
    expect(c.introHead('Einwort')).toBe('');
  });

  it('Zieltage des nächsten Zyklus halbiert und aufgerundet', () => {
    const { c } = render([]);
    expect([28, 14, 7, 4, 2, 1].map(d => c.nextDays(cycle({ targetDays: d })))).toEqual([14, 7, 4, 2, 1, 1]);
  });
});

/** Kompiliertes CSS ohne Leerraum und ohne %NS%-Platzhalter (Muster features/page-width.spec.ts). */
const listCss = () =>
  (((WoodpeckerListComponent as unknown as { ɵcmp: { styles?: string[] } }).ɵcmp.styles) ?? []).join('\n')
    .replace(/%NS%/g, '').replace(/\[_ngcontent-%COMP%\]/g, '').replace(/\s/g, '');

describe('WoodpeckerListComponent (UI-Sweep c1)', () => {
  it('c1-wp-intro-hilfe-eigene-zeile: Einleitung ist kein Flex-Container, das (?) steht im Satz', () => {
    const intro = /\.intro\{[^}]*\}/.exec(listCss())![0];
    expect(intro).not.toContain('display:flex');
    expect(intro).not.toContain('flex-wrap');
  });

  it('d6-rk-woodpecker-kartentitel-abgeschnitten: Kartentitel bricht um (bis zwei Zeilen), ⋮ bleibt oben', () => {
    const h2 = /\.plan-titleh2\{[^}]*\}/.exec(listCss())![0];
    expect(h2).not.toContain('white-space:nowrap');
    expect(h2).toContain('-webkit-line-clamp:2');
    expect(h2).toContain('overflow-wrap:anywhere');
    expect(/\.plan-head\{[^}]*\}/.exec(listCss())![0]).toContain('align-items:flex-start');
  });

  it('c1-wp-woodpecker-seitenpolster-96vw: Seite ohne 96vw, Rand = 16-px-Polster', () => {
    const page = /\.page\{[^}]*\}/.exec(listCss())![0];
    expect(page).toContain('max-width:var(--page-max-width)');
    expect(page).not.toContain('96vw');
    expect(page).toContain('padding:16px');
  });

  it('c1-wp-dialog-fokus-datum: Dialoge öffnen mit Fokus auf dem Dialog, Breiten nach DESIGN 5.6', () => {
    TestBed.resetTestingModule();
    const dialogOpen = jasmine.createSpy('open').and.returnValue({ afterClosed: () => of(null) });
    TestBed.configureTestingModule({
      imports: [WoodpeckerListComponent],
      providers: [
        provideRouter([]), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: WoodpeckerService, useValue: { list: () => of([]) } },
        { provide: MatDialog, useValue: { open: dialogOpen } },
        { provide: SnackbarService, useValue: { warn: () => {} } },
        { provide: ConfirmService, useValue: { ask: () => of(true) } },
      ],
    });
    const c = TestBed.createComponent(WoodpeckerListComponent).componentInstance;
    c.create();
    expect(dialogOpen.calls.mostRecent().args[1]).toEqual(jasmine.objectContaining({ width: '560px', maxWidth: '94vw', autoFocus: 'dialog' }));
    c.changeStart(plan());
    expect(dialogOpen.calls.mostRecent().args[1]).toEqual(jasmine.objectContaining({ width: '440px', maxWidth: '94vw', autoFocus: 'dialog' }));
  });
});

describe('woodpecker.service helpers', () => {
  it('startOfLocalDayIso = lokale Mitternacht des Tages', () => {
    const d = new Date(startOfLocalDayIso('2026-10-14'));
    expect([d.getFullYear(), d.getMonth(), d.getDate(), d.getHours(), d.getMinutes()]).toEqual([2026, 9, 14, 0, 0]);
  });

  it('todayYmd mit führenden Nullen', () => {
    expect(todayYmd(new Date(2026, 0, 5, 23, 59))).toBe('2026-01-05');
  });

  it('daysLeft rundet auf und wird nie negativ', () => {
    const now = Date.UTC(2026, 9, 14, 12);
    expect(daysLeft(new Date(now + 36 * 3_600_000).toISOString(), now)).toBe(2);
    expect(daysLeft(new Date(now - 1000).toISOString(), now)).toBe(0);
  });
});

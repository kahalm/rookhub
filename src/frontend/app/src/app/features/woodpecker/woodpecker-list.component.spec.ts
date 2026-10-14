import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import { SnackbarService } from '../../core/snackbar.service';
import { ConfirmService } from '../../shared/confirm-dialog/confirm-dialog.component';
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

  function render(plans: WoodpeckerPlan[]) {
    service = jasmine.createSpyObj<WoodpeckerService>('WoodpeckerService', ['list', 'startNextCycle', 'finishSet', 'delete']);
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

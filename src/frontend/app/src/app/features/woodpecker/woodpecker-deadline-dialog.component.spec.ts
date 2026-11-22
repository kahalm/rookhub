import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialog, MatDialogRef } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { SnackbarService } from '../../core/snackbar.service';
import { WoodpeckerDeadlineDialogComponent, WoodpeckerDeadlinePrompt } from './woodpecker-deadline-dialog.component';
import { WoodpeckerCycle, WoodpeckerPlan, WoodpeckerService } from './woodpecker.service';

const DAY = 86_400_000;

const cycle2 = (over: Partial<WoodpeckerCycle> = {}): WoodpeckerCycle => ({
  number: 2, targetDays: 14, startedAt: new Date(Date.now() - 15 * DAY).toISOString(),
  deadline: new Date(Date.now() - DAY).toISOString(), finishedAt: null, attempted: 241, solved: 200, timeSeconds: 9000, ...over,
});

const duePlan = (over: Partial<WoodpeckerPlan> = {}): WoodpeckerPlan => ({
  id: 1, name: 'The Woodpecker Method', createdAt: '', completedAt: null, books: [], setSize: 388, availableCount: 1128,
  currentCycle: 2, maxCycles: 7, status: 'solving', nextCycleAt: null, deadlinePassed: true, suggestedExtensionDays: 8,
  cycles: [{ ...cycle2(), number: 1, targetDays: 28, finishedAt: '2026-11-06T22:00:00Z', attempted: 388 }, cycle2()], ...over,
});

describe('WoodpeckerDeadlineDialogComponent (1.007.0: Frist um – verlängern oder abschließen?)', () => {
  let service: jasmine.SpyObj<WoodpeckerService>;
  let ref: jasmine.SpyObj<MatDialogRef<WoodpeckerDeadlineDialogComponent>>;
  let snackbar: jasmine.SpyObj<SnackbarService>;

  function render(plan: WoodpeckerPlan) {
    service = jasmine.createSpyObj<WoodpeckerService>('WoodpeckerService', ['extendCycle', 'finishCycle']);
    ref = jasmine.createSpyObj('MatDialogRef', ['close']);
    snackbar = jasmine.createSpyObj<SnackbarService>('SnackbarService', ['warn', 'success']);
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [WoodpeckerDeadlineDialogComponent],
      providers: [
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: MAT_DIALOG_DATA, useValue: { plan } },
        { provide: MatDialogRef, useValue: ref },
        { provide: WoodpeckerService, useValue: service },
        { provide: SnackbarService, useValue: snackbar },
      ],
    });
    const fixture = TestBed.createComponent(WoodpeckerDeadlineDialogComponent);
    fixture.detectChanges();
    return { fixture, c: fixture.componentInstance, el: fixture.nativeElement as HTMLElement };
  }

  it('belegt die Tage mit dem Vorschlag des Servers und rechnet die offenen Aufgaben (Satz − bearbeitet)', () => {
    const { c, el } = render(duePlan());
    expect(c.days()).toBe(8);
    expect(c.open).toBe(147);
    expect(c.nextDays).toBe(7);
    expect(el.querySelector('.wdl-intro')!.textContent).toContain('woodpecker.deadline.intro');
    expect(el.querySelectorAll('mat-dialog-actions button').length).toBe(3);
  });

  it('Zyklus 1: offen = Kursaufgaben außerhalb des Satzes, eigener Text', () => {
    const c1 = { ...cycle2(), number: 1, targetDays: 28, attempted: 250 };
    const { c, el } = render(duePlan({ currentCycle: 1, setSize: 250, cycles: [c1], suggestedExtensionDays: null }));
    expect(c.open).toBe(878);
    expect(c.days()).toBe(28);   // ohne Vorschlag: Zieldauer
    expect(el.querySelector('.wdl-intro')!.textContent).toContain('woodpecker.deadline.introSet');
  });

  it('„Verlängern“ speichert die Tage und gibt den neuen Plan zurück', () => {
    const { c, el } = render(duePlan());
    const after = duePlan({ deadlinePassed: false });
    service.extendCycle.and.returnValue(of(after));
    c.days.set(3);
    el.querySelector<HTMLButtonElement>('.wdl-extend')!.click();
    expect(service.extendCycle).toHaveBeenCalledOnceWith(1, 3);
    expect(ref.close).toHaveBeenCalledOnceWith(after);
    expect(snackbar.success).toHaveBeenCalled();
  });

  it('ungültige Tage sperren „Verlängern“', () => {
    const { c, el, fixture } = render(duePlan());
    c.days.set(0);
    fixture.detectChanges();
    expect(el.querySelector<HTMLButtonElement>('.wdl-extend')!.disabled).toBeTrue();
    c.days.set(29);
    fixture.detectChanges();
    expect(el.querySelector<HTMLButtonElement>('.wdl-extend')!.disabled).toBeTrue();
    c.extend();
    expect(service.extendCycle).not.toHaveBeenCalled();
  });

  it('„Zyklus abschließen“ ruft finish-cycle und gibt den Plan zurück', () => {
    const { el } = render(duePlan());
    const closed = duePlan({ deadlinePassed: false, status: 'ready' });
    service.finishCycle.and.returnValue(of(closed));
    el.querySelector<HTMLButtonElement>('.wdl-finish')!.click();
    expect(service.finishCycle).toHaveBeenCalledOnceWith(1);
    expect(ref.close).toHaveBeenCalledOnceWith(closed);
  });

  it('Fehler: Fenster bleibt mit der Wahl offen, Warnung', () => {
    const { c, el } = render(duePlan());
    service.finishCycle.and.returnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    el.querySelector<HTMLButtonElement>('.wdl-finish')!.click();
    expect(ref.close).not.toHaveBeenCalled();
    expect(snackbar.warn).toHaveBeenCalled();
    expect(c.busy()).toBeFalse();
  });

  it('offline: nichts gesendet, Hinweis, Fenster bleibt offen', () => {
    const { el } = render(duePlan());
    const spy = spyOnProperty(navigator, 'onLine', 'get').and.returnValue(false);
    try {
      el.querySelector<HTMLButtonElement>('.wdl-extend')!.click();
    } finally { spy.and.callThrough(); }
    expect(service.extendCycle).not.toHaveBeenCalled();
    expect(snackbar.warn).toHaveBeenCalledWith('woodpecker.deadline.offline');
    expect(ref.close).not.toHaveBeenCalled();
  });

  it('„Später“ schließt ohne Ergebnis', () => {
    const { el } = render(duePlan());
    el.querySelector<HTMLButtonElement>('.wdl-later')!.click();
    expect(ref.close).toHaveBeenCalledOnceWith();
    expect(service.extendCycle).not.toHaveBeenCalled();
    expect(service.finishCycle).not.toHaveBeenCalled();
  });
});

describe('WoodpeckerDeadlinePrompt', () => {
  let open: jasmine.Spy;
  let prompt: WoodpeckerDeadlinePrompt;

  beforeEach(() => {
    open = jasmine.createSpy('open').and.returnValue({ afterClosed: () => of(undefined) });
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [{ provide: MatDialog, useValue: { open } }] });
    prompt = TestBed.inject(WoodpeckerDeadlinePrompt);
  });

  it('fragt nur bei laufendem Zyklus mit abgelaufener Frist', () => {
    expect(prompt.shouldAsk(duePlan())).toBeTrue();
    expect(prompt.shouldAsk(duePlan({ deadlinePassed: false }))).toBeFalse();
    expect(prompt.shouldAsk(duePlan({ deadlinePassed: undefined }))).toBeFalse();   // älterer Server
    expect(prompt.shouldAsk(duePlan({ status: 'archived' }))).toBeFalse();
    expect(prompt.shouldAsk(null)).toBeFalse();
  });

  it('von selbst nur einmal je Plan und Frist; „Später“ → null; neue Frist fragt wieder', () => {
    let result: WoodpeckerPlan | null | undefined;
    prompt.askIfDue(duePlan()).subscribe(p => result = p);
    expect(open).toHaveBeenCalledTimes(1);
    expect(open.calls.mostRecent().args[1].data.plan.id).toBe(1);
    expect(result).toBeNull();
    prompt.askIfDue(duePlan()).subscribe();
    expect(open).toHaveBeenCalledTimes(1);
    const later = duePlan({ cycles: [duePlan().cycles[0], cycle2({ deadline: new Date(Date.now() - 1000).toISOString() })] });
    prompt.askIfDue(later).subscribe();
    expect(open).toHaveBeenCalledTimes(2);
    prompt.open(later).subscribe();   // Knopf auf der Karte öffnet immer
    expect(open).toHaveBeenCalledTimes(3);
  });

  it('gibt den neuen Plan nach einer Entscheidung weiter', () => {
    const after = duePlan({ deadlinePassed: false });
    open.and.returnValue({ afterClosed: () => of(after) });
    let result: WoodpeckerPlan | null | undefined;
    prompt.askIfDue(duePlan()).subscribe(p => result = p);
    expect(result).toBe(after);
  });
});

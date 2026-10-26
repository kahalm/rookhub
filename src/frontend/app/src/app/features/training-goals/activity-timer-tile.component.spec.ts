import { TestBed, fakeAsync, tick, discardPeriodicTasks } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { ActivityTimerTileComponent } from './activity-timer-tile.component';
import { ActivityPreset, ActivityTimer, TrainingGoalService } from './training-goals.service';
import { ConfirmService } from '../../shared/confirm-dialog/confirm-dialog.component';

describe('ActivityTimerTileComponent', () => {
  it('creates (template AOT-compiles + DI resolves)', async () => {
    await TestBed.configureTestingModule({
      imports: [ActivityTimerTileComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(ActivityTimerTileComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  // ---- F5-008: Poll-Fehler behält den Zustand, 409 zeigt den laufenden Timer ----

  const running: ActivityTimer = { label: 'Coaching', kind: 'Coaching', startedAt: '2026-10-26T08:00:00Z', elapsedSeconds: 120 };
  const preset: ActivityPreset = { id: 3, label: 'Buch', kind: 'OfflineStudy' } as ActivityPreset;

  function make(goals: Partial<Record<keyof TrainingGoalService, jasmine.Spy>>, answer = true) {
    const goalsStub = {
      getTimer: jasmine.createSpy('getTimer').and.returnValue(of(null)),
      listPresets: jasmine.createSpy('listPresets').and.returnValue(of([])),
      startTimer: jasmine.createSpy('startTimer').and.returnValue(of(running)),
      ...goals,
    };
    const confirm = { ask: jasmine.createSpy('ask').and.returnValue(of(answer)) };
    TestBed.configureTestingModule({
      imports: [ActivityTimerTileComponent],
      providers: [
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: TrainingGoalService, useValue: goalsStub },
        { provide: ConfirmService, useValue: confirm },
      ],
    });
    const c = TestBed.createComponent(ActivityTimerTileComponent).componentInstance;
    return { c, goalsStub, confirm };
  }

  it('keeps the running timer when a later poll fails', fakeAsync(() => {
    let call = 0;
    const getTimer = jasmine.createSpy('getTimer').and.callFake(() => (call++ === 0 ? of(running) : throwError(() => ({ status: 500 }))));
    const { c } = make({ getTimer });
    c.ngOnInit();
    tick(0);
    expect(c.running?.label).toBe('Coaching');
    tick(30_000);
    expect(getTimer).toHaveBeenCalledTimes(2);
    expect(c.running?.label).toBe('Coaching');   // nicht ausgeblendet
    c.ngOnDestroy();
    discardPeriodicTasks();
  }));

  it('a failing first poll ends the loading state without inventing a timer', fakeAsync(() => {
    const { c } = make({ getTimer: jasmine.createSpy('getTimer').and.returnValue(throwError(() => ({ status: 500 }))) });
    c.ngOnInit();
    tick(0);
    expect(c.loading).toBeFalse();
    expect(c.running).toBeNull();
    c.ngOnDestroy();
    discardPeriodicTasks();
  }));

  it('starts without replace when nothing is known to run', () => {
    const { c, goalsStub, confirm } = make({});
    c.start(preset);
    expect(confirm.ask).not.toHaveBeenCalled();
    expect(goalsStub.startTimer).toHaveBeenCalledWith({ presetId: 3 }, false);
  });

  it('on 409 shows the running timer and replaces only after the confirm', () => {
    const startTimer = jasmine.createSpy('startTimer').and.returnValues(
      throwError(() => ({ status: 409, error: { message: 'running', timer: running } })), of({ ...running, label: 'Buch' }));
    const { c, confirm } = make({ startTimer });
    c.start(preset);
    expect(confirm.ask).toHaveBeenCalledWith('trainingGoals.timer.replaceConfirm');
    expect(startTimer.calls.argsFor(1)).toEqual([{ presetId: 3 }, true]);
    expect(c.running?.label).toBe('Buch');
    c.ngOnDestroy();
  });

  it('on 409 keeps the running timer when the replace is declined', () => {
    const startTimer = jasmine.createSpy('startTimer').and.returnValue(
      throwError(() => ({ status: 409, error: { message: 'running', timer: running } })));
    const { c } = make({ startTimer }, false);
    c.start(preset);
    expect(startTimer).toHaveBeenCalledTimes(1);
    expect(c.running?.label).toBe('Coaching');
    c.ngOnDestroy();
  });
});

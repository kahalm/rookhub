import { Injector, runInInjectionContext } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { AuthService } from '@rh/core/auth.service';
import { NotificationService } from '@rh/core/notification.service';
import { SnackbarService } from '@rh/core/snackbar.service';
import { ROUND_MONITOR_POLL_MS, TournamentRoundMonitor } from './tournament-round-monitor';

/**
 * UI-Sweep 2026-10-11 (r2-t-public-actions): der gemeinsame „Beobachten"-Baustein der Detailansicht und des geteilten
 * Links. Geprueft wird die Abfrage selbst (neue Runde melden + nachladen, serverseitig beendet, abgelaufen), die beide
 * Seiten vorher je fuer sich haetten haben muessen.
 */
describe('TournamentRoundMonitor', () => {
  let http: HttpTestingController;
  let newRounds: number[];
  let monitor: TournamentRoundMonitor;
  let destroy: () => void;
  let signedIn: boolean;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideTranslateService({ fallbackLang: 'en' })],
    });
    signedIn = true;
    spyOnProperty(TestBed.inject(AuthService), 'isLoggedIn', 'get').and.callFake(() => signedIn);
    spyOn(TestBed.inject(NotificationService), 'notify');
    http = TestBed.inject(HttpTestingController);
    newRounds = [];
    jasmine.clock().install();
    // Eigener Injector je Test: sein destroy() loest die DestroyRef aus und muss die Abfrage beenden.
    const injector = Injector.create({ providers: [], parent: TestBed.inject(Injector) });
    monitor = runInInjectionContext(injector, () => new TournamentRoundMonitor(() => '4711', r => newRounds.push(r)));
    destroy = () => (injector as unknown as { destroy(): void }).destroy();
  });

  afterEach(() => jasmine.clock().uninstall());

  it('meldet eine neue Runde genau einmal und laedt nach', () => {
    const warn = spyOn(TestBed.inject(SnackbarService), 'warn');
    monitor.load();
    http.expectOne('/api/tournament-monitors/4711').flush({ active: true, activeUntil: null, lastKnownRounds: 3 });
    expect(monitor.monitoring()).toBeTrue();

    jasmine.clock().tick(ROUND_MONITOR_POLL_MS);
    http.expectOne('/api/tournament-monitors/4711').flush({ active: true, activeUntil: null, lastKnownRounds: 3 });
    expect(newRounds).toEqual([]);

    jasmine.clock().tick(ROUND_MONITOR_POLL_MS);
    http.expectOne('/api/tournament-monitors/4711').flush({ active: true, activeUntil: null, lastKnownRounds: 4 });
    expect(newRounds).toEqual([4]);
    expect(warn).toHaveBeenCalledTimes(1);
    destroy();
    jasmine.clock().tick(ROUND_MONITOR_POLL_MS);
    http.expectNone('/api/tournament-monitors/4711');
  });

  it('hoert auf, wenn der Server die Beobachtung beendet hat', () => {
    monitor.load();
    http.expectOne('/api/tournament-monitors/4711').flush({ active: true, activeUntil: null, lastKnownRounds: 2 });
    jasmine.clock().tick(ROUND_MONITOR_POLL_MS);
    http.expectOne('/api/tournament-monitors/4711').flush({ active: false, activeUntil: null, lastKnownRounds: 2 });
    expect(monitor.monitoring()).toBeFalse();
    jasmine.clock().tick(ROUND_MONITOR_POLL_MS);
    http.expectNone('/api/tournament-monitors/4711');
    destroy();
  });

  it('hoert nach Ablauf von selbst auf, ohne noch einmal zu fragen', () => {
    jasmine.clock().mockDate(new Date('2026-10-11T10:00:00Z'));
    monitor.load();
    http.expectOne('/api/tournament-monitors/4711')
      .flush({ active: true, activeUntil: '2026-10-11T10:00:10Z', lastKnownRounds: 1 });
    jasmine.clock().tick(ROUND_MONITOR_POLL_MS);
    expect(monitor.monitoring()).toBeFalse();
    expect(monitor.activeUntil()).toBeNull();
    http.expectNone('/api/tournament-monitors/4711');
    destroy();
  });

  it('fragt abgemeldet gar nicht erst', () => {
    signedIn = false;
    monitor.load();
    http.expectNone('/api/tournament-monitors/4711');
    destroy();
  });
});

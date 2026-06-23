import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { Router } from '@angular/router';
import { SwUpdate } from '@angular/service-worker';
import { TranslateService } from '@ngx-translate/core';
import { AppComponent } from './app.component';
import { LocaleService } from './core/locale.service';
import { AuthService } from './core/auth.service';
import { MenuService } from './core/menu.service';
import { DiscordLinkService } from './core/discord-link.service';
import { OfflineQueueService } from './core/offline-queue.service';
import { OfflinePrefetchService } from './core/offline-prefetch.service';
import { PwaInstallService } from './core/pwa-install.service';
import { ClientLogService } from './core/client-log.service';
import { SnackbarService } from './core/snackbar.service';
import { StockfishService } from './features/puzzles/stockfish.service';
import { AnalysisEngineService } from './features/analysis/analysis-engine.service';
import { ThemeService } from './core/theme.service';

// Sichert das v0.181.1-Refactoring ab: die langlebigen Root-Subscriptions
// (router.events, swUpdate.versionUpdates/unrecoverable) hängen jetzt an
// takeUntilDestroyed → nach dem Zerstören der Komponente bleibt kein Observer
// auf den Quell-Streams zurück.
describe('AppComponent lifecycle', () => {
  let routerEvents: Subject<unknown>;
  let versionUpdates: Subject<unknown>;
  let unrecoverable: Subject<unknown>;

  beforeEach(() => {
    routerEvents = new Subject<unknown>();
    versionUpdates = new Subject<unknown>();
    unrecoverable = new Subject<unknown>();

    TestBed.configureTestingModule({
      imports: [AppComponent],
      providers: [
        { provide: Router, useValue: { events: routerEvents } },
        { provide: SwUpdate, useValue: { isEnabled: true, versionUpdates, unrecoverable } },
        { provide: LocaleService, useValue: { init: () => {} } },
        { provide: AuthService, useValue: { isLoggedIn: false, isAdmin: false, isImpersonating: false } },
        { provide: MenuService, useValue: {} },
        { provide: DiscordLinkService, useValue: {} },
        { provide: SnackbarService, useValue: {} },
        { provide: TranslateService, useValue: { instant: (k: string) => k } },
        { provide: OfflineQueueService, useValue: {} },
        { provide: OfflinePrefetchService, useValue: { prefetchAll: () => {} } },
        { provide: ClientLogService, useValue: { report: () => {} } },
        { provide: StockfishService, useValue: {} },
        { provide: AnalysisEngineService, useValue: {} },
        { provide: ThemeService, useValue: {} },
        { provide: PwaInstallService, useValue: { isAndroid: false, isInstalled: () => false } },
      ],
    });
    // Template + dessen Imports entfernen → nur die Constructor-/Lifecycle-Logik testen.
    TestBed.overrideComponent(AppComponent, { set: { template: '', imports: [] } });
  });

  it('tears down its root-level subscriptions on destroy', () => {
    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges(); // ngOnInit → legt die Subscriptions an

    expect(routerEvents.observed).toBe(true);
    expect(versionUpdates.observed).toBe(true);
    expect(unrecoverable.observed).toBe(true);

    fixture.destroy();

    expect(routerEvents.observed).toBe(false);
    expect(versionUpdates.observed).toBe(false);
    expect(unrecoverable.observed).toBe(false);
  });
});

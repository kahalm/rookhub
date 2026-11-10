import { ChangeDetectionStrategy, Component, Type } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { renderAfterHttpInterceptor } from '../../../core/render-after-http.interceptor';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { AdminMenuVisibilityComponent } from './admin-menu-visibility.component';
import { AdminMessagesComponent } from './admin-messages.component';
import { AdminRolesComponent } from './admin-roles.component';
import { AdminDailyPuzzleComponent } from './admin-daily-puzzle.component';
import { AdminChessableDownloadComponent } from './admin-chessable-download.component';

/*
 * UI-Sweep 10.11.2026: `/admin?tab=menu|messages|roles` blieben per Direktlink dauerhaft am Ladekreis. Die Admin-Seite
 * ist OnPush, und Angular 22 zeichnet nach einer HTTP-Antwort nicht von selbst neu — die Reiter (CheckAlways, Felder im
 * subscribe gesetzt) wurden nie mehr geprüft. Die Hosts hier sind OnPush wie die Admin-Seite; gezeichnet wird nur über
 * autoDetectChanges + whenStable (also über den Scheduler wie im Browser), kein manuelles detectChanges nach der Antwort.
 */
@Component({ selector: 'app-h-menu', standalone: true, imports: [AdminMenuVisibilityComponent],
  changeDetection: ChangeDetectionStrategy.OnPush, template: '<app-admin-menu-visibility [groups]="[]" />' })
class MenuHost {}
@Component({ selector: 'app-h-msg', standalone: true, imports: [AdminMessagesComponent],
  changeDetection: ChangeDetectionStrategy.OnPush, template: '<app-admin-messages />' })
class MessagesHost {}
@Component({ selector: 'app-h-roles', standalone: true, imports: [AdminRolesComponent],
  changeDetection: ChangeDetectionStrategy.OnPush, template: '<app-admin-roles [groups]="[]" />' })
class RolesHost {}
@Component({ selector: 'app-h-daily', standalone: true, imports: [AdminDailyPuzzleComponent],
  changeDetection: ChangeDetectionStrategy.OnPush, template: '<app-admin-daily-puzzle />' })
class DailyHost {}
@Component({ selector: 'app-h-dl', standalone: true, imports: [AdminChessableDownloadComponent],
  changeDetection: ChangeDetectionStrategy.OnPush, template: '<app-admin-chessable-download />' })
class ChessableHost {}

describe('Admin-Reiter unter der OnPush-Admin-Seite (Direktlink, UI-Sweep 10.11.2026)', () => {
  let http: HttpTestingController;

  async function open<T>(host: Type<T>): Promise<ComponentFixture<T>> {
    await TestBed.configureTestingModule({
      imports: [host],
      providers: [
        // wie im Browser: renderAfterHttp tickt nach jeder Antwort — der Lauf steigt aber nicht in die nicht markierte
        // OnPush-Elternansicht hinab, deshalb braucht der Reiter selbst ein markForCheck.
        provideHttpClient(withInterceptors([renderAfterHttpInterceptor])), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(host);
    fixture.autoDetectChanges();
    await fixture.whenStable();
    return fixture;
  }

  const spinner = (f: ComponentFixture<unknown>) => (f.nativeElement as HTMLElement).querySelector('app-loading-spinner');
  const text = (f: ComponentFixture<unknown>) => (f.nativeElement as HTMLElement).textContent ?? '';

  afterEach(() => http.verify());

  it('Menü: Ladekreis verschwindet nach der Antwort', async () => {
    const f = await open(MenuHost);
    expect(spinner(f)).withContext('vor der Antwort').not.toBeNull();
    http.expectOne('/api/admin/menu').flush([{ key: 'courses', level: 'All', groupIds: [] }]);
    http.match('/api/menu').forEach(r => r.flush([]));   // MenuService (eigene Navbar) lädt selbst
    await f.whenStable();
    expect(spinner(f)).toBeNull();
  });

  it('Nachrichten: Ladekreis verschwindet nach der Antwort', async () => {
    const f = await open(MessagesHost);
    expect(spinner(f)).withContext('vor der Antwort').not.toBeNull();
    http.expectOne('/api/admin/messages/threads').flush([]);
    await f.whenStable();
    expect(spinner(f)).toBeNull();
  });

  it('Rollen: Ladekreis verschwindet, Rollen erscheinen', async () => {
    const f = await open(RolesHost);
    expect(spinner(f)).withContext('vor der Antwort').not.toBeNull();
    http.expectOne('/api/admin/roles/permissions').flush([]);
    http.expectOne('/api/admin/roles').flush([
      { id: 7, key: 'mein-testrolle', name: 'Meine Testrolle', permissions: [], isSystem: false, memberCount: 0, groups: [] },
    ]);
    await f.whenStable();
    expect(spinner(f)).toBeNull();
    expect(text(f)).toContain('Meine Testrolle');
  });

  it('Tagespuzzle: Ladekreis verschwindet auch bei 404 (kein Puzzle für den Tag)', async () => {
    const f = await open(DailyHost);
    expect(spinner(f)).withContext('vor der Antwort').not.toBeNull();
    http.expectOne(r => r.url.startsWith('/api/book-puzzles/daily/')).flush(null, { status: 404, statusText: 'Not Found' });
    await f.whenStable();
    expect(spinner(f)).toBeNull();
  });

  it('Kurse holen: Leerzustand erscheint nach der Antwort', async () => {
    const f = await open(ChessableHost);
    expect(text(f)).not.toContain('admin.courseDl.noUsers');
    http.expectOne('/api/chessable/admin/credentialed-users').flush([]);
    await f.whenStable();
    expect(text(f)).toContain('admin.courseDl.noUsers');
  });
});

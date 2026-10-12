import { of, throwError } from 'rxjs';
import { NotificationsComponent } from './notifications.component';
import { AppNotification } from '../../core/in-app-notification.service';

/** Direkt instanziiert (ohne TestBed/Template) — testet die Komponenten-Logik. */
const translate: any = { instant: (k: string) => k };

function notif(id: number, seen = false, link: string | null = null, type = 'friend_request_received'): AppNotification {
  return { id, type, data: null, link, createdAt: '2026-06-17T00:00:00Z', seen };
}

function makeService(overrides: any = {}): any {
  return {
    history: jasmine.createSpy('history').and.returnValue(of({ items: [], total: 0 })),
    markSeen: jasmine.createSpy('markSeen').and.returnValue(of({})),
    ...overrides,
  };
}

describe('NotificationsComponent', () => {
  it('loadMore accumulates pages, tracks the total and advances the page counter', () => {
    const svc = makeService();
    svc.history.and.returnValues(
      of({ items: [notif(1), notif(2)], total: 3 }),
      of({ items: [notif(3)], total: 3 }),
    );
    const c = new NotificationsComponent(svc, translate, { navigateByUrl: jasmine.createSpy() } as any, { supported: false, permissionDenied: false } as any, { isAdmin: false, has: () => false } as any, {} as any);

    c.loadMore();
    expect(c.items.map(n => n.id)).toEqual([1, 2]);
    expect(c.total).toBe(3);

    c.loadMore();
    expect(c.items.map(n => n.id)).toEqual([1, 2, 3]);
    expect(svc.history.calls.allArgs()).toEqual([[1, 30], [2, 30]]);   // page hochgezählt, pageSize 30
    expect(c.loading).toBeFalse();
  });

  it('open marks an unseen notification as seen and navigates when it has a link', () => {
    const svc = makeService();
    const router: any = { navigateByUrl: jasmine.createSpy('nav') };
    const c = new NotificationsComponent(svc, translate, router, { supported: false, permissionDenied: false } as any, { isAdmin: false, has: () => false } as any, {} as any);

    const n = notif(7, false, '/friends');
    c.open(n);

    expect(svc.markSeen).toHaveBeenCalledOnceWith(7);
    expect(n.seen).toBeTrue();
    expect(router.navigateByUrl).toHaveBeenCalledOnceWith('/friends');
  });

  it('open neither re-marks an already-seen notification nor navigates without a link', () => {
    const svc = makeService();
    const router: any = { navigateByUrl: jasmine.createSpy('nav') };
    const c = new NotificationsComponent(svc, translate, router, { supported: false, permissionDenied: false } as any, { isAdmin: false, has: () => false } as any, {} as any);

    c.open(notif(8, true, null));

    expect(svc.markSeen).not.toHaveBeenCalled();
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it('loadMore clears the loading flag on error', () => {
    const svc = makeService({ history: jasmine.createSpy('history').and.returnValue(throwError(() => new Error('x'))) });
    const c = new NotificationsComponent(svc, translate, { navigateByUrl: jasmine.createSpy() } as any, { supported: false, permissionDenied: false } as any, { isAdmin: false, has: () => false } as any, {} as any);

    c.loadMore();

    expect(c.loading).toBeFalse();
    expect(c.items).toEqual([]);
  });

  // F5-017: ein Ladefehler darf nicht wie „keine Benachrichtigungen" aussehen.
  it('a failing first page sets the error state, the retry clears it', () => {
    const svc = makeService({ history: jasmine.createSpy('history').and.returnValues(
      throwError(() => ({ status: 500 })), of({ items: [notif(1)], total: 1 })) });
    const c = new NotificationsComponent(svc, translate, { navigateByUrl: jasmine.createSpy() } as any, { supported: false, permissionDenied: false } as any, { isAdmin: false, has: () => false } as any, {} as any);

    c.loadMore();
    expect(c.loadError).toBeTrue();

    c.loadMore();
    expect(c.loadError).toBeFalse();
    expect(c.items.map(n => n.id)).toEqual([1]);
  });

  it('a failing "load more" keeps the list and reports the failure', () => {
    const snackbar = { warn: jasmine.createSpy('warn') };
    const svc = makeService({ history: jasmine.createSpy('history').and.returnValues(
      of({ items: [notif(1)], total: 2 }), throwError(() => ({ status: 500 }))) });
    const c = new NotificationsComponent(svc, translate, { navigateByUrl: jasmine.createSpy() } as any, { supported: false, permissionDenied: false } as any, { isAdmin: false, has: () => false } as any, snackbar as any);

    c.loadMore();
    c.loadMore();

    expect(c.items.map(n => n.id)).toEqual([1]);
    expect(snackbar.warn).toHaveBeenCalledOnceWith('common.loadFailed');
  });

  describe('category filter', () => {
    beforeEach(() => localStorage.removeItem('rookhub_notifications_hidden_categories'));
    afterEach(() => localStorage.removeItem('rookhub_notifications_hidden_categories'));

    function withItems(items: AppNotification[]): NotificationsComponent {
      const svc = makeService();
      svc.history.and.returnValue(of({ items, total: items.length }));
      const c = new NotificationsComponent(svc, translate, { navigateByUrl: jasmine.createSpy() } as any, { supported: false, permissionDenied: false } as any, { isAdmin: false, has: () => false } as any, {} as any);
      c.loadMore();
      return c;
    }

    it('lists only categories present in the loaded items, in canonical order', () => {
      const c = withItems([
        notif(1, false, null, 'friend_request_received'),           // friends
        notif(2, false, null, 'challenge_received'),                 // puzzles
        notif(3, false, null, 'chessable_import_completed'),         // courses
        notif(4, false, null, 'admin_message_received'),             // messages
      ]);
      // Canonical order: courses, friends, puzzles, messages, …
      expect(c.availableCategories).toEqual(['courses', 'friends', 'puzzles', 'messages']);
      expect(c.counts.courses).toBe(1);
      expect(c.counts.friends).toBe(1);
      expect(c.counts.puzzles).toBe(1);
      expect(c.counts.messages).toBe(1);
      expect(c.counts.other).toBe(0);
    });

    it('hides items whose category is toggled off; showAll restores everything', () => {
      const c = withItems([
        notif(1, false, null, 'friend_request_received'),
        notif(2, false, null, 'challenge_received'),
        notif(3, false, null, 'chessable_import_completed'),
      ]);
      expect(c.visibleItems.map(n => n.id)).toEqual([1, 2, 3]);

      c.toggleCategory('friends');
      expect(c.isHidden('friends')).toBeTrue();
      expect(c.visibleItems.map(n => n.id)).toEqual([2, 3]);

      c.toggleCategory('puzzles');
      expect(c.visibleItems.map(n => n.id)).toEqual([3]);

      c.toggleCategory('friends');   // Toggle wieder an
      expect(c.visibleItems.map(n => n.id)).toEqual([1, 3]);

      c.showAll();
      expect(c.hidden.size).toBe(0);
      expect(c.visibleItems.map(n => n.id)).toEqual([1, 2, 3]);
    });

    it('persists hidden categories in localStorage and restores them on the next instance', () => {
      const c = withItems([notif(1, false, null, 'friend_request_received')]);
      c.toggleCategory('friends');
      expect(localStorage.getItem('rookhub_notifications_hidden_categories')).toContain('friends');

      // Frischer Component-Instanz-Aufbau → liest Storage im Constructor
      const svc = makeService();
      svc.history.and.returnValue(of({ items: [], total: 0 }));
      const c2 = new NotificationsComponent(svc, translate, { navigateByUrl: jasmine.createSpy() } as any, { supported: false, permissionDenied: false } as any, { isAdmin: false, has: () => false } as any, {} as any);
      expect(c2.isHidden('friends')).toBeTrue();
    });
  });

  // Codereview F5-005: der Push-Bereich „admin" (neue Registrierungen) folgt dem Recht users.manage, nicht dem Admin-Flag.
  it('offers the admin push area to users.manage holders, not by the admin flag', () => {
    const make = (auth: any) => new NotificationsComponent(makeService(), translate, { navigateByUrl: jasmine.createSpy() } as any,
      { supported: false, permissionDenied: false } as any, auth, {} as any);
    expect(make({ isAdmin: false, has: (p: string) => p === 'users.manage' }).pushCategories).toContain('admin');
    expect(make({ isAdmin: false, has: () => false }).pushCategories).not.toContain('admin');
  });

  // W5 F5-018: der geklickte Schalter stellt sich selbst um; nach einem Fehler muss er zurueck auf „aus".
  describe('Push-Schalter', () => {
    function withPush(push: any, snackbar: any = { warn: jasmine.createSpy('warn') }) {
      const c = new NotificationsComponent(makeService(), translate, { navigateByUrl: jasmine.createSpy() } as any,
        { supported: true, permissionDenied: false, ...push } as any, { isAdmin: false, has: () => false } as any, snackbar as any);
      c.pushPublicKey = 'vapid';
      return { c, snackbar };
    }

    it('setzt den Schalter zurueck, wenn die Berechtigung/Subscription scheitert', async () => {
      const { c, snackbar } = withPush({
        ensureSubscribed: jasmine.createSpy('ensure').and.rejectWith(new Error('denied')),
        setPreferences: jasmine.createSpy('prefs'),
      });
      const toggle = { checked: true };
      await c.togglePush('friends', true, toggle);
      expect(toggle.checked).toBeFalse();
      expect(c.isPushOn('friends')).toBeFalse();
      expect(snackbar.warn).toHaveBeenCalled();
    });

    it('setzt den Schalter zurueck, wenn das Speichern der Bereiche scheitert', async () => {
      const { c } = withPush({
        ensureSubscribed: jasmine.createSpy('ensure').and.resolveTo(),
        setPreferences: jasmine.createSpy('prefs').and.returnValue(throwError(() => ({ status: 500 }))),
      });
      const toggle = { checked: true };
      await c.togglePush('friends', true, toggle);
      expect(toggle.checked).toBeFalse();
    });

    it('folgt der effektiven Server-Antwort (Bereich verworfen) und bleibt bei Erfolg an', async () => {
      const { c } = withPush({
        ensureSubscribed: jasmine.createSpy('ensure').and.resolveTo(),
        setPreferences: jasmine.createSpy('prefs').and.callFake((cats: string[]) =>
          of({ categories: cats.filter(x => x !== 'admin') })),
        removeSubscription: jasmine.createSpy('remove').and.resolveTo(),
      });
      const friends = { checked: true };
      await c.togglePush('friends', true, friends);
      expect(friends.checked).toBeTrue();
      const admin = { checked: true };
      await c.togglePush('admin', true, admin);
      expect(admin.checked).toBeFalse();
      expect(c.isPushOn('friends')).toBeTrue();
    });
  });
});

/** UI-Sweep 2026-10-11 (d2-rk-notif-push-card): Push-Bereich steht unter der Liste; ohne Push nur eine leise Zeile. */
describe('NotificationsComponent Push-Bereich unter der Liste', () => {
  async function render(push: any) {
    const { TestBed } = await import('@angular/core/testing');
    const { provideTranslateService } = await import('@ngx-translate/core');
    const { provideNoopAnimations } = await import('@angular/platform-browser/animations');
    const { InAppNotificationService } = await import('../../core/in-app-notification.service');
    const { PushService } = await import('../../core/push.service');
    const { AuthService } = await import('../../core/auth.service');
    const { SnackbarService } = await import('../../core/snackbar.service');
    const { Router } = await import('@angular/router');
    TestBed.configureTestingModule({
      imports: [NotificationsComponent],
      providers: [
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: InAppNotificationService, useValue: makeService({
          history: () => of({ items: [notif(1)], total: 1 }) }) },
        { provide: PushService, useValue: push },
        { provide: AuthService, useValue: { isAdmin: false, has: () => false } },
        { provide: SnackbarService, useValue: {} },
        { provide: Router, useValue: { navigateByUrl: () => {} } },
      ],
    });
    const fixture = TestBed.createComponent(NotificationsComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('ohne Push: keine Karte, nur eine Zeile nach der Liste', async () => {
    const el = await render({ supported: false, permissionDenied: false });
    expect(el.querySelector('.push-card')).toBeNull();
    const note = el.querySelector('.push-note');
    expect(note).not.toBeNull();
    const list = el.querySelector('.list')!;
    expect(list.compareDocumentPosition(note!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('mit Push: Karte mit Schaltern steht nach der Liste', async () => {
    const el = await render({ supported: true, permissionDenied: false,
      getConfig: () => of({ publicKey: 'vapid', enabledCategories: [] }) });
    const card = el.querySelector('.push-card');
    expect(card).not.toBeNull();
    expect(el.querySelector('.push-note')).toBeNull();
    expect(el.querySelector('.list')!.compareDocumentPosition(card!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });
});

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
    const c = new NotificationsComponent(svc, translate, { navigateByUrl: jasmine.createSpy() } as any);

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
    const c = new NotificationsComponent(svc, translate, router);

    const n = notif(7, false, '/friends');
    c.open(n);

    expect(svc.markSeen).toHaveBeenCalledOnceWith(7);
    expect(n.seen).toBeTrue();
    expect(router.navigateByUrl).toHaveBeenCalledOnceWith('/friends');
  });

  it('open neither re-marks an already-seen notification nor navigates without a link', () => {
    const svc = makeService();
    const router: any = { navigateByUrl: jasmine.createSpy('nav') };
    const c = new NotificationsComponent(svc, translate, router);

    c.open(notif(8, true, null));

    expect(svc.markSeen).not.toHaveBeenCalled();
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it('loadMore clears the loading flag on error', () => {
    const svc = makeService({ history: jasmine.createSpy('history').and.returnValue(throwError(() => new Error('x'))) });
    const c = new NotificationsComponent(svc, translate, { navigateByUrl: jasmine.createSpy() } as any);

    c.loadMore();

    expect(c.loading).toBeFalse();
    expect(c.items).toEqual([]);
  });

  describe('category filter', () => {
    beforeEach(() => localStorage.removeItem('rookhub_notifications_hidden_categories'));
    afterEach(() => localStorage.removeItem('rookhub_notifications_hidden_categories'));

    function withItems(items: AppNotification[]): NotificationsComponent {
      const svc = makeService();
      svc.history.and.returnValue(of({ items, total: items.length }));
      const c = new NotificationsComponent(svc, translate, { navigateByUrl: jasmine.createSpy() } as any);
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
      const c2 = new NotificationsComponent(svc, translate, { navigateByUrl: jasmine.createSpy() } as any);
      expect(c2.isHidden('friends')).toBeTrue();
    });
  });
});

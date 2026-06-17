import { of, throwError } from 'rxjs';
import { NotificationsComponent } from './notifications.component';
import { AppNotification } from '../../core/in-app-notification.service';

/** Direkt instanziiert (ohne TestBed/Template) — testet die Komponenten-Logik. */
const translate: any = { instant: (k: string) => k };

function notif(id: number, seen = false, link: string | null = null): AppNotification {
  return { id, type: 'friend_request_received', data: null, link, createdAt: '2026-06-17T00:00:00Z', seen };
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
});

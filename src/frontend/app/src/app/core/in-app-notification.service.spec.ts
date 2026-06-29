import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { InAppNotificationService } from './in-app-notification.service';

describe('InAppNotificationService', () => {
  let service: InAppNotificationService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [InAppNotificationService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(InAppNotificationService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  function setCount(n: number): void {
    service.refreshCount();
    httpMock.expectOne('/api/notifications/count').flush({ count: n });
  }

  it('refreshCount updates the unseen badge count', () => {
    let count = -1;
    service.unseenCount$.subscribe(c => count = c);
    setCount(3);
    expect(count).toBe(3);
  });

  it('refreshCount keeps the previous count on error (non-critical)', () => {
    let count = -1;
    service.unseenCount$.subscribe(c => count = c);
    setCount(2);
    service.refreshCount();
    httpMock.expectOne('/api/notifications/count').flush('boom', { status: 500, statusText: 'Server Error' });
    expect(count).toBe(2);
  });

  it('markAllSeen clears the badge to 0', () => {
    let count = -1;
    service.unseenCount$.subscribe(c => count = c);
    setCount(5);

    service.markAllSeen().subscribe();
    const req = httpMock.expectOne('/api/notifications/seen');
    expect(req.request.method).toBe('POST');
    req.flush({});

    expect(count).toBe(0);
  });

  it('markSeen decrements the badge by one (clamped at 0)', () => {
    let count = -1;
    service.unseenCount$.subscribe(c => count = c);
    setCount(2);

    service.markSeen(11).subscribe();
    httpMock.expectOne('/api/notifications/11/seen').flush({});
    expect(count).toBe(1);

    service.markSeen(12).subscribe();
    httpMock.expectOne('/api/notifications/12/seen').flush({});
    expect(count).toBe(0);

    service.markSeen(13).subscribe();      // schon 0 → bleibt 0, nicht negativ
    httpMock.expectOne('/api/notifications/13/seen').flush({});
    expect(count).toBe(0);
  });

  it('reset sets the count back to 0 locally (logout) without a request', () => {
    let count = -1;
    service.unseenCount$.subscribe(c => count = c);
    setCount(4);
    service.reset();
    expect(count).toBe(0);
    httpMock.verify();   // reset darf KEINEN HTTP-Call ausgelöst haben
  });

  it('ignores a stale higher refresh right after an optimistic markSeen (no badge flicker)', () => {
    let count = -1;
    service.unseenCount$.subscribe(c => count = c);
    setCount(3);

    // Optimistische Verkleinerung → 2.
    service.markSeen(7).subscribe();
    httpMock.expectOne('/api/notifications/7/seen').flush({});
    expect(count).toBe(2);

    // Ein gleichzeitig gestarteter Refresh liefert noch den alten Wert 3 → darf NICHT zurückspringen.
    service.refreshCount();
    httpMock.expectOne('/api/notifications/count').flush({ count: 3 });
    expect(count).toBe(2);

    // Eine Verkleinerung durch den Server greift dagegen sofort.
    service.refreshCount();
    httpMock.expectOne('/api/notifications/count').flush({ count: 1 });
    expect(count).toBe(1);
  });

  it('emits arrived$ only when the unseen count rises (a new notification came in)', () => {
    let arrivals = 0;
    service.arrived$.subscribe(() => arrivals++);

    setCount(0);                 // 0 → 0: keine Steigerung
    expect(arrivals).toBe(0);
    setCount(2);                 // 0 → 2: neue Benachrichtigung(en) → 1×
    expect(arrivals).toBe(1);
    setCount(2);                 // unverändert → kein Feuern
    expect(arrivals).toBe(1);
    setCount(5);                 // 2 → 5: erneut neue → 2×
    expect(arrivals).toBe(2);
    setCount(1);                 // gesunken (gelesen) → kein Feuern
    expect(arrivals).toBe(2);
  });

  it('list requests take + unseenOnly as query params', () => {
    service.list(10, true).subscribe();
    const req = httpMock.expectOne('/api/notifications?take=10&unseenOnly=true');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('history requests page + pageSize as query params', () => {
    service.history(2, 15).subscribe();
    const req = httpMock.expectOne('/api/notifications/history?page=2&pageSize=15');
    expect(req.request.method).toBe('GET');
    req.flush({ items: [], total: 0 });
  });
});

import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { MessageService } from './message.service';

describe('MessageService', () => {
  let svc: MessageService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule] });
    svc = TestBed.inject(MessageService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lädt den eigenen Thread des Users', () => {
    let result: unknown;
    svc.getThread().subscribe(r => result = r);
    const req = http.expectOne('/api/messages');
    expect(req.request.method).toBe('GET');
    req.flush([{ id: 1, fromAdmin: true, body: 'hi', createdAt: '2026-06-15T10:00:00Z', readByRecipient: false }]);
    expect((result as unknown[]).length).toBe(1);
  });

  it('sendet eine User-Nachricht an /api/messages/reply', () => {
    svc.send('hallo').subscribe();
    const req = http.expectOne('/api/messages/reply');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ body: 'hallo' });
    req.flush({ id: 2, fromAdmin: false, body: 'hallo', createdAt: '', readByRecipient: false });
  });

  it('Admin: claim + release posten auf die Thread-Endpoints', () => {
    svc.claimThread(7).subscribe();
    const c = http.expectOne('/api/admin/messages/threads/7/claim');
    expect(c.request.method).toBe('POST');
    c.flush({});
    svc.releaseThread(7).subscribe();
    const r = http.expectOne('/api/admin/messages/threads/7/release');
    expect(r.request.method).toBe('POST');
    r.flush({});
  });

  it('markUserSeen leert den User-Ungelesen-Zähler', () => {
    let count = -1;
    svc.userUnread$.subscribe(c => count = c);
    svc.markUserSeen().subscribe();
    http.expectOne('/api/messages/seen').flush({});
    expect(count).toBe(0);
  });

  it('refreshUserUnread aktualisiert den Zähler aus der API', () => {
    let count = -1;
    svc.userUnread$.subscribe(c => count = c);
    svc.refreshUserUnread();
    http.expectOne('/api/messages/unread-count').flush({ count: 3 });
    expect(count).toBe(3);
  });

  it('Admin: sendToUser postet an den Thread-Endpoint', () => {
    svc.sendToUser(42, 'moin').subscribe();
    const req = http.expectOne('/api/admin/messages/threads/42');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ body: 'moin' });
    req.flush({ id: 5, fromAdmin: true, body: 'moin', createdAt: '', readByRecipient: false });
  });

  it('Admin: getThreads + refreshAdminUnread', () => {
    svc.getThreads().subscribe();
    http.expectOne('/api/admin/messages/threads').flush([]);

    let count = -1;
    svc.adminUnread$.subscribe(c => count = c);
    svc.refreshAdminUnread();
    http.expectOne('/api/admin/messages/unread-count').flush({ count: 2 });
    expect(count).toBe(2);
  });

  it('reset setzt beide Zähler zurück', () => {
    let u = -1, a = -1;
    svc.userUnread$.subscribe(c => u = c);
    svc.adminUnread$.subscribe(c => a = c);
    svc.refreshUserUnread();
    http.expectOne('/api/messages/unread-count').flush({ count: 4 });
    svc.reset();
    expect(u).toBe(0);
    expect(a).toBe(0);
  });
});

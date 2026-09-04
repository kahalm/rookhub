import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { RevengeService } from './revenge.service';

describe('RevengeService', () => {
  let service: RevengeService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [RevengeService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(RevengeService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('recordResult posts targetUserId + puzzleId + solved', () => {
    service.recordResult(5, 42, true).subscribe();
    const req = httpMock.expectOne('/api/revenge/result');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ targetUserId: 5, puzzleId: 42, solved: true });
    req.flush({ created: true });
  });

  it('getNotifications liest die eigene Liste', () => {
    service.getNotifications().subscribe();
    const req = httpMock.expectOne('/api/revenge/notifications');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('markSeen quittiert alle Benachrichtigungen', () => {
    // Der frühere Badge-Zähler dieses Services wurde von NIEMANDEM abonniert (die Glocke bedient
    // Benachrichtigungen und Admin-Nachrichten) — gepflegter, nie gelesener Zustand, der beim
    // Nutzerwechsel zudem nicht zurückgesetzt wurde. Deshalb entfernt; hier bleibt der Aufruf.
    service.markSeen().subscribe();
    const req = httpMock.expectOne('/api/revenge/notifications/seen');
    expect(req.request.method).toBe('POST');
    req.flush({});
  });
});

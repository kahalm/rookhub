import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ChallengeService, IncomingChallenge } from './challenge.service';

describe('ChallengeService', () => {
  let service: ChallengeService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [ChallengeService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(ChallengeService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('getIncoming() liest die eingehenden Challenges', () => {
    // Der frühere Badge-Zähler dieses Services wurde nirgends abonniert (die Navbar-Glocke bedient
    // Benachrichtigungen und Admin-Nachrichten) und beim Nutzerwechsel nicht zurückgesetzt —
    // deshalb entfernt. Geprüft wird jetzt der Abruf selbst.
    let received: IncomingChallenge[] = [];
    service.getIncoming().subscribe(list => received = list);
    const incoming: IncomingChallenge[] = [
      { id: 1, fromUserId: 2, fromUsername: 'a', fromDisplayName: null, puzzleId: 5, source: 'Standard', rating: 1500, themes: null, title: null, createdAt: '' },
      { id: 2, fromUserId: 3, fromUsername: 'b', fromDisplayName: null, puzzleId: 6, source: 'Book', rating: 1600, themes: null, title: 'Kap. 1', createdAt: '' },
    ];
    const req = httpMock.expectOne('/api/challenges/incoming');
    expect(req.request.method).toBe('GET');
    req.flush(incoming);

    expect(received.length).toBe(2);
  });

  it('sendMany() posts toUserIds + puzzleId + source', () => {
    service.sendMany([7, 8], 42, 'book').subscribe();
    const req = httpMock.expectOne('/api/challenges');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ toUserIds: [7, 8], puzzleId: 42, source: 'book' });
    req.flush({ sent: 2, skipped: [] });
  });

  it('sendMany() defaults source to standard', () => {
    service.sendMany([7], 42).subscribe();
    const req = httpMock.expectOne('/api/challenges');
    expect(req.request.body).toEqual({ toUserIds: [7], puzzleId: 42, source: 'standard' });
    req.flush({ sent: 1, skipped: [] });
  });
});

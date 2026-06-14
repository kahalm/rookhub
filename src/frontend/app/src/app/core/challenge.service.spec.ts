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

  it('starts with a badge count of 0', () => {
    let count = -1;
    service.incomingCount$.subscribe(c => count = c);
    expect(count).toBe(0);
  });

  it('updates the badge count from the incoming list length', () => {
    let count = 0;
    service.incomingCount$.subscribe(c => count = c);

    service.getIncoming().subscribe();
    const incoming: IncomingChallenge[] = [
      { id: 1, fromUserId: 2, fromUsername: 'a', fromDisplayName: null, puzzleId: 5, rating: 1500, themes: null, createdAt: '' },
      { id: 2, fromUserId: 3, fromUsername: 'b', fromDisplayName: null, puzzleId: 6, rating: 1600, themes: null, createdAt: '' },
    ];
    httpMock.expectOne('/api/challenges/incoming').flush(incoming);

    expect(count).toBe(2);
  });

  it('refreshCount() reads the dedicated count endpoint', () => {
    let count = 0;
    service.incomingCount$.subscribe(c => count = c);

    service.refreshCount();
    httpMock.expectOne('/api/challenges/incoming/count').flush({ count: 4 });

    expect(count).toBe(4);
  });

  it('send() posts toUserId + puzzleId', () => {
    service.send(7, 42).subscribe();
    const req = httpMock.expectOne('/api/challenges');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ toUserId: 7, puzzleId: 42 });
    req.flush({ id: 99 });
  });
});

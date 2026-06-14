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

  it('refreshCount updates the unseen badge count', () => {
    let count = 0;
    service.unseenCount$.subscribe(c => count = c);

    service.refreshCount();
    httpMock.expectOne('/api/revenge/notifications/count').flush({ count: 3 });

    expect(count).toBe(3);
  });

  it('markSeen resets the badge count to 0', () => {
    let count = -1;
    service.unseenCount$.subscribe(c => count = c);

    service.refreshCount();
    httpMock.expectOne('/api/revenge/notifications/count').flush({ count: 3 });
    expect(count).toBe(3);

    service.markSeen().subscribe();
    httpMock.expectOne('/api/revenge/notifications/seen').flush({});
    expect(count).toBe(0);
  });
});

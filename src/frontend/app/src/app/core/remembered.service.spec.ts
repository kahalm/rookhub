import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { RememberedService } from './remembered.service';

describe('RememberedService', () => {
  let svc: RememberedService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    svc = TestBed.inject(RememberedService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('list hits the extension endpoint with take', () => {
    svc.list(20).subscribe();
    const req = http.expectOne('/api/extension/remembered-lines?take=20');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('remove DELETEs by id', () => {
    svc.remove(13).subscribe();
    const req = http.expectOne('/api/extension/remembered-lines/13');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });
});

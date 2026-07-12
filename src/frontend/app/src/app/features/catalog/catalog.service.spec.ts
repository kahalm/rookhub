import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { CatalogService } from './catalog.service';

describe('CatalogService', () => {
  let svc: CatalogService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    svc = TestBed.inject(CatalogService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('access / list GET the right endpoints', () => {
    svc.access().subscribe(); http.expectOne('/api/catalog/access').flush({ hasAccess: true });
    svc.list().subscribe(); http.expectOne('/api/catalog').flush([]);
  });

  it('request POSTs itemType+itemId', () => {
    svc.request('course', 3).subscribe();
    const req = http.expectOne('/api/catalog/request');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ itemType: 'course', itemId: 3 });
    req.flush({ status: 'pending' });
  });

  it('grants get/set + requests + approve/decline hit the owner endpoints', () => {
    svc.getGrants().subscribe(); http.expectOne('/api/catalog/grants').flush({ userIds: [], groupIds: [] });
    svc.setGrants({ userIds: [1], groupIds: [2] }).subscribe();
    const put = http.expectOne('/api/catalog/grants');
    expect(put.request.method).toBe('PUT');
    put.flush({ userIds: [1], groupIds: [2] });
    svc.getRequests().subscribe(); http.expectOne('/api/catalog/requests').flush([]);
    svc.approve(5).subscribe();
    const ap = http.expectOne('/api/catalog/requests/5/approve');
    expect(ap.request.method).toBe('POST'); ap.flush(null);
    svc.decline(6).subscribe();
    http.expectOne('/api/catalog/requests/6/decline').flush(null);
  });
});

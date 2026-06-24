import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AdminService } from './admin.service';

describe('AdminService', () => {
  let service: AdminService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [AdminService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AdminService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('getUsers passes search/page/pageSize as params', () => {
    service.getUsers('bob', 2, 50).subscribe();
    const req = httpMock.expectOne(r => r.url === '/api/admin/users');
    expect(req.request.params.get('search')).toBe('bob');
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.get('pageSize')).toBe('50');
    req.flush({ items: [], total: 0 });
  });

  it('deleteUser DELETEs the user route', () => {
    service.deleteUser(7).subscribe();
    const req = httpMock.expectOne('/api/admin/users/7');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('toggleAdmin + impersonate POST to their routes', () => {
    service.toggleAdmin(3).subscribe();
    const toggle = httpMock.expectOne('/api/admin/users/3/toggle-admin');
    expect(toggle.request.method).toBe('POST');
    toggle.flush({});

    service.impersonate(3).subscribe();
    httpMock.expectOne('/api/admin/users/3/impersonate').flush({ token: 't' });
  });

  it('importBooks sends a multipart form with the files', () => {
    const f = new File(['1. e4 *'], 'b.pgn', { type: 'application/x-chess-pgn' });
    service.importBooks([f]).subscribe();
    const req = httpMock.expectOne('/api/admin/books/import');
    expect(req.request.method).toBe('POST');
    expect(req.request.body instanceof FormData).toBeTrue();
    req.flush({ totalImported: 1, totalSkipped: 0, totalInvalid: 0 });
  });

  it('updateBookGroups PUTs the group id list', () => {
    service.updateBookGroups(9, [1, 2]).subscribe();
    const req = httpMock.expectOne('/api/admin/books/9/groups');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ groupIds: [1, 2] });
    req.flush([1, 2]);
  });

  it('addGroupMember / removeGroupMember hit the member sub-routes', () => {
    service.addGroupMember(4, 11).subscribe();
    const add = httpMock.expectOne('/api/admin/groups/4/members/11');
    expect(add.request.method).toBe('POST');
    add.flush(null);

    service.removeGroupMember(4, 11).subscribe();
    const remove = httpMock.expectOne('/api/admin/groups/4/members/11');
    expect(remove.request.method).toBe('DELETE');
    remove.flush(null);
  });

  it('saveMenuConfig PUTs the item list', () => {
    const items = [{ key: 'courses', level: 'Registered', groupIds: [] } as any];
    service.saveMenuConfig(items).subscribe();
    const req = httpMock.expectOne('/api/admin/menu');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(items);
    req.flush(items);
  });
});

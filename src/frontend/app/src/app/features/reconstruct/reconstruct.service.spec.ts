import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { PartKind, ReconstructService } from './reconstruct.service';

/**
 * Der Dienst ist dünn — geprüft wird genau das, was schiefgehen kann: Adresse, Methode und dass
 * die Fortsetzungs-Angabe (`continuesPrevious`) wirklich mitgeht. Ohne sie prüft der Server die
 * Zugfolge nicht gegen die Stellung davor, und das merkt man erst an der falschen Anzeige.
 */
describe('ReconstructService', () => {
  let service: ReconstructService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(ReconstructService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('listet und legt an', () => {
    service.list().subscribe();
    http.expectOne({ url: '/api/reconstructions', method: 'GET' }).flush([]);

    service.create({ title: 'Runde 3' }).subscribe();
    const created = http.expectOne({ url: '/api/reconstructions', method: 'POST' });
    expect(created.request.body).toEqual({ title: 'Runde 3' });
    created.flush({});
  });

  it('schickt ein Teil samt Fortsetzungs-Angabe', () => {
    service.addPart(7, { kind: PartKind.Moves, moves: 'e4 e5', continuesPrevious: true }).subscribe();
    const req = http.expectOne({ url: '/api/reconstructions/7/parts', method: 'POST' });
    expect(req.request.body).toEqual({ kind: PartKind.Moves, moves: 'e4 e5', continuesPrevious: true });
    req.flush({});
  });

  it('setzt die Reihenfolge über die Id-Liste', () => {
    service.reorder(7, [3, 1, 2]).subscribe();
    const req = http.expectOne({ url: '/api/reconstructions/7/parts/order', method: 'PUT' });
    expect(req.request.body).toEqual({ partIds: [3, 1, 2] });
    req.flush({});
  });

  it('löscht ein einzelnes Teil, nicht die Rekonstruktion', () => {
    service.removePart(7, 42).subscribe();
    http.expectOne({ url: '/api/reconstructions/7/parts/42', method: 'DELETE' }).flush({});
  });
});

import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { CalcEditionsService } from './calc-editions.service';

/** Adressen und Methoden sind der Vertrag mit CalcSeriesController (api/calc-editions) — jede Abweichung wäre ein 404/405 im Betrieb. */
describe('CalcEditionsService', () => {
  let svc: CalcEditionsService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    svc = TestBed.inject(CalcEditionsService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  function expectCall(method: string, url: string, body: unknown = null) {
    const req = http.expectOne(r => r.url === url);
    expect(req.request.method).toBe(method);
    expect(req.request.body).toEqual(body);
    req.flush(null);
  }

  it('Ausgaben: Betrachter- und Verwaltungsliste, Anlegen/Ändern und Entfernen', () => {
    svc.visible(12).subscribe();
    expectCall('GET', '/api/calc-editions/12');

    svc.manage(12).subscribe();
    expectCall('GET', '/api/calc-editions/12/manage');

    const dto = { chapter: 'KW 7', videoUrl: null, publishAt: '2026-09-07T16:30:00.000Z', testerPreviewAt: null };
    svc.upsert(12, dto).subscribe();
    expectCall('PUT', '/api/calc-editions/12', dto);

    svc.remove(12, 4).subscribe();
    expectCall('DELETE', '/api/calc-editions/12/4');
  });

  it('Verteiler: Mitglieder lesen, hinzufügen/ändern, entfernen und die „Gesehen"-Übersicht', () => {
    svc.members(12).subscribe();
    expectCall('GET', '/api/calc-editions/12/members');

    svc.upsertMember(12, { username: 'anna', isTester: true }).subscribe();
    expectCall('PUT', '/api/calc-editions/12/members', { username: 'anna', isTester: true });

    svc.removeMember(12, 7).subscribe();
    expectCall('DELETE', '/api/calc-editions/12/members/7');

    svc.views(12).subscribe();
    expectCall('GET', '/api/calc-editions/12/views');
  });
});

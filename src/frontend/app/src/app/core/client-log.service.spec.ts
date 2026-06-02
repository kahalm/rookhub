import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { ClientLogService } from './client-log.service';

describe('ClientLogService', () => {
  let svc: ClientLogService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule] });
    svc = TestBed.inject(ClientLogService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('posts an event to /api/client-log', () => {
    svc.report('engine_analysis_crash', 'boom');
    const req = http.expectOne('/api/client-log');
    expect(req.request.method).toBe('POST');
    expect(req.request.body.kind).toBe('engine_analysis_crash');
    expect(req.request.body.detail).toBe('boom');
    req.flush(null);
  });

  it('throttles repeated events of the same kind', () => {
    svc.report('engine_analysis_crash');
    http.expectOne('/api/client-log').flush(null);
    svc.report('engine_analysis_crash');   // sofort wieder → gedrosselt, kein zweiter Request
    http.expectNone('/api/client-log');
  });

  it('does not throttle distinct kinds', () => {
    svc.report('a'); http.expectOne('/api/client-log').flush(null);
    svc.report('b'); http.expectOne('/api/client-log').flush(null);
  });

  it('ignores an empty kind', () => {
    svc.report('');
    http.expectNone('/api/client-log');
  });
});

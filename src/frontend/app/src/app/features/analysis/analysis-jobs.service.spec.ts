import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { AnalysisJobsService } from './analysis-jobs.service';

describe('AnalysisJobsService', () => {
  let svc: AnalysisJobsService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    svc = TestBed.inject(AnalysisJobsService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('talks to /api/analysis-jobs', () => {
    svc.list().subscribe();
    expect(http.expectOne('/api/analysis-jobs').request.method).toBe('GET');
    svc.create({ fen: 'x', targetDepth: 30, multiPv: 3 }).subscribe();
    const post = http.expectOne('/api/analysis-jobs');
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toEqual({ fen: 'x', targetDepth: 30, multiPv: 3 });
    svc.update(7, { targetDepth: 50 }).subscribe();
    expect(http.expectOne('/api/analysis-jobs/7').request.method).toBe('PUT');
    svc.delete(7).subscribe();
    expect(http.expectOne('/api/analysis-jobs/7').request.method).toBe('DELETE');
  });
});

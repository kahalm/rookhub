import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TrainingGoalService } from './training-goals.service';

describe('TrainingGoalService', () => {
  let svc: TrainingGoalService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    svc = TestBed.inject(TrainingGoalService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('gets the effective goal', () => {
    svc.getGoal().subscribe(g => expect(g.source).toBe('group'));
    const req = http.expectOne('/api/training-goals');
    expect(req.request.method).toBe('GET');
    req.flush({ puzzleMinutes: 15, bookMinutes: 0, playMinutes: 0, weeklyDaysTarget: 5, source: 'group', groupName: 'A' });
  });

  it('saves a personal override via PUT', () => {
    svc.saveGoal({ puzzleMinutes: 30, bookMinutes: 0, playMinutes: 0, weeklyDaysTarget: 0 })
      .subscribe(g => expect(g.source).toBe('personal'));
    const req = http.expectOne('/api/training-goals');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body.puzzleMinutes).toBe(30);
    req.flush({ puzzleMinutes: 30, bookMinutes: 0, playMinutes: 0, weeklyDaysTarget: 0, source: 'personal', groupName: null });
  });

  it('deletes the personal override via DELETE', () => {
    svc.deleteOverride().subscribe();
    const req = http.expectOne('/api/training-goals');
    expect(req.request.method).toBe('DELETE');
    req.flush({ puzzleMinutes: 0, bookMinutes: 0, playMinutes: 0, weeklyDaysTarget: 0, source: 'none', groupName: null });
  });

  it('requests the tracker with a weeks param', () => {
    svc.getTracker(10).subscribe();
    const req = http.expectOne(r => r.url === '/api/training-goals/tracker' && r.params.get('weeks') === '10');
    expect(req.request.method).toBe('GET');
    req.flush({ goal: {}, days: [] });
  });
});

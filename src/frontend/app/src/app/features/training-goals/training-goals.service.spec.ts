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
    req.flush({ puzzleMinutes: 15, bookMinutes: 0, playGames: 0, weeklyDaysTarget: 5, source: 'group', groupName: 'A' });
  });

  it('saves a personal override via PUT', () => {
    svc.saveGoal({ puzzleMinutes: 30, bookMinutes: 0, chessableMinutes: 0, playGames: 0, weeklyDaysTarget: 0 })
      .subscribe(g => expect(g.source).toBe('personal'));
    const req = http.expectOne('/api/training-goals');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body.puzzleMinutes).toBe(30);
    req.flush({ puzzleMinutes: 30, bookMinutes: 0, playGames: 0, weeklyDaysTarget: 0, source: 'personal', groupName: null });
  });

  it('deletes the personal override via DELETE', () => {
    svc.deleteOverride().subscribe();
    const req = http.expectOne('/api/training-goals');
    expect(req.request.method).toBe('DELETE');
    req.flush({ puzzleMinutes: 0, bookMinutes: 0, playGames: 0, weeklyDaysTarget: 0, source: 'none', groupName: null });
  });

  it('requests the tracker with a weeks param', () => {
    svc.getTracker(10).subscribe();
    const req = http.expectOne(r => r.url === '/api/training-goals/tracker' && r.params.get('weeks') === '10');
    expect(req.request.method).toBe('GET');
    req.flush({ goal: {}, days: [] });
  });

  it('lists manual activities', () => {
    svc.listManual().subscribe(list => expect(list.length).toBe(1));
    const req = http.expectOne('/api/training-goals/manual');
    expect(req.request.method).toBe('GET');
    req.flush([{ id: 1, date: '2026-06-22', kind: 'OtbGame', amount: 1, note: null }]);
  });

  it('adds a manual activity via POST', () => {
    svc.addManual({ date: '2026-06-22', kind: 'OfflineStudy', amount: 30, note: 'opening prep' }).subscribe();
    const req = http.expectOne('/api/training-goals/manual');
    expect(req.request.method).toBe('POST');
    expect(req.request.body.kind).toBe('OfflineStudy');
    req.flush({ id: 2, date: '2026-06-22', kind: 'OfflineStudy', amount: 30, note: 'opening prep' });
  });

  it('updates a manual activity via PUT', () => {
    svc.updateManual(5, { date: '2026-06-22', kind: 'Coaching', amount: 45, note: null }).subscribe();
    const req = http.expectOne('/api/training-goals/manual/5');
    expect(req.request.method).toBe('PUT');
    req.flush({ id: 5, date: '2026-06-22', kind: 'Coaching', amount: 45, note: null });
  });

  it('deletes a manual activity via DELETE', () => {
    svc.deleteManual(7).subscribe();
    const req = http.expectOne('/api/training-goals/manual/7');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });
});

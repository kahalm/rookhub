import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { TranslateModule } from '@ngx-translate/core';
import { ChessableImportsBannerComponent } from './chessable-imports-banner.component';
import { ChessableService, ChessableImport } from './chessable.service';

function imp(over: Partial<ChessableImport>): ChessableImport {
  return {
    id: 1, bid: 'b', courseName: 'C', target: 'repertoire', status: 'running', phase: 'fetching',
    error: null, resultId: null, imported: 0, skipped: 0, invalid: 0,
    chaptersDone: 1, chaptersTotal: 4, linesDone: 5, linesTotal: 0, queuedAhead: 0,
    createdAt: '2026-06-29T10:00:00Z', startedAt: null, completedAt: null, ...over,
  };
}

describe('ChessableImportsBannerComponent', () => {
  let fixture: ComponentFixture<ChessableImportsBannerComponent>;
  let component: ChessableImportsBannerComponent;
  let getImports: jasmine.Spy;

  function setup(initial: ChessableImport[]) {
    getImports = jasmine.createSpy('getImports').and.returnValue(of(initial));
    TestBed.configureTestingModule({
      imports: [ChessableImportsBannerComponent, TranslateModule.forRoot()],
      providers: [{ provide: ChessableService, useValue: { getImports } }],
    });
    fixture = TestBed.createComponent(ChessableImportsBannerComponent);
    component = fixture.componentInstance;
    fixture.detectChanges(); // ngOnInit → load() (synchron via of())
  }

  afterEach(() => fixture?.destroy());

  it('shows only active (running/paused) imports, sorted by # (queue position)', () => {
    setup([
      imp({ id: 1, bid: 'a', status: 'running', phase: 'fetching', queuedAhead: 0 }),
      imp({ id: 2, bid: 'done', status: 'completed', phase: 'done' }),
      imp({ id: 3, bid: 'wait', status: 'running', phase: 'queued', queuedAhead: 2 }),
      imp({ id: 4, bid: 'mid', status: 'paused', phase: 'queued', queuedAhead: 1 }),
    ]);
    // completed fällt raus; Rest nach queuedAhead 0,1,2
    expect(component.active.map(i => i.bid)).toEqual(['a', 'mid', 'wait']);
    expect(component.active.every(i => !!i.label)).toBeTrue();
  });

  it('renders nothing when there are no active imports', () => {
    setup([imp({ id: 9, status: 'completed' })]);
    expect(component.active.length).toBe(0);
    expect(fixture.nativeElement.querySelector('.queue-card')).toBeNull();
  });

  it('emits importCompleted when a previously active import disappears', () => {
    setup([imp({ id: 1, bid: 'a', status: 'running' })]);
    const spy = spyOn(component.importCompleted, 'emit');
    getImports.and.returnValue(of([])); // Import fertig → nicht mehr aktiv
    (component as any).poll();
    expect(spy).toHaveBeenCalled();
    expect(component.active.length).toBe(0);
  });
});

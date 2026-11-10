import { ChangeDetectionStrategy, Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { renderAfterHttpInterceptor } from '../../../core/render-after-http.interceptor';
import {
  AdminDiagramReportsComponent, EbookDiagramReportRow, EbookMissedDiagramRow, positionKey,
} from './admin-diagram-reports.component';

/** OnPush-Host wie die Admin-Seite: gezeichnet wird nach der HTTP-Antwort nur über den Scheduler (Angular-22-Falle). */
@Component({ selector: 'app-h-diag', standalone: true, imports: [AdminDiagramReportsComponent],
  changeDetection: ChangeDetectionStrategy.OnPush, template: '<app-admin-diagram-reports />' })
class Host {}

const report = (id: number, ebookId: number, title: string, note: string | null): EbookDiagramReportRow => ({
  id, ebookId, ebookTitle: title, diagramId: 100 + id, diagramExists: true, page: 12, fen: '8/8/8/8/8/8/8/K6k w - - 0 1',
  note, username: 'leser', fontFamily: 'opus', createdAt: '2026-11-09T10:00:00Z',
});
const missed = (id: number, ebookId: number, title: string, note: string | null, source: string | null): EbookMissedDiagramRow => ({
  id, ebookId, ebookTitle: title, page: 34, x: 0.1, y: 0.2, epubRef: null, epubChapter: null, isDiagram: true, note, source,
  userId: 1, username: 'claude-dev', createdAt: '2026-11-09T11:00:00Z',
});

describe('AdminDiagramReportsComponent', () => {
  let http: HttpTestingController;
  let fixture: ComponentFixture<Host>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Host],
      providers: [
        provideHttpClient(withInterceptors([renderAfterHttpInterceptor])), provideHttpClientTesting(), provideRouter([]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(Host);
    fixture.autoDetectChanges();
    await fixture.whenStable();
  });

  afterEach(() => http.verify());

  async function answer(reports: EbookDiagramReportRow[], rows: EbookMissedDiagramRow[]): Promise<HTMLElement> {
    http.expectOne('/api/admin/ebooks/diagram-reports').flush(reports);
    http.expectOne('/api/admin/ebooks/missed-diagrams').flush(rows);
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  it('loads both lists and shows note, source and a reader link to the page', async () => {
    const el = await answer(
      [report(1, 7, 'Silman', 'Dame auf d1 fehlt')],
      [missed(2, 8, 'Nunn', 'oben links, grauer Rahmen', 'review-2026-11'), missed(3, 7, 'Silman', null, null)]);

    expect(el.querySelectorAll('table.reports tbody tr').length).toBe(1);
    expect(el.querySelectorAll('table.missed tbody tr').length).toBe(2);
    expect(el.textContent).toContain('Dame auf d1 fehlt');
    expect(el.textContent).toContain('oben links, grauer Rahmen');
    expect(el.textContent).toContain('review-2026-11');
    const link = el.querySelector('table.missed tbody tr a') as HTMLAnchorElement;
    expect(link.getAttribute('href')).toBe('/ebooks/8?page=34');
  });

  it('filters both lists by book', async () => {
    await answer([report(1, 7, 'Silman', null)], [missed(2, 8, 'Nunn', null, null), missed(3, 7, 'Silman', null, null)]);
    const cmp = fixture.debugElement.children[0].componentInstance as AdminDiagramReportsComponent;
    expect(cmp.books().map(b => b.title)).toEqual(['Nunn', 'Silman']);

    cmp.bookId.set(8);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelectorAll('table.reports tbody tr').length).toBe(0);
    expect(el.querySelectorAll('table.missed tbody tr').length).toBe(1);
  });

  it('shows the load error instead of an empty state when a list fails', async () => {
    http.expectOne('/api/admin/ebooks/diagram-reports').flush([]);
    http.expectOne('/api/admin/ebooks/missed-diagrams').flush('x', { status: 500, statusText: 'err' });
    await new Promise(r => setTimeout(r));
    await fixture.whenStable();
    expect((fixture.nativeElement as HTMLElement).querySelector('app-load-error')).not.toBeNull();
  });

});

describe('positionKey', () => {
  it('names the position on a 3×3 grid', () => {
    expect(positionKey(0.1, 0.1)).toBe('tl');
    expect(positionKey(0.5, 0.5)).toBe('mc');
    expect(positionKey(0.9, 0.95)).toBe('br');
    expect(positionKey(0.5, 0.05)).toBe('tc');
  });
});

import { ComponentFixture, TestBed, fakeAsync, flushMicrotasks, tick } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { AuthService } from '@rh/core/auth.service';
import { ClubApiService } from '../../core/club-api.service';
import { ScoresheetScan } from '../../core/club.models';
import { ClubAddPageComponent } from './club-add-page.component';

const SCAN = (status: ScoresheetScan['status']): ScoresheetScan => ({
  id: 7, status, notationLanguage: 'de', createdAt: '2026-09-28T08:00:00Z', rounds: 1, moveCount: 40, uncertainCount: 2,
  unresolvedCount: 0, white: 'Didi', black: 'Hengl',
});

describe('ClubAddPageComponent', () => {
  let fixture: ComponentFixture<ClubAddPageComponent>;
  let api: jasmine.SpyObj<ClubApiService>;
  let query: Record<string, string>;

  beforeEach(() => {
    query = {};
    api = jasmine.createSpyObj<ClubApiService>('ClubApiService', ['importPgn', 'scans', 'scoresheetStatus', 'upload', 'discard']);
    api.scans.and.resolveTo([]);
    api.scoresheetStatus.and.resolveTo({ available: true, dailyLimit: 1, usedToday: 0, languages: [{ code: 'de', name: 'Deutsch', pieces: 'KDTLS' }] });
  });

  function create(): HTMLElement {
    TestBed.configureTestingModule({
      imports: [ClubAddPageComponent],
      providers: [
        provideRouter([]),
        { provide: ClubApiService, useValue: api },
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(query) } } },
        { provide: AuthService, useValue: { has: () => true, currentUser: { username: 'patrik' } } },
      ],
    });
    fixture = TestBed.createComponent(ClubAddPageComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('PGN: „durch Schwaz ersetzen" ist vorbelegt, das Ergebnis nennt die abgelehnten mit Grund', fakeAsync(() => {
    const el = create();
    flushMicrotasks();
    expect((el.querySelector('.anon-toggle input') as HTMLInputElement).checked).toBeTrue();
    api.importPgn.and.resolveTo({ added: 1, anonymized: 1, duplicates: 0, truncated: false, ids: [5],
      failed: [{ index: 2, white: 'A', black: 'B', reason: 'noLeaguePlayer' }] });
    fixture.componentInstance.pgn.set('[White "x"]\n1. e4 *');
    fixture.detectChanges();
    (el.querySelector('.btn-pri') as HTMLButtonElement).click();
    flushMicrotasks();
    fixture.detectChanges();
    expect(api.importPgn).toHaveBeenCalledWith('[White "x"]\n1. e4 *', true);
    expect(el.querySelector('.result')?.textContent).toContain('1 Partie übernommen (1 als Schwaz), 1 nicht übernommen.');
    expect(el.querySelector('tbody')?.textContent).toContain('Kein Ligaspieler');
  }));

  it('Formular: lädt ein Foto mit Notation und Seite hoch und fragt nach, bis es gelesen ist', fakeAsync(() => {
    query = { art: 'formular' };
    const el = create();
    flushMicrotasks();
    fixture.detectChanges();
    expect(el.textContent).toContain('Eines je 24 Stunden');
    api.upload.and.resolveTo(SCAN('pending'));
    api.scans.and.resolveTo([SCAN('done')]);
    const file = new File(['x'], 'bogen.jpg', { type: 'image/jpeg' });
    fixture.componentInstance.photo.set(file);
    fixture.componentInstance.side.set('white');
    fixture.componentInstance.upload();
    flushMicrotasks();
    expect(api.upload).toHaveBeenCalledWith(file, 'auto', 'white');
    tick(3000);
    flushMicrotasks();
    fixture.detectChanges();
    const link = el.querySelector('.scan-list a') as HTMLAnchorElement;
    expect(link.textContent).toContain('Prüfen und übernehmen');
    expect(link.getAttribute('href')).toBe('/verein/formular/7');
  }));
});

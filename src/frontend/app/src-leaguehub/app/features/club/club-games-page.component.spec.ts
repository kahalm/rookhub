import { ComponentFixture, TestBed, fakeAsync, flushMicrotasks } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from '@rh/core/auth.service';
import { ClubApiService } from '../../core/club-api.service';
import { LeagueApiService } from '../../core/league-api.service';
import { ClubGame } from '../../core/club.models';
import { ClubGamesPageComponent } from './club-games-page.component';

const G = (id: number, extra: Partial<ClubGame> = {}): ClubGame => ({
  id, year: 2024, white: 'Schwaz', black: 'Hengl, Philip', whiteFide: null, blackFide: '222', whiteElo: null, blackElo: 2172,
  result: '1-0', event: null, plies: 22, opening: '1.e4 c5 2.Nf3 d6', anonymized: true, canDelete: false, ...extra,
});

describe('ClubGamesPageComponent', () => {
  let fixture: ComponentFixture<ClubGamesPageComponent>;
  let api: jasmine.SpyObj<ClubApiService>;
  let perms: Set<string>;

  beforeEach(() => {
    perms = new Set(['league.view', 'league.contribute']);
    api = jasmine.createSpyObj<ClubApiService>('ClubApiService', ['list', 'deleteGame', 'pgn']);
    api.list.and.resolveTo({ total: 2, page: 1, pageSize: 50, items: [G(1), G(2, { white: 'Oberschmid, Patrik', whiteFide: '900', anonymized: false, canDelete: true })] });
  });

  function create(): HTMLElement {
    TestBed.configureTestingModule({
      imports: [ClubGamesPageComponent],
      providers: [
        provideRouter([]),
        { provide: ClubApiService, useValue: api },
        { provide: LeagueApiService, useValue: jasmine.createSpyObj('LeagueApiService', ['card', 'pgn']) },
        { provide: AuthService, useValue: { has: (p: string) => perms.has(p), currentUser: { username: 'patrik' } } },
      ],
    });
    fixture = TestBed.createComponent(ClubGamesPageComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('zeigt die Partien: Jahr, Namen (Ligaspieler anklickbar), Eröffnung deutsch, Löschen nur wo erlaubt', fakeAsync(() => {
    const el = create();
    flushMicrotasks();
    fixture.detectChanges();
    const rows = el.querySelectorAll('tbody tr');
    expect(rows.length).toBe(2);
    expect(rows[0].textContent).toContain('2024');
    expect(rows[0].querySelector('.anon')?.textContent).toBe('Schwaz');
    expect(rows[0].querySelector('button.pl')?.textContent).toContain('Hengl, Philip');
    expect(rows[0].textContent).toContain('1.e4 c5 2.Sf3 d6');
    expect(rows[0].textContent).not.toContain('Löschen');
    expect(rows[1].textContent).toContain('Löschen');
    expect(el.textContent).toContain('2 Partien');
    expect(el.querySelector('a.btn-pri')?.textContent).toContain('Partien hinzufügen');
  }));

  it('Löschen fragt nach und nimmt die Zeile heraus', fakeAsync(() => {
    const el = create();
    flushMicrotasks();
    fixture.detectChanges();
    spyOn(window, 'confirm').and.returnValue(true);
    api.deleteGame.and.resolveTo({});
    (el.querySelectorAll('tbody tr')[1].querySelector('.btn-link') as HTMLButtonElement).click();
    flushMicrotasks();
    fixture.detectChanges();
    expect(api.deleteGame).toHaveBeenCalledWith(2);
    expect(el.querySelectorAll('tbody tr').length).toBe(1);
    expect(el.textContent).toContain('1 Partie');
  }));

  it('Suche fragt mit dem Begriff, ohne Treffer ein Leerzustand', fakeAsync(() => {
    const el = create();
    flushMicrotasks();
    api.list.and.resolveTo({ total: 0, page: 1, pageSize: 50, items: [] });
    fixture.componentInstance.search(' Hengl ');
    flushMicrotasks();
    fixture.detectChanges();
    expect(api.list).toHaveBeenCalledWith(null, 'Hengl', 1);
    expect(el.textContent).toContain('Nichts gefunden');
  }));

  it('ohne Freischaltung kein Abruf', () => {
    perms = new Set();
    const el = create();
    expect(el.textContent).toContain('Nicht freigeschaltet');
    expect(api.list).not.toHaveBeenCalled();
  });
});

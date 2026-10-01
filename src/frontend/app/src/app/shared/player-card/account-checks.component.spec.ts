import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { LeagueApiService } from '@lh/core/league-api.service';
import { AccountChecks } from '@lh/core/league.models';
import { AccountChecksComponent } from './account-checks.component';

const CHECKS: AccountChecks = {
  site: 'lichess', user: 'MaxMuster', url: 'https://lichess.org/@/MaxMuster', player: 'Muster, Max', elo: 1900,
  checkedAt: '2026-09-30T18:00:00Z', profileLoaded: true,
  items: [
    { key: 'self', label: 'Selbstmeldung', status: 'ok', text: 'selbst gemeldet (Meldeliste Online-TMM 2021)' },
    { key: 'rating:Lichess Blitz', label: 'Lichess Blitz', status: 'warn', text: '2250 (300 Partien) — 350 über der Elo 1900' },
    { key: 'country', label: 'Land', status: 'fail', text: 'IT — weder Österreich noch seine Föderation (AUT)' },
    { key: 'fide', label: 'FIDE-Wertung im Profil', status: 'none', text: 'nicht angegeben' },
  ],
};

describe('AccountChecksComponent', () => {
  let fixture: ComponentFixture<AccountChecksComponent>;
  let api: jasmine.SpyObj<LeagueApiService>;
  const el = () => fixture.nativeElement as HTMLElement;

  async function render(kind: 'account' | 'suggestion', id: number): Promise<void> {
    fixture = TestBed.createComponent(AccountChecksComponent);
    fixture.componentRef.setInput('kind', kind);
    fixture.componentRef.setInput('id', id);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  beforeEach(() => {
    api = jasmine.createSpyObj<LeagueApiService>('LeagueApiService', ['accountChecks']);
    TestBed.configureTestingModule({ imports: [AccountChecksComponent], providers: [{ provide: LeagueApiService, useValue: api }] });
  });

  it('zeigt jede Prüfung mit Zeichen, Wort für Vorleser und Satz, oben die Zusammenfassung', async () => {
    api.accountChecks.and.resolveTo(CHECKS);
    await render('account', 7);
    expect(api.accountChecks).toHaveBeenCalledWith('account', 7);
    const rows = el().querySelectorAll('.chk-list li');
    expect(rows.length).toBe(4);
    expect(rows[0].classList).toContain('chk-ok');
    expect(rows[0].querySelector('.chk-sym')?.textContent).toBe('✓');
    expect(rows[0].querySelector('.sr-only')?.textContent).toContain('spricht dafür');
    expect(rows[2].classList).toContain('chk-fail');
    expect(rows[2].textContent).toContain('weder Österreich');
    expect(el().querySelector('.chk-sum')?.textContent).toContain('1 spricht dafür, 1 macht stutzig, 1 spricht dagegen.');
    expect(el().querySelector('.chk-sum')?.textContent).toContain('Elo laut Meldeliste: 1900');
    expect(el().textContent).not.toContain('nicht abrufbar');
  });

  it('404 heißt: das Konto gibt es nicht mehr; sonst ein Satz zum Nachladen', async () => {
    api.accountChecks.and.rejectWith(new HttpErrorResponse({ status: 404 }));
    await render('suggestion', 3);
    expect(el().textContent).toContain('gibt es nicht mehr');
    api.accountChecks.and.rejectWith(new HttpErrorResponse({ status: 500 }));
    await render('suggestion', 3);
    expect(el().textContent).toContain('ließ sich gerade nicht laden');
  });
});

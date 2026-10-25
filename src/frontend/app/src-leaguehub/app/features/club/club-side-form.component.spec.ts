import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ClubClient } from '../../core/club-api.service';
import { ScanHeaderForm } from './scan-header-form';
import { ClubSideFormComponent } from './club-side-form.component';

describe('ClubSideFormComponent', () => {
  let fixture: ComponentFixture<ClubSideFormComponent>;
  let form: ScanHeaderForm;

  beforeEach(() => {
    const api = jasmine.createSpyObj<ClubClient>('ClubClient', ['match', 'pairings', 'players']);
    api.pairings.and.resolveTo([]);
    api.players.and.resolveTo([]);
    form = new ScanHeaderForm({ api, anon: () => 'Testdorf', clubName: () => 'Testdorf', pairingsEnabled: false });
    TestBed.configureTestingModule({ imports: [ClubSideFormComponent] });
    fixture = TestBed.createComponent(ClubSideFormComponent);
    fixture.componentRef.setInput('form', form);
    fixture.componentRef.setInput('client', api);
    fixture.componentRef.setInput('anon', 'Testdorf');
    fixture.componentRef.setInput('clubName', 'Testdorf');
    fixture.detectChanges();
  });
  afterEach(() => form.dispose());

  it('zeigt je Seite Suche, „durch … ersetzen" und Elo; die Felder schreiben in das Formular', () => {
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelectorAll('lh-player-search').length).toBe(2);
    expect(el.querySelectorAll('.replace-row').length).toBe(2);
    expect(el.textContent).toContain('durch „Testdorf“ ersetzen');
    const elo = el.querySelector('input[maxlength="4"]') as HTMLInputElement;
    elo.value = '1834.6';
    elo.dispatchEvent(new Event('input'));
    expect(form.elo('white')()).toBe(1835);
    elo.value = ' ';
    elo.dispatchEvent(new Event('input'));
    expect(form.elo('white')()).toBeNull();
    const [white, black] = Array.from(el.querySelectorAll('.seg button')) as HTMLButtonElement[];
    black.click();
    fixture.detectChanges();
    expect(form.ownerSide()).toBe('black');
    expect(black.getAttribute('aria-pressed')).toBe('true');
    expect(white.getAttribute('aria-pressed')).toBe('false');
    expect(el.querySelector('.preview')).not.toBeNull();
  });

  it('Ergebnis „*" heißt „unbekannt"; das Jahr reicht bis nächstes Jahr', () => {
    const el = fixture.nativeElement as HTMLElement;
    expect(Array.from(el.querySelectorAll('select option')).map(o => o.textContent?.trim())).toEqual(['1-0', '0-1', '1/2-1/2', 'unbekannt']);
    expect((el.querySelector('input[type=number]') as HTMLInputElement).max).toBe(String(new Date().getFullYear() + 1));
  });
});

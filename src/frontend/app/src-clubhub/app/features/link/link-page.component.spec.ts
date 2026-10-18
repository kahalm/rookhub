import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { ClubApiService, apiErrorText } from '../../core/club-api.service';
import { LinkPageComponent } from './link-page.component';
import { LinkStateStore } from '../../core/link-state.store';
import { of } from 'rxjs';
import { ConfirmService } from '@rh/shared/confirm-dialog/confirm-dialog.component';

describe('LinkPageComponent (Konto verknüpfen)', () => {
  let fixture: ComponentFixture<LinkPageComponent>;
  let api: jasmine.SpyObj<ClubApiService>;
  /** Rückfrage (ConfirmService) — Vorgabe „ja", je Test umstellbar. */
  let confirmAsk: jasmine.Spy;
  const el = () => fixture.nativeElement as HTMLElement;

  async function create(query: Record<string, string> = {}): Promise<void> {
    TestBed.configureTestingModule({
      imports: [LinkPageComponent],
      providers: [{ provide: ConfirmService, useValue: { ask: (...a: unknown[]) => confirmAsk(...a) } }, { provide: ClubApiService, useValue: api },
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(query) } } }],
    });
    fixture = TestBed.createComponent(LinkPageComponent);
    await settle();
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  beforeEach(() => {
    confirmAsk = jasmine.createSpy('ask').and.returnValue(of(true));
    api = jasmine.createSpyObj<ClubApiService>('ClubApiService', ['linkState', 'redeem', 'selfUnlink']);
    api.linkState.and.resolveTo({ linked: false });
  });

  it('der Code aus dem Link steht schon im Feld; einlösen zeigt, mit wessen Blatt das Konto jetzt verknüpft ist', async () => {
    api.redeem.and.resolveTo({ linked: true, firstName: 'Daniel' });
    await create({ code: 'ABCDE-FGHJK' });
    expect(el().querySelector<HTMLInputElement>('.code-input')!.value).toBe('ABCDE-FGHJK');
    el().querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle();
    expect(api.redeem).toHaveBeenCalledWith('ABCDE-FGHJK');
    expect(el().querySelector('.linked')!.textContent).toContain('Karteiblatt von Daniel');
    expect(el().querySelector('form')).toBeNull();
  });

  it('ein unbekannter oder abgelaufener Code: die Begründung des Servers, das Feld bleibt', async () => {
    api.redeem.and.rejectWith(new HttpErrorResponse({ status: 404, error: { message: 'Der Code ist unbekannt oder abgelaufen.' } }));
    await create();
    // a1-c-disabled-button-contrast: ohne Code nicht gesperrt (grau, ohne Grund), sondern ein Hinweis beim Tippen.
    expect(el().querySelector<HTMLButtonElement>('button[type=submit]')!.disabled).toBeFalse();
    el().querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle();
    expect(api.redeem).not.toHaveBeenCalled();
    expect(el().querySelector('.need')!.textContent).toContain('Bitte zuerst den Code eingeben');
    expect(document.activeElement).toBe(el().querySelector('.code-input'));
    expect(el().querySelector('.code-input')!.getAttribute('aria-describedby')).toBe('code-missing');
    const input = el().querySelector<HTMLInputElement>('.code-input')!;
    input.value = 'X';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(el().querySelector('.need')).withContext('Hinweis weg, sobald getippt wird').toBeNull();
    fixture.componentInstance.code.set('XXXXX-XXXXX');
    await fixture.componentInstance.redeem();
    fixture.detectChanges();
    expect(el().querySelector('[role=alert]')!.textContent).toContain('Der Code ist unbekannt oder abgelaufen.');
    expect(el().querySelector('form')).not.toBeNull();
  });

  it('ein verknüpftes Konto sieht, was die Trainer sehen — und trennt sich selbst, nach Rückfrage', async () => {
    api.linkState.and.resolveTo({ linked: true, firstName: 'Daniel' });
    api.selfUnlink.and.resolveTo();
    await create();
    expect(el().textContent).toContain('Trainingsminuten');
    const ask = confirmAsk.and.returnValue(of(false));
    await fixture.componentInstance.unlink();
    expect(api.selfUnlink).not.toHaveBeenCalled();
    ask.and.returnValue(of(true));
    await fixture.componentInstance.unlink();
    fixture.detectChanges();
    expect(api.selfUnlink).toHaveBeenCalled();
    expect(el().querySelector('form')).not.toBeNull();
  });

  // d3-c-verknuepfen-titel-beschriftung: verknüpft heißt die Seite „Verknüpfung" (wie die Fußzeile), sonst „Konto verknüpfen".
  it('Überschrift folgt dem Stand: „Konto verknüpfen" vor dem Einlösen, „Verknüpfung" danach', async () => {
    api.redeem.and.resolveTo({ linked: true, firstName: 'Lena' });
    api.selfUnlink.and.resolveTo();
    await create();
    expect(el().querySelector('h1')!.textContent!.trim()).toBe('Konto verknüpfen');
    fixture.componentInstance.code.set('ABCDE-FGHJK');
    await fixture.componentInstance.redeem();
    fixture.detectChanges();
    expect(el().querySelector('h1')!.textContent!.trim()).toBe('Verknüpfung');
    await fixture.componentInstance.unlink();
    fixture.detectChanges();
    expect(el().querySelector('h1')!.textContent!.trim()).toBe('Konto verknüpfen');
  });

  // UI-Sweep 2026-10-11 (r2-c-footer-link, r2-c-code-field)
  it('Einlösen und Trennen landen auch im gemeinsamen Stand (Fußzeile); Feld und Knopf stehen in einer Zeile', async () => {
    api.redeem.and.resolveTo({ linked: true, firstName: 'Lena' });
    api.selfUnlink.and.resolveTo();
    await create();
    const row = el().querySelector('.code-row')!;
    expect(row.querySelector('.code-input')).not.toBeNull();
    expect(row.querySelector('button[type=submit]')).not.toBeNull();
    const store = TestBed.inject(LinkStateStore);
    expect(store.state()).toEqual({ linked: false });
    fixture.componentInstance.code.set('ABCDE-FGHJK');
    await fixture.componentInstance.redeem();
    expect(store.state()).toEqual({ linked: true, firstName: 'Lena' });
    await fixture.componentInstance.unlink();
    expect(store.state()).toEqual({ linked: false });
  });

  it('apiErrorText: der Text des Servers, sonst der Rückfall', () => {
    expect(apiErrorText(new HttpErrorResponse({ status: 400, error: { message: ' Der Vorname fehlt. ' } }), 'x')).toBe('Der Vorname fehlt.');
    expect(apiErrorText(new HttpErrorResponse({ status: 0 }), 'Speichern hat nicht geklappt.')).toBe('Speichern hat nicht geklappt.');
    expect(apiErrorText(new Error('kaputt'), 'x')).toBe('x');
  });
});

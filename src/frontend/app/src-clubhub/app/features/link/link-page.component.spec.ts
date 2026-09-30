import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { ClubApiService, apiErrorText } from '../../core/club-api.service';
import { LinkPageComponent } from './link-page.component';

describe('LinkPageComponent (Konto verknüpfen)', () => {
  let fixture: ComponentFixture<LinkPageComponent>;
  let api: jasmine.SpyObj<ClubApiService>;
  const el = () => fixture.nativeElement as HTMLElement;

  async function create(query: Record<string, string> = {}): Promise<void> {
    TestBed.configureTestingModule({
      imports: [LinkPageComponent],
      providers: [{ provide: ClubApiService, useValue: api },
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
    expect(el().querySelector<HTMLButtonElement>('button[type=submit]')!.disabled).toBeTrue();   // ohne Code nichts zu tun
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
    const ask = spyOn(window, 'confirm').and.returnValue(false);
    await fixture.componentInstance.unlink();
    expect(api.selfUnlink).not.toHaveBeenCalled();
    ask.and.returnValue(true);
    await fixture.componentInstance.unlink();
    fixture.detectChanges();
    expect(api.selfUnlink).toHaveBeenCalled();
    expect(el().querySelector('form')).not.toBeNull();
  });

  it('apiErrorText: der Text des Servers, sonst der Rückfall', () => {
    expect(apiErrorText(new HttpErrorResponse({ status: 400, error: { message: ' Der Vorname fehlt. ' } }), 'x')).toBe('Der Vorname fehlt.');
    expect(apiErrorText(new HttpErrorResponse({ status: 0 }), 'Speichern hat nicht geklappt.')).toBe('Speichern hat nicht geklappt.');
    expect(apiErrorText(new Error('kaputt'), 'x')).toBe('x');
  });
});

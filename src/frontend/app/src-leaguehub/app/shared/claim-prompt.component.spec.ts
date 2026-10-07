import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ClaimPromptComponent } from './claim-prompt.component';
import { ClubApiService, ClubClient } from '../core/club-api.service';
import { claimKeys, rememberClaimKey } from '../core/claim-keys';

describe('ClaimPromptComponent (0.656.0)', () => {
  let fixture: ComponentFixture<ClaimPromptComponent>;
  let api: jasmine.SpyObj<ClubClient>;
  const K1 = 'a'.repeat(32), K2 = 'b'.repeat(32);

  beforeEach(() => {
    localStorage.removeItem('lh-claim-keys');
    api = jasmine.createSpyObj<ClubClient>('ClubClient', ['claimPreview', 'claim', 'forgetClaims']);
    TestBed.configureTestingModule({
      imports: [ClaimPromptComponent],
      providers: [provideRouter([]), { provide: ClubApiService, useValue: { client: () => api } }],
    });
    fixture = TestBed.createComponent(ClaimPromptComponent);
  });
  afterEach(() => localStorage.removeItem('lh-claim-keys'));

  async function login(): Promise<HTMLElement> {
    fixture.componentRef.setInput('userId', 7);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('fragt nach dem Anmelden, ordnet erst bei Ja zu und verbraucht die Schlüssel', async () => {
    rememberClaimKey(K1);
    rememberClaimKey(K2);
    rememberClaimKey('kaputt');
    api.claimPreview.and.resolveTo({ games: 3, anonymized: 2 });
    api.claim.and.resolveTo({ claimed: 3 });
    const el = await login();
    expect(api.claimPreview).toHaveBeenCalledWith([K1, K2]);
    expect(el.textContent).toContain('3 Partien');
    expect(el.textContent).toContain('davon 2 anonymisiert');
    expect(api.claim).not.toHaveBeenCalled();
    await fixture.componentInstance.answer(true);
    fixture.detectChanges();
    expect(api.claim).toHaveBeenCalledWith([K1, K2]);
    expect(claimKeys()).toEqual([]);
    expect(el.textContent).toContain('3 Partien sind jetzt deinem Konto zugeordnet.');
  });

  it('Nein: nichts zugeordnet, Schlüssel verfallen', async () => {
    rememberClaimKey(K1);
    api.claimPreview.and.resolveTo({ games: 1, anonymized: 0 });
    api.forgetClaims.and.resolveTo({});
    await login();
    await fixture.componentInstance.answer(false);
    expect(api.forgetClaims).toHaveBeenCalledWith([K1]);
    expect(api.claim).not.toHaveBeenCalled();
    expect(claimKeys()).toEqual([]);
  });

  it('ohne Schlüssel oder ohne Anmeldung keine Frage', async () => {
    await login();
    expect(api.claimPreview).not.toHaveBeenCalled();
    rememberClaimKey(K1);
    fixture.componentRef.setInput('userId', null);
    fixture.detectChanges();
    expect(api.claimPreview).not.toHaveBeenCalled();
  });
});

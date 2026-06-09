import { TestBed } from '@angular/core/testing';
import { PwaInstallService } from './pwa-install.service';

/** Minimal-Stub eines beforeinstallprompt-Events. */
function makePromptEvent(outcome: 'accepted' | 'dismissed') {
  return {
    preventDefault: () => {},
    prompt: () => Promise.resolve(),
    userChoice: Promise.resolve({ outcome }),
  } as unknown as Event;
}

describe('PwaInstallService', () => {
  afterEach(() => { window.__rookhubInstallPrompt = null; });

  function create(): PwaInstallService {
    return TestBed.runInInjectionContext(() => new PwaInstallService());
  }

  it('übernimmt ein früh in index.html abgefangenes Event und meldet Installierbarkeit', () => {
    window.__rookhubInstallPrompt = makePromptEvent('accepted') as never;
    const svc = create();
    expect(svc.canInstallPwa()).toBeTrue();
  });

  it('fängt ein nach Bootstrap gefeuertes beforeinstallprompt', () => {
    const svc = create();
    expect(svc.canInstallPwa()).toBeFalse();
    window.dispatchEvent(Object.assign(new Event('beforeinstallprompt'), {
      prompt: () => Promise.resolve(),
      userChoice: Promise.resolve({ outcome: 'accepted' }),
    }));
    expect(svc.canInstallPwa()).toBeTrue();
  });

  it('promptInstall ohne Event liefert false', async () => {
    const svc = create();
    await expectAsync(svc.promptInstall()).toBeResolvedTo(false);
  });

  it('promptInstall verbraucht das Event und setzt canInstallPwa zurück', async () => {
    window.__rookhubInstallPrompt = makePromptEvent('accepted') as never;
    const svc = create();
    await expectAsync(svc.promptInstall()).toBeResolvedTo(true);
    expect(svc.canInstallPwa()).toBeFalse();
    await expectAsync(svc.promptInstall()).toBeResolvedTo(false);
  });
});

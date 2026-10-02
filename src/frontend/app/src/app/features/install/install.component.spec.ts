import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { APK_DOWNLOAD_URL, InstallComponent } from './install.component';
import { PwaInstallService } from '../../core/pwa-install.service';

describe('APK_DOWNLOAD_URL', () => {
  it('zeigt auf das jeweils neueste GitHub-Release (kein hartkodierter Versions-Tag)', () => {
    expect(APK_DOWNLOAD_URL).toBe(
      'https://github.com/kahalm/rookhub/releases/latest/download/app-release-signed.apk',
    );
    expect(APK_DOWNLOAD_URL).not.toMatch(/v\d+\.\d+\.\d+/);
  });
});

/**
 * Codereview W5 UX-051: auf Android standen APK (mit „unbekannte Quellen erlauben") und PWA
 * gleichrangig nebeneinander, und ohne `beforeinstallprompt` schickte die PWA-Karte Chrome-Nutzer
 * „nach Chrome". Jetzt steht die PWA oben (auf Android als empfohlen), der Rückfall nennt den
 * Menüweg, und am Desktop führt ein QR-Code aufs Handy statt „Datei übertragen".
 */
describe('InstallComponent', () => {
  function setup(platform: { android?: boolean; ios?: boolean; canInstall?: boolean; installed?: boolean } = {}) {
    const pwa = {
      isAndroid: !!platform.android,
      isIOS: !!platform.ios,
      canInstallPwa: signal(!!platform.canInstall),
      isInstalled: signal(!!platform.installed),
      promptInstall: jasmine.createSpy('promptInstall').and.resolveTo(true),
    };
    TestBed.configureTestingModule({
      imports: [InstallComponent],
      providers: [
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: PwaInstallService, useValue: pwa },
      ],
    });
    const fixture = TestBed.createComponent(InstallComponent);
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  afterEach(() => TestBed.resetTestingModule());

  it('stellt auf Android die Web-App als empfohlenen Weg VOR die APK', () => {
    const { el } = setup({ android: true });
    const cards = Array.from(el.querySelectorAll('.install-section'));
    expect(cards.length).toBe(2);
    expect(cards[0].classList).toContain('pwa-section');
    expect(cards[1].classList).toContain('apk-section');
    expect(cards[0].querySelector('.recommended')?.textContent).toContain('install.pwa.recommended');
    // Auf Android gibt es keinen QR-Code — man ist ja schon am Handy.
    expect(el.querySelector('app-qr-code')).toBeNull();
  });

  it('nennt ohne Installieren-Knopf den manuellen Weg, statt nur nach Chrome zu schicken', () => {
    const { el } = setup({ android: true, canInstall: false });
    expect(el.querySelector('.pwa-section .unavailable')?.textContent).toContain('install.pwa.unavailable');
  });

  it('bietet am Desktop einen QR-Code zu dieser Seite an, auf iOS nicht', () => {
    let { el } = setup({});
    const qr = el.querySelector('.apk-section app-qr-code');
    expect(qr).not.toBeNull();
    expect(el.querySelector('.apk-section .qr')?.textContent).toContain('install.apk.qrHint');
    expect(el.querySelector('.recommended')).toBeNull();   // „empfohlen" nur, wo es eine Wahl gibt
    TestBed.resetTestingModule();

    ({ el } = setup({ ios: true }));
    expect(el.querySelector('app-qr-code')).toBeNull();
  });

  it('zeigt das Empfehlungs-Abzeichen nicht, wenn die App schon installiert ist', () => {
    const { el } = setup({ android: true, installed: true });
    expect(el.querySelector('.recommended')).toBeNull();
    expect(el.querySelector('.installed')).not.toBeNull();
  });
});

/** Die TEXTE sind die halbe Behebung — sie stehen in den Sprachdateien, nicht im Template. */
describe('install-Texte (Codereview W5 UX-051)', () => {
  type InstallTexts = { install: { pwa: Record<string, string>; apk: Record<string, string> } };
  async function load(lang: string): Promise<InstallTexts> {
    for (const url of [`/i18n/${lang}.json`, `/base/i18n/${lang}.json`]) {
      const res = await fetch(url);
      if (res.ok) return res.json();
    }
    throw new Error(`${lang}.json nicht ladbar`);
  }

  for (const lang of ['en', 'de', 'hr', 'hu']) {
    it(`${lang}: der Rückfall nennt das Browsermenü, die APK-Karte rät nicht mehr zum Übertragen`, async () => {
      const t = await load(lang);
      expect(t.install.pwa['unavailable']).toContain('⋮');
      expect(t.install.pwa['recommended']).toBeTruthy();
      expect(t.install.apk['qrHint']).toBeTruthy();
      // „…und auf ein Android-Gerät übertragen" ist durch den QR-Code ersetzt.
      expect(t.install.apk['unavailable']).not.toMatch(/übertragen|transfer|prenijeti|átmásol/i);
    });
  }
});

/** Codereview W5 UX-052: eine Primäraktion je Seite, und das Info-Symbol wird im Zeilenumbruch nicht gestaucht. */
describe('InstallComponent — Primäraktion und Symbole (UX-052)', () => {
  function render(canInstall: boolean, android = true) {
    TestBed.configureTestingModule({
      imports: [InstallComponent],
      providers: [
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: PwaInstallService, useValue: {
          isAndroid: android, isIOS: false, canInstallPwa: signal(canInstall), isInstalled: signal(false),
          promptInstall: () => Promise.resolve(true),
        } },
      ],
    });
    const fixture = TestBed.createComponent(InstallComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  afterEach(() => TestBed.resetTestingModule());

  it('bietet der Browser die Web-App an, ist sie die Primäraktion und die APK nur Alternative', () => {
    const el = render(true);
    expect(el.querySelectorAll('.mat-primary').length).toBe(1);
    expect(el.querySelector('.pwa-section .mat-primary')).not.toBeNull();
    expect(el.querySelector('.apk-section a[mat-stroked-button]')).not.toBeNull();
  });

  it('ohne Installieren-Knopf ist „APK herunterladen“ die einzige Primäraktion', () => {
    const el = render(false);
    expect(el.querySelectorAll('.mat-primary').length).toBe(1);
    expect(el.querySelector('.apk-section a.mat-primary')).not.toBeNull();
  });

  it('lässt das Info-Symbol vor einem Hinweis in voller Breite stehen', () => {
    const el = render(false, false);
    document.body.appendChild(el);
    try {
      const icon = el.querySelector('.unavailable mat-icon')!;
      expect(getComputedStyle(icon).flexShrink).toBe('0');
    } finally {
      el.remove();
    }
  });
});

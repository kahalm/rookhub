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

  // d3-rk-install-karte-fliesstext: ein Satz sichtbar, die Menü-Anleitung liegt hinter dem ?-Symbol.
  it('zeigt ohne Installieren-Knopf nur einen Satz, die Anleitung steckt im ?', () => {
    const { el } = setup({ canInstall: false });
    const p = el.querySelector('.pwa-section .unavailable') as HTMLElement;
    const hint = p.querySelector('app-help-hint button') as HTMLElement;
    expect(hint).not.toBeNull();
    expect(hint.getAttribute('aria-label')).toBe('install.pwa.unavailableHelp');
    expect(p.textContent).toContain('install.pwa.unavailable');
    expect(p.textContent).not.toContain('install.pwa.unavailableHelp');
  });

  // d4-rk-hilfe-symbol-umbruch: vor dem ? stand ein normaler Umbruch (= Leerzeichen) — das Symbol brach allein um.
  it('d4: vor dem ? steht ein geschütztes Leerzeichen, keine Umbruchstelle', () => {
    const { el } = setup({ canInstall: false });
    const hint = el.querySelector('.pwa-section .unavailable app-help-hint') as HTMLElement;
    const prev = hint.previousSibling!;
    expect(prev.nodeType).toBe(Node.TEXT_NODE);
    expect(prev.textContent!.endsWith(' ')).withContext(JSON.stringify(prev.textContent)).toBeTrue();
    expect(/\s$/.test(prev.textContent!.replace(/ $/, ''))).toBeFalse();
  });

  // d7-rk-install-hilfesymbol-allein-in-zeile: U+00A0 allein schützt nicht vor dem Umbruch vor app-help-hint
  // (inline-flex = atomares Inline-Element). U+00A0 und Symbol stehen gemeinsam in .rh-nowrap (styles.scss).
  it('d7: das ? bricht bei keiner Breite allein in eine eigene Zeile', () => {
    const { el } = setup({ canInstall: false });
    const p = el.querySelector('.pwa-section .unavailable') as HTMLElement;
    const wrap = p.querySelector('.rh-nowrap') as HTMLElement;
    expect(wrap).withContext('Klammer um U+00A0 + ?').not.toBeNull();
    expect(getComputedStyle(wrap).whiteSpace).toBe('nowrap');
    expect(wrap.firstChild!.textContent).toBe('\u00a0');
    const hint = wrap.querySelector('app-help-hint') as HTMLElement;
    const textNode = wrap.previousSibling as Text;
    textNode.textContent = 'Your browser is not offering an install button right now.';
    expect(document.body.contains(el)).toBeTrue();
    {
      for (let w = 160; w <= 480; w += 4) {
        p.style.width = `${w}px`;
        const r = document.createRange();
        r.setStart(textNode, textNode.length - 1);
        r.setEnd(textNode, textNode.length);
        const last = r.getBoundingClientRect();
        const h = hint.getBoundingClientRect();
        // gleiche Zeile: die senkrechten Bereiche von letztem Zeichen und Symbol überschneiden sich
        expect(h.top < last.bottom && h.bottom > last.top).withContext(`Breite ${w}px`).toBeTrue();
      }
    }
  });

  it('Kartentexte sind je ein Satz (de/en/hr/hu)', async () => {
    for (const lang of ['de', 'en', 'hr', 'hu']) {
      let res = await fetch(`/i18n/${lang}.json`).catch(() => null);
      if (!res?.ok) res = await fetch(`/base/i18n/${lang}.json`);
      expect(res!.ok).withContext(`${lang}.json`).toBeTrue();
      const i = (await res!.json()).install;
      for (const t of [i.pwa.desc, i.pwa.unavailable, i.apk.qrHint]) {
        expect(t.split(/[.!?](\s|$)/).filter((x: string | undefined) => x && x.trim()).length)
          .withContext(`${lang}: ${t}`).toBe(1);
      }
    }
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
      // d3-rk-install-karte-fliesstext: der Menüweg steht seitdem hinter dem ?-Symbol (unavailableHelp).
      expect(t.install.pwa['unavailableHelp']).toContain('⋮');
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

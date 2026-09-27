import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { PrivacyComponent } from './privacy.component';
import { LEGAL_SITE, LegalSite } from './legal-site';
import { OPERATOR } from '../../../environments/operator';

describe('PrivacyComponent', () => {
  function render(site?: LegalSite): HTMLElement {
    TestBed.configureTestingModule({
      imports: [PrivacyComponent],
      providers: [
        provideRouter([]), provideTranslateService({ fallbackLang: 'en' }),
        ...(site ? [{ provide: LEGAL_SITE, useValue: site }] : []),
      ],
    });
    const f = TestBed.createComponent(PrivacyComponent);
    f.detectChanges();
    return f.nativeElement as HTMLElement;
  }

  const hrefs = (el: HTMLElement) =>
    Array.from(el.querySelectorAll('a') as NodeListOf<HTMLAnchorElement>).map(a => a.getAttribute('href'));

  it('RookHub: verweist auf das Impressum, Kontakt aus OPERATOR', () => {
    const el = render();
    expect(hrefs(el)).toContain('/impressum');
    expect(hrefs(el)).toContain('mailto:' + OPERATOR.email);
  });

  it('KidHub: kein Impressum, eigene Adresse — auch fuer den Verantwortlichen', () => {
    const el = render({ contactEmail: 'kidhub@oberschm.id', imprint: false });
    expect(hrefs(el)).not.toContain('/impressum');
    expect(hrefs(el).filter(h => h === 'mailto:kidhub@oberschm.id').length).toBe(2);
    expect(el.textContent).not.toContain(OPERATOR.email);
  });
});

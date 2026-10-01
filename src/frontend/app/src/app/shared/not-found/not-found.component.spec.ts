import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Component } from '@angular/core';
import { RouterLink, Routes, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { NotFoundComponent } from './not-found.component';

@Component({ standalone: true, template: '' })
class StubComponent {}

/** Hinweisseite für unbekannte Adressen (UX-026): Text plus ein Weg weiter, kein stummer Sprung. */
function render(routes: Routes): HTMLElement & { links: string[] } {
  TestBed.configureTestingModule({
    imports: [NotFoundComponent],
    providers: [provideRouter(routes), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' })],
  });
  const fixture = TestBed.createComponent(NotFoundComponent);
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement & { links: string[] };
  el.links = fixture.debugElement.queryAll(By.directive(RouterLink))
    .map(de => de.injector.get(RouterLink).urlTree?.toString() ?? '');
  return el;
}

describe('NotFoundComponent', () => {
  it('sagt, dass es die Adresse nicht (mehr) gibt, und führt zur Startseite', () => {
    const el = render([{ path: '', component: StubComponent }]);
    // Im Test sind keine Übersetzungen geladen — die rohen Keys zeigen, WAS dasteht.
    expect(el.querySelector('h1')?.textContent).toContain('app.notFound.title');
    expect(el.textContent).toContain('app.notFound.text');
    expect(el.links).toEqual(['/']);
  });

  it('bietet die Hilfe nur, wo die App eine Hilfeseite hat (RookHub ja, LeagueHub nein)', () => {
    const el = render([{ path: '', component: StubComponent }, { path: 'help', component: StubComponent }]);
    expect(el.links).toEqual(['/', '/help']);
  });
});

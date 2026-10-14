import { Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { ReconstructListComponent } from './reconstruct-list.component';
import { ReconstructDetailComponent } from './reconstruct-detail.component';

function compactCss(cmp: Type<unknown>): string {
  return (((cmp as unknown as { ɵcmp: { styles?: string[] } }).ɵcmp.styles) ?? []).join('\n')
    .replace(/%NS%/g, '').replace(/\[_ngcontent-[^\]]*\]/g, '').replace(/\s/g, '');
}

describe('UI-Sweep b1 — Partie rekonstruieren', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ReconstructListComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
  });

  function list(items: unknown[]) {
    const fixture = TestBed.createComponent(ReconstructListComponent);
    fixture.detectChanges();
    TestBed.inject(HttpTestingController).expectOne('/api/reconstructions').flush(items);
    fixture.detectChanges();
    return fixture;
  }

  it('b1-rk-recon-leer-kontrast: Leersatz nur mit .empty-state (nicht zusätzlich .muted)', () => {
    const p = (list([]).nativeElement as HTMLElement).querySelector('p.empty-state')!;
    expect(p).not.toBeNull();
    expect(p.classList).not.toContain('muted');
  });

  it('b1-rk-recon-anlegen-ohne-grund: gesperrtes „Anlegen" nennt seinen Grund — auch mit bestehenden Rekonstruktionen', async () => {
    const fixture = list([{ id: 1, title: 'Runde 3', partCount: 2, knownPlies: 12, gaps: 0, updatedAt: '2026-09-20T10:00:00Z' }]);
    const el = fixture.nativeElement as HTMLElement;
    expect((el.querySelector('.create-btn') as HTMLButtonElement).disabled).toBeTrue();
    expect(el.querySelector('mat-hint.create-hint')!.textContent!.trim()).toBe('reconstruct.createHint');

    fixture.componentInstance.newTitle = 'Runde 4';
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect((el.querySelector('.create-btn') as HTMLButtonElement).disabled).toBeFalse();
    expect(el.querySelector('mat-hint.create-hint')).toBeNull();
  });

  it('b1-rk-recon-breitstufe: Bearbeiten ab 2400 px — Brett-Editor bis 900 px, Brett bis Fensterhöhe', () => {
    const css = compactCss(ReconstructDetailComponent);
    const at = css.indexOf('@media(min-width:2400px)');
    expect(at).toBeGreaterThan(-1);
    const wide = css.slice(at);
    expect(wide).toContain('.columns{grid-template-columns:minmax(420px,1fr)minmax(600px,900px)');
    expect(wide).toMatch(/\.editorapp-chess-board,\.editorapp-position-setup,\.arrow-hint,\.ply-nav\{max-width:min\(100%,(calc\()?100vh-360px\)?\)/);
    // Darunter bleibt die 56vh-Schranke.
    expect(css.slice(0, at)).toContain('max-width:min(100%,56vh)');
  });
});

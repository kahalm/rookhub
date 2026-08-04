import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { PuzzleActionBarComponent } from './puzzle-action-bar.component';

/**
 * Die EINE Aktionszeile der Solver (UI-Welle 2): Rating-Pille + Tags-Toggle sichtbar,
 * Teilen als Icon, Seltenes (Letztes ansehen/lieben, Endlos, Einstellungen) im ⋮-Menü.
 */
describe('PuzzleActionBarComponent', () => {
  function make(inputs: Partial<PuzzleActionBarComponent> = {}) {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [PuzzleActionBarComponent],
      providers: [provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' })],
    });
    const fixture = TestBed.createComponent(PuzzleActionBarComponent);
    for (const [k, v] of Object.entries(inputs)) fixture.componentRef.setInput(k, v);
    fixture.detectChanges();
    return fixture;
  }

  function openMenu(fixture: ReturnType<typeof make>): HTMLElement {
    const el: HTMLElement = fixture.nativeElement;
    (el.querySelectorAll('button[mat-icon-button]')[1] as HTMLButtonElement).click();
    fixture.detectChanges();
    return document.querySelector('.mat-mdc-menu-panel') as HTMLElement;
  }

  afterEach(() => {
    document.querySelectorAll('.cdk-overlay-container').forEach(n => n.remove());
  });

  it('zeigt Rating-Pille + Tags-Toggle, Teilen feuert', () => {
    const fixture = make({ rating: 1650, tags: 'fork pin' });
    const c = fixture.componentInstance;
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('.pab-rating')?.textContent).toContain('1650');
    expect(el.querySelector('.puzzle-tags-toggle')).toBeTruthy();

    let shared = 0;
    c.shareClicked.subscribe(() => shared++);
    (el.querySelector('.pab-share') as HTMLButtonElement).click();
    expect(shared).toBe(1);
  });

  it('ohne Rating keine Pille (Endless zeigt sie in den Quick-Stats)', () => {
    const fixture = make({ rating: null });
    expect((fixture.nativeElement as HTMLElement).querySelector('.pab-rating')).toBeNull();
  });

  it('⋮-Menü: Einstellungen immer; Letztes/Lieben/Endlos nur wenn erlaubt', () => {
    const minimal = openMenu(make({ hasLast: false, showEndless: false }));
    expect(minimal.querySelectorAll('button[mat-menu-item]').length).toBe(1);   // nur Einstellungen

    const full = openMenu(make({ hasLast: true, canLoveLast: true, showEndless: true }));
    expect(full.querySelectorAll('button[mat-menu-item]').length).toBe(4);
  });

  it('⋮-Menü-Einträge feuern die Outputs', () => {
    const fixture = make({ hasLast: true, canLoveLast: true, showEndless: true });
    const c = fixture.componentInstance;
    const fired: string[] = [];
    c.reviewLastClicked.subscribe(() => fired.push('review'));
    c.loveLastClicked.subscribe(() => fired.push('love'));
    c.endlessClicked.subscribe(() => fired.push('endless'));
    c.settingsClicked.subscribe(() => fired.push('settings'));

    const panel = openMenu(fixture);
    const items = panel.querySelectorAll('button[mat-menu-item]');
    items.forEach(b => (b as HTMLButtonElement).click());
    expect(fired).toEqual(['review', 'love', 'endless', 'settings']);
  });
});

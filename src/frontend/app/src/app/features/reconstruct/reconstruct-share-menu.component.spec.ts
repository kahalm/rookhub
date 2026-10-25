import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { ReconstructShareMenuComponent } from './reconstruct-share-menu.component';

describe('ReconstructShareMenuComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ReconstructShareMenuComponent],
      providers: [provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
  });

  function openMenu(inputs: Record<string, unknown>) {
    const fixture = TestBed.createComponent(ReconstructShareMenuComponent);
    for (const [k, v] of Object.entries(inputs)) fixture.componentRef.setInput(k, v);
    fixture.detectChanges();
    (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('button.head-menu')!.click();
    fixture.detectChanges();
    return fixture;
  }
  const items = () => Array.from(document.querySelectorAll<HTMLButtonElement>('.mat-mdc-menu-item'));

  it('noch nicht geteilt: nur „Teilen", kein „Beenden"; ohne Anfang kein Kopieren', () => {
    openMenu({ hasPrefix: false, shared: false });
    const text = items().map(b => b.textContent).join('|');
    expect(text).toContain('reconstruct.share.action');
    expect(text).not.toContain('reconstruct.share.stop');
    expect(text).not.toContain('reconstruct.copyPrefix');
  });

  it('geteilt: Link erneut kopieren und beenden; meldet die Klicks', () => {
    const fixture = openMenu({ hasPrefix: true, shared: true });
    const events: string[] = [];
    const c = fixture.componentInstance;
    c.copyPrefix.subscribe(() => events.push('copy'));
    c.share.subscribe(() => events.push('share'));
    c.stopSharing.subscribe(() => events.push('stop'));
    const text = items().map(b => b.textContent).join('|');
    expect(text).toContain('reconstruct.share.copyAgain');
    items().forEach(b => b.click());
    expect(events).toEqual(['copy', 'share', 'stop']);
  });
});

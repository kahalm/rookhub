import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { HandoffService } from '@rh/core/handoff.service';
import { RookHubLinkComponent } from './rookhub-link.component';

/**
 * Codereview UX-079: die Turnierseite verwies fuer Passwort, Freunde und Verwaltung auf RookHub,
 * bot dorthin aber keinen Weg.
 */
describe('RookHubLinkComponent', () => {
  let fixture: ComponentFixture<RookHubLinkComponent>;
  let handoff: HandoffService;
  let jump: jasmine.Spy;

  function create(base: string | null): void {
    TestBed.configureTestingModule({
      imports: [RookHubLinkComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    handoff = TestBed.inject(HandoffService);
    spyOnProperty(handoff, 'accountHomeUrl', 'get').and.returnValue(base);
    jump = spyOn(handoff, 'jumpToAccountHome').and.resolveTo();
    fixture = TestBed.createComponent(RookHubLinkComponent);
    fixture.componentRef.setInput('path', 'profile');
    fixture.detectChanges();
  }

  /** Ein Klick-Ereignis, dessen Standardaktion (Seitenwechsel) der Test nur beobachtet. */
  function click(init: MouseEventInit = {}): MouseEvent {
    const event = new MouseEvent('click', { button: 0, ...init });
    spyOn(event, 'preventDefault');
    fixture.componentInstance.open(event);
    return event;
  }

  afterEach(() => TestBed.resetTestingModule());

  it('verlinkt auf die Seite in RookHub und nimmt beim Klick die Anmeldung mit', () => {
    create('https://rookhub.example.org');
    const anchor = (fixture.nativeElement as HTMLElement).querySelector('a')!;

    expect(anchor.getAttribute('href')).toBe('https://rookhub.example.org/profile');
    const event = click();
    expect(event.preventDefault).toHaveBeenCalled();
    expect(jump).toHaveBeenCalledWith('profile');
  });

  it('lässt Strg-Klick als gewöhnlichen Link (neuer Tab)', () => {
    create('https://rookhub.example.org');

    const event = click({ ctrlKey: true });

    expect(event.preventDefault).not.toHaveBeenCalled();
    expect(jump).not.toHaveBeenCalled();
  });

  it('fällt ohne bekannte RookHub-Adresse weg, statt eine zu raten', () => {
    create(null);

    expect((fixture.nativeElement as HTMLElement).hidden).toBeTrue();
    click();
    expect(jump).not.toHaveBeenCalled();
  });
});

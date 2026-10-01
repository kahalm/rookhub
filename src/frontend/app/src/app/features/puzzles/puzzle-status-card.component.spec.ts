import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { PuzzleStatusCardComponent } from './puzzle-status-card.component';

describe('PuzzleStatusCardComponent', () => {
  async function setup() {
    await TestBed.configureTestingModule({
      imports: [PuzzleStatusCardComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    return TestBed.createComponent(PuzzleStatusCardComponent);
  }

  it('creates (template AOT-compiles + DI resolves)', async () => {
    const fixture = await setup();
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('exposes the solver state as data-state for the E2E specs', async () => {
    const fixture = await setup();
    const card = () => fixture.nativeElement.querySelector('.psc-card') as HTMLElement;

    fixture.componentRef.setInput('state', 'AWAITING_USER_MOVE');
    fixture.detectChanges();
    expect(card().getAttribute('data-state')).toBe('AWAITING_USER_MOVE');

    fixture.componentRef.setInput('state', 'SOLVED');
    fixture.detectChanges();
    expect(card().getAttribute('data-state')).toBe('SOLVED');
  });

  // Codereview UX-045: wartet der Löser nur auf die Spielweise, bittet die Karte um die Wahl statt „lädt…".
  it('LOADING + awaitingSolveMode: Bitte um die Spielweise statt Lade-Spinner', async () => {
    const fixture = await setup();
    fixture.componentRef.setInput('mode', 'book');
    fixture.componentRef.setInput('state', 'LOADING');
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('mat-spinner')).not.toBeNull();
    expect(el.textContent).toContain('book.status.loading');

    fixture.componentRef.setInput('awaitingSolveMode', true);
    fixture.detectChanges();
    expect(el.querySelector('mat-spinner')).toBeNull();
    expect(el.textContent).toContain('solveMode.awaitingChoice');
    expect(el.textContent).not.toContain('book.status.loading');
  });
});

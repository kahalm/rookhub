import { TestBed } from '@angular/core/testing';
import { MatDialogRef } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { PreferencesService } from '../../../core/preferences.service';
import { BOARD_THEMES, PIECE_SETS } from '../../puzzles/board-theme.util';
import { CalcSettingsDialogComponent } from './calc-settings-dialog.component';

describe('CalcSettingsDialogComponent', () => {
  let prefs: { boardTheme: string; pieceSet: string; setBoardTheme: jasmine.Spy; setPieceSet: jasmine.Spy };
  let close: jasmine.Spy;

  beforeEach(async () => {
    prefs = {
      boardTheme: BOARD_THEMES[0].key,
      pieceSet: PIECE_SETS[0].key,
      setBoardTheme: jasmine.createSpy('setBoardTheme'),
      setPieceSet: jasmine.createSpy('setPieceSet'),
    };
    close = jasmine.createSpy('close');
    await TestBed.configureTestingModule({
      imports: [CalcSettingsDialogComponent],
      providers: [
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: PreferencesService, useValue: prefs },
        { provide: MatDialogRef, useValue: { close } },
      ],
    }).compileComponents();
  });
  afterEach(() => TestBed.resetTestingModule());

  function render() {
    const fixture = TestBed.createComponent(CalcSettingsDialogComponent);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const groups = el.querySelectorAll('.cs-chips');
    return {
      el,
      boards: groups[0].querySelectorAll<HTMLButtonElement>('.cs-chip'),
      pieces: groups[1].querySelectorAll<HTMLButtonElement>('.cs-chip'),
    };
  }

  it('bietet jede Brett- und Figurenart an und markiert die aktuelle', () => {
    const { boards, pieces } = render();

    expect(boards.length).toBe(BOARD_THEMES.length);
    expect(pieces.length).toBe(PIECE_SETS.length);
    expect(boards[0].getAttribute('aria-pressed')).toBe('true');
    expect(pieces[0].getAttribute('aria-pressed')).toBe('true');
    expect(Array.from(boards).filter(b => b.classList.contains('cs-chip--on')).length).toBe(1);
    expect(Array.from(pieces).filter(b => b.classList.contains('cs-chip--on')).length).toBe(1);
  });

  it('eine Wahl wirkt sofort über den PreferencesService', () => {
    const { boards, pieces } = render();

    boards[boards.length - 1].click();
    pieces[pieces.length - 1].click();

    expect(prefs.setBoardTheme).toHaveBeenCalledOnceWith(BOARD_THEMES[BOARD_THEMES.length - 1].key);
    expect(prefs.setPieceSet).toHaveBeenCalledOnceWith(PIECE_SETS[PIECE_SETS.length - 1].key);
  });

  it('hat nur „Schließen" und schließt ohne Wert (nicht mit dem leeren String)', () => {
    const { el } = render();
    const actions = el.querySelectorAll<HTMLButtonElement>('mat-dialog-actions button');

    expect(actions.length).toBe(1);
    actions[0].click();

    expect(close).toHaveBeenCalledOnceWith();
  });
});

import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { TeamPlayersDialogComponent } from './team-players-dialog.component';
import { TournamentPlayer } from '@rh/core/models';

describe('TeamPlayersDialogComponent', () => {
  async function render(players: Partial<TournamentPlayer>[]): Promise<HTMLElement> {
    await TestBed.configureTestingModule({
      imports: [TeamPlayersDialogComponent],
      providers: [
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: MatDialogRef, useValue: { close: () => {} } },
        { provide: MAT_DIALOG_DATA, useValue: { teamName: 'Reinter Snina', players } },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(TeamPlayersDialogComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  // d7-t-teamdialog-titel-codes: das Fenster zeigte Rohcodes wie „Z56“ in der Spalte „Titel“
  it('zeigt nur echte Titel; Rohcodes wie „Z56“ erscheinen nicht', async () => {
    const el = await render([
      { boardNumber: 1, title: 'Z56', name: 'Eins', elo: 2100 },
      { boardNumber: 2, title: 'fm', name: 'Zwei', elo: 2300 },
    ]);
    expect(el.textContent).not.toContain('Z56');
    expect(el.querySelectorAll('th').length).toBe(4);
    expect(Array.from(el.querySelectorAll('td.mat-column-title')).map(td => td.textContent!.trim())).toEqual(['', 'FM']);
  });

  it('ohne echten Titel fehlt die Spalte „Titel“', async () => {
    const el = await render([
      { boardNumber: 1, title: 'Z56', name: 'Eins', elo: 2100 },
      { boardNumber: 2, title: 'Z55', name: 'Zwei', elo: 2000 },
    ]);
    expect(el.querySelector('.mat-column-title')).toBeNull();
    expect(el.querySelectorAll('th').length).toBe(3);
    expect(el.textContent).not.toContain('Z5');
  });

  it('creates (template AOT-compiles + DI resolves)', async () => {
    await TestBed.configureTestingModule({
      imports: [TeamPlayersDialogComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: MatDialogRef, useValue: { close: () => {} } },
        { provide: MAT_DIALOG_DATA, useValue: {} },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(TeamPlayersDialogComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });
});

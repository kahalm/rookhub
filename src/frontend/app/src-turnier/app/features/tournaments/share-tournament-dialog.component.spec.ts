import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { ShareTournamentDialogComponent } from './share-tournament-dialog.component';

describe('ShareTournamentDialogComponent', () => {
  it('creates (template AOT-compiles + DI resolves)', async () => {
    await TestBed.configureTestingModule({
      imports: [ShareTournamentDialogComponent],
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
    const fixture = TestBed.createComponent(ShareTournamentDialogComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  // d8-t-dialog-titel-fett, d8-t-teilen-link-*, d8-t-share-url-feld-hell
  it('Baukasten-Titel, Link steht am Anfang, Feld aus Tokens ohne hellen Festwert', async () => {
    await TestBed.configureTestingModule({
      imports: [ShareTournamentDialogComponent],
      providers: [
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: MatDialogRef, useValue: { close: () => {} } },
        { provide: MAT_DIALOG_DATA, useValue: { url: 'https://turnier-dev.oberschmid.homes/t/65' + '/x'.repeat(40) } },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(ShareTournamentDialogComponent);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('h2[mat-dialog-title]')).not.toBeNull();
    expect(el.querySelector('.dialog-title, .dialog-actions')).toBeNull();
    expect(el.querySelector('mat-dialog-content .link-input')).not.toBeNull();
    expect(el.querySelector('mat-dialog-actions')).not.toBeNull();
    const input = el.querySelector('.link-input') as HTMLInputElement;
    expect(input.scrollLeft).toBe(0);
    expect(document.activeElement).not.toBe(input);
    expect(getComputedStyle(input).textOverflow).toBe('ellipsis');
    const src = (ShareTournamentDialogComponent as unknown as { ɵcmp: { styles: string[] } }).ɵcmp.styles.join('');
    expect(src).not.toContain('#f5f5f5');
    expect(src).toContain('mat-sys-surface-container-highest');   // kompiliert: var(--%NS%mat-sys-…)
  });
});

import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { PositionSetupComponent } from './position-setup.component';

describe('PositionSetupComponent', () => {
  it('creates (template AOT-compiles + DI resolves)', async () => {
    await TestBed.configureTestingModule({
      imports: [PositionSetupComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(PositionSetupComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('d7: „Brett leeren“/„Grundstellung“ stehen beschriftet im ⋮ statt als lose ✕; hideActions blendet die Knopfzeile aus', async () => {
    await TestBed.configureTestingModule({
      imports: [PositionSetupComponent],
      providers: [provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    const fixture = TestBed.createComponent(PositionSetupComponent);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const toolIcons = Array.from(el.querySelectorAll('.ps-tools mat-icon')).map(i => i.textContent?.trim());
    expect(toolIcons).not.toContain('clear');
    expect(toolIcons).toContain('more_vert');
    expect(el.querySelector('.ps-actions')).withContext('Knopfzeile ohne hideActions').toBeTruthy();
    (el.querySelector('.ps-more') as HTMLElement).click();
    fixture.detectChanges();
    const clear = document.querySelector('.cdk-overlay-container .ps-clear') as HTMLElement;
    expect(clear?.textContent).toContain('analysis.setup.clear');
    expect(clear.querySelector('mat-icon')?.textContent?.trim()).toBe('layers_clear');
    expect(document.querySelector('.cdk-overlay-container .ps-startpos')?.textContent).toContain('analysis.setup.startpos');
    clear.click();
    fixture.componentRef.setInput('hideActions', true);
    fixture.detectChanges();
    expect(el.querySelector('.ps-actions')).toBeNull();
  });
});

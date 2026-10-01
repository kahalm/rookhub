import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { PuzzleTagsComponent } from './puzzle-tags.component';
import { coarseRuleFor } from '../../testing/coarse-pointer-rules';

describe('PuzzleTagsComponent', () => {
  it('creates (template AOT-compiles + DI resolves)', async () => {
    await TestBed.configureTestingModule({
      imports: [PuzzleTagsComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(PuzzleTagsComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  // Codereview UX-007: „Tags anzeigen" war nur ein 0.8em-Textspan — am Handy kein Touch-Ziel.
  it('„Tags anzeigen" ist bei grobem Zeiger mindestens 44 px hoch', async () => {
    await TestBed.configureTestingModule({
      imports: [PuzzleTagsComponent],
      providers: [provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    const fixture = TestBed.createComponent(PuzzleTagsComponent);
    fixture.componentRef.setInput('tags', 'fork pin');
    fixture.detectChanges();   // erzeugt die Komponente → ihre Stile hängen im Dokument
    expect(fixture.nativeElement.querySelector('.puzzle-tags-toggle')).not.toBeNull();
    expect(coarseRuleFor('.puzzle-tags-toggle')?.style.minHeight).toBe('44px');
  });
});

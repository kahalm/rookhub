import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { By } from '@angular/platform-browser';
import { AnalysisBoardComponent } from './analysis-board.component';
import {
  BoardFullscreenButtonComponent,
} from '../../shared/fullscreen/board-fullscreen-button.component';

describe('AnalysisBoardComponent', () => {
  it('creates (template AOT-compiles + DI resolves)', async () => {
    await TestBed.configureTestingModule({
      imports: [AnalysisBoardComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(AnalysisBoardComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('schickt die äußere Hülle ins Vollbild und hängt den Knopf ans Brett', async () => {
    await TestBed.configureTestingModule({
      imports: [AnalysisBoardComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(AnalysisBoardComponent);
    fixture.detectChanges();

    const target: HTMLElement =
      fixture.debugElement.query(By.directive(BoardFullscreenButtonComponent)).componentInstance.target;
    expect(target.classList).toContain('ab-fs-host');
    expect(fixture.nativeElement.querySelector('.ab-fs-host .board-fs-btn')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.ab-wrap .board-fs-btn')).toBeNull();
  });
});

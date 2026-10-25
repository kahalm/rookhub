import { TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { SimilarPositionsPanelComponent } from './similar-positions-panel.component';
import { SimilarPositionsComponent } from './similar-positions.component';
import { By } from '@angular/platform-browser';

describe('SimilarPositionsPanelComponent', () => {
  async function render(inputs: Record<string, unknown> = {}) {
    await TestBed.configureTestingModule({
      imports: [SimilarPositionsPanelComponent],
      providers: [provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    const fixture = TestBed.createComponent(SimilarPositionsPanelComponent);
    for (const [k, v] of Object.entries(inputs)) fixture.componentRef.setInput(k, v);
    fixture.detectChanges();
    return fixture;
  }

  it('reicht die Werte an app-similar-positions durch', async () => {
    const selected = new Set([7]);
    const fixture = await render({ options: [{ id: 7, name: 'A' }], selected, preset: 'struktur', includeMirrored: false, moveText: 'Nc6' });
    const child = fixture.debugElement.query(By.directive(SimilarPositionsComponent)).componentInstance as SimilarPositionsComponent;
    expect(child.options).toEqual([{ id: 7, name: 'A' }]);
    expect(child.selected).toBe(selected);
    expect(child.preset).toBe('struktur');
    expect(child.includeMirrored).toBeFalse();
    expect(child.moveText).toBe('Nc6');
  });

  it('Hinweis auf das Zeitbudget nur bei truncated', async () => {
    const fixture = await render();
    expect(fixture.nativeElement.querySelector('.pr-note')).toBeNull();
    fixture.componentRef.setInput('truncated', true);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.pr-note').textContent).toContain('positionInReps.truncated');
  });

  it('leitet die Bedienung als Ausgaben weiter', async () => {
    const fixture = await render();
    const c = fixture.componentInstance;
    const child = fixture.debugElement.query(By.directive(SimilarPositionsComponent)).componentInstance as SimilarPositionsComponent;
    const seen: unknown[] = [];
    c.presetChange.subscribe(v => seen.push(['preset', v]));
    c.mirroredChange.subscribe(v => seen.push(['mirrored', v]));
    c.sameSideToMoveChange.subscribe(v => seen.push(['side', v]));
    c.moveTextChange.subscribe(v => seen.push(['move', v]));
    c.onlyWithMoveChange.subscribe(v => seen.push(['only', v]));
    c.selectionChange.subscribe(v => seen.push(['sel', v]));
    c.openMatch.subscribe(v => seen.push(['open', (v as any).gameIndex]));
    child.presetChange.emit('stellungsbild');
    child.mirroredChange.emit(false);
    child.sameSideToMoveChange.emit(true);
    child.moveTextChange.emit('e5');
    child.onlyWithMoveChange.emit(true);
    child.selectionChange.emit([9]);
    child.open.emit({ gameIndex: 3 } as any);
    expect(seen).toEqual([['preset', 'stellungsbild'], ['mirrored', false], ['side', true], ['move', 'e5'], ['only', true], ['sel', [9]], ['open', 3]]);
  });
});

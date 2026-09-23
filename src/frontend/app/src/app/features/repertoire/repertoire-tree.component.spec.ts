import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { RepertoireTreeComponent } from './repertoire-tree.component';
import { MoveTreeService } from './move-tree.service';

describe('RepertoireTreeComponent', () => {
  it('creates (template AOT-compiles + DI resolves)', async () => {
    await TestBed.configureTestingModule({
      imports: [RepertoireTreeComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(RepertoireTreeComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  async function render(frequencies: any) {
    await TestBed.configureTestingModule({
      imports: [RepertoireTreeComponent],
      providers: [provideRouter([]), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    const tree = new MoveTreeService();
    tree.buildTree('[Event "a"]\n\n1. e4 e5 *\n\n[Event "b"]\n\n1. d4 d5 *\n');
    const fixture = TestBed.createComponent(RepertoireTreeComponent);
    fixture.componentRef.setInput('children', tree.children);
    fixture.componentRef.setInput('frequencies', frequencies);
    fixture.detectChanges();
    return fixture;
  }

  it('shows the frequency of the position after each move, where the hole search knows it', async () => {
    const fixture = await render({
      savedAt: '2026-09-23T10:00:00Z', source: 'local', database: 'masters', ratings: [], speeds: [], complete: true,
      positions: { 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq': 0.6 },
    });
    const items: HTMLElement[] = Array.from(fixture.nativeElement.querySelectorAll('.child-item'));
    const e4 = items.find(i => i.textContent!.includes('e4'))!;
    const d4 = items.find(i => i.textContent!.includes('d4'))!;
    expect(e4.querySelector('.child-freq')!.textContent).toContain('60');
    expect(d4.querySelector('.child-freq')).toBeNull();
    expect(fixture.nativeElement.querySelector('.freq-source').textContent).toContain('repertoire.tree.freqFrom');
  });

  it('without a hole search it says where the numbers would come from', async () => {
    const fixture = await render(null);
    expect(fixture.nativeElement.querySelector('.child-freq')).toBeNull();
    expect(fixture.nativeElement.querySelector('.freq-source').textContent).toContain('repertoire.tree.freqHint');
  });
});

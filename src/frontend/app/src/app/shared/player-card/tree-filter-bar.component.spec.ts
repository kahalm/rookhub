import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TreeFilter } from '@lh/core/league.models';
import { TreeFilterBarComponent } from './tree-filter-bar.component';

const BOARD: TreeFilter = { source: 'board', speeds: [], years: null, withUnsure: false };

describe('TreeFilterBarComponent', () => {
  let fixture: ComponentFixture<TreeFilterBarComponent>;
  let emitted: TreeFilter[];

  function create(inputs: Record<string, unknown>): HTMLElement {
    TestBed.configureTestingModule({ imports: [TreeFilterBarComponent] });
    fixture = TestBed.createComponent(TreeFilterBarComponent);
    for (const [k, v] of Object.entries({ filter: BOARD, active: BOARD, ...inputs })) fixture.componentRef.setInput(k, v);
    emitted = [];
    fixture.componentInstance.changed.subscribe(f => emitted.push(f));
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('ohne Online-Partien keine Quellen-Wahl, nur der Zeitraum', () => {
    const el = create({ boardGames: 12, onlineGames: 0 });
    expect(el.querySelector('.seg')).toBeNull();
    const sel = el.querySelector('select')!;
    sel.value = '3';
    sel.dispatchEvent(new Event('change'));
    expect(emitted).toEqual([{ ...BOARD, years: 3 }]);
  });

  it('Online dazu: Quellen, Zeitformat und unsichere Konten', () => {
    const both: TreeFilter = { ...BOARD, source: 'both' };
    const el = create({ filter: both, active: both, boardGames: 4, onlineGames: 30, unsureGames: 7 });
    expect(Array.from(el.querySelectorAll('.seg button')).map(b => b.textContent?.trim())).toEqual(['Brett', 'Brett + online', 'Online']);
    (Array.from(el.querySelectorAll<HTMLButtonElement>('.chips button')).find(b => b.textContent?.trim() === 'Blitz')!).click();
    expect(emitted.at(-1)).toEqual({ ...both, speeds: ['blitz'] });
    expect(el.querySelector('.check')?.textContent).toContain('auch unsichere Konten (7 Partien)');
    const box = el.querySelector<HTMLInputElement>('.check input')!;
    box.checked = true;
    box.dispatchEvent(new Event('change'));
    expect(emitted.at(-1)).toEqual({ ...both, withUnsure: true });
  });

  it('nur Online-Konten: „Brett" gesperrt; über einen Teilen-Link kein Schalter für unsichere', () => {
    const online: TreeFilter = { ...BOARD, source: 'online' };
    const el = create({ active: online, boardGames: 0, onlineGames: 8, token: 'TOK' });
    expect(el.querySelector<HTMLButtonElement>('.seg button')!.disabled).toBeTrue();
    expect(el.querySelector('.chips')).not.toBeNull();
    expect(el.querySelector('.check')).toBeNull();
  });

  it('ohne Recht dazu (Spielervorbereitung ohne prep.manage) kein Schalter für unsichere Konten', () => {
    const both: TreeFilter = { ...BOARD, source: 'both' };
    const el = create({ filter: both, active: both, boardGames: 4, onlineGames: 30, unsureGames: 7, unsure: false });
    expect(el.querySelector('.chips')).not.toBeNull();
    expect(el.querySelector('.check')).toBeNull();
  });
});

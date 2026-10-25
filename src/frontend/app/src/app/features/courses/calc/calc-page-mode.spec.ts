import { CalcPageMode } from './calc-page-mode';

describe('CalcPageMode', () => {
  afterEach(() => {
    document.body.classList.remove(CalcPageMode.MODE_CLASS);
    document.body.style.removeProperty('--calc-nav-w');
    document.querySelectorAll('.calc-page-mode-spec').forEach(e => e.remove());
  });

  it('setzt die Modus-Klasse und misst die Navbar; leave räumt beides ab', () => {
    const nav = document.createElement('app-navbar');
    nav.className = 'calc-page-mode-spec';
    nav.style.cssText = 'display:block;width:321.4px';
    document.body.appendChild(nav);
    const mode = new CalcPageMode();
    mode.enter();
    expect(document.body.classList).toContain(CalcPageMode.MODE_CLASS);
    expect(document.body.style.getPropertyValue('--calc-nav-w')).toBe('322px');
    mode.leave();
    expect(document.body.classList).not.toContain(CalcPageMode.MODE_CLASS);
    expect(document.body.style.getPropertyValue('--calc-nav-w')).toBe('');
  });

  it('merkt sich das Vollbild-Ziel, sucht aber neu, sobald es nicht mehr im DOM hängt', () => {
    const make = () => {
      const board = document.createElement('app-puzzle-board');
      board.className = 'calc-page-mode-spec';
      board.innerHTML = '<div class="board-fs-host"></div>';
      document.body.appendChild(board);
      return board;
    };
    const mode = new CalcPageMode();
    const first = make();
    const target = mode.fsTarget;
    expect(target).toBe(first.querySelector('.board-fs-host') as HTMLElement);
    first.remove();
    const second = make();
    expect(mode.fsTarget).toBe(second.querySelector('.board-fs-host') as HTMLElement);
  });
});

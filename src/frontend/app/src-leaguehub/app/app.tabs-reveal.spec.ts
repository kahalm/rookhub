import { LeagueHubAppComponent } from './app.component';

// UI-Sweep 2026-10-10 (l-nav-mobile): am Handy scrollt die Reiterzeile waagrecht — der aktive Reiter darf nicht
// angeschnitten am rechten Rand stehen. Gescrollt wird nur die Reiterzeile, nie die Seite.
describe('LeagueHub: aktiver Reiter wird sichtbar', () => {
  it('scrollt die Reiterzeile, bis der aktive Reiter ganz zu sehen ist', () => {
    const host = document.createElement('div');
    host.innerHTML = `<nav class="tabs" style="display:flex;flex-wrap:nowrap;width:200px;overflow-x:auto;position:relative">
      ${[1, 2, 3, 4, 5].map(i => `<a style="flex:0 0 120px;display:block" class="${i === 4 ? 'on' : ''}">Reiter ${i}</a>`).join('')}</nav>`;
    document.body.appendChild(host);
    const bar = host.querySelector('nav') as HTMLElement;
    const fake = { host: { nativeElement: host } } as unknown as LeagueHubAppComponent;
    LeagueHubAppComponent.prototype.revealActiveTab.call(fake);
    const on = host.querySelector('a.on') as HTMLElement;
    expect(bar.scrollLeft).toBeGreaterThan(0);
    expect(on.getBoundingClientRect().right - bar.getBoundingClientRect().left).toBeLessThanOrEqual(bar.clientWidth);
    expect(window.scrollX).toBe(0);
    host.remove();
  });
});

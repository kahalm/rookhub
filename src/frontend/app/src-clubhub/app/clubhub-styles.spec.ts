/**
 * Regeln aus clubhub.scss (das Karma für ClubHub mitlädt), die an keiner eigenen Komponente hängen: die Karteireiter
 * der App-Hülle, RookHubs Anmelde-Maske in ClubHub-Farben und der Abstand über der klebenden Speichern-Leiste.
 * Geprüft wird an nachgebautem Markup — dieselben Klassen wie in den Vorlagen.
 */
describe('clubhub.scss', () => {
  let host: HTMLElement;

  function mount(html: string): HTMLElement {
    host = document.createElement('div');
    host.innerHTML = html;
    document.body.appendChild(host);
    return host;
  }

  afterEach(() => host?.remove());

  const rootVar = (name: string) => getComputedStyle(document.documentElement).getPropertyValue(name).trim();

  it('Karteireiter: aktiver und inaktiver haben dieselbe Schrift, dieselben Polster und dieselbe Grundlinie', () => {
    const el = mount(`<header class="top"><nav class="wrap tabs"><a class="on">Kartei</a><a>Gruppen</a></nav></header>`);
    const [on, off] = Array.from(el.querySelectorAll<HTMLElement>('.tabs a'));
    const style = (a: HTMLElement) => {
      const s = getComputedStyle(a);
      return [s.fontSize, s.fontWeight, s.fontFamily, s.paddingTop, s.paddingBottom, s.lineHeight];
    };
    expect(style(on)).toEqual(style(off));
    const textBottom = (a: HTMLElement) => {
      const range = document.createRange();
      range.selectNodeContents(a.firstChild!);
      return Math.round(range.getBoundingClientRect().bottom);
    };
    expect(textBottom(on)).toBe(textBottom(off));                                       // vorher 3 px höher
    expect(getComputedStyle(on).backgroundColor).not.toBe(getComputedStyle(off).backgroundColor);
  });

  it('Anmelde-Maske: Karte, Knopf und Fokus in ClubHub-Farben, Überschrift in Zilla Slab', () => {
    const el = mount(`<app-login><div class="auth-container"><div class="mat-mdc-card">
      <p class="auth-required">Hinweis</p></div></div></app-login>`);
    const card = el.querySelector<HTMLElement>('.mat-mdc-card')!;
    const cs = getComputedStyle(card);
    expect(cs.getPropertyValue('--mat-sys-primary').trim()).toBe(rootVar('--primary'));
    expect(cs.getPropertyValue('--mat-sys-on-primary').trim()).toBe(rootVar('--primary-ink'));
    expect(cs.getPropertyValue('--mat-card-elevated-container-color').trim()).toBe(rootVar('--card'));
    expect(cs.getPropertyValue('--mat-card-title-text-font')).toContain('Zilla Slab');
    expect(cs.getPropertyValue('--mat-button-protected-container-shape').trim()).toBe('8px');
    expect(cs.borderTopStyle).toBe('solid');
    // Der Hinweis-Kasten trägt die Primärfarbe als Kante statt Hellblau.
    const probe = document.createElement('span');
    probe.style.color = 'var(--primary)';
    card.appendChild(probe);
    expect(getComputedStyle(card.querySelector('.auth-required')!).borderLeftColor).toBe(getComputedStyle(probe).color);
  });

  it('Anmelde-Maske außerhalb der Anmeldung bleibt unberührt', () => {
    const el = mount(`<div class="mat-mdc-card"></div>`);
    expect(getComputedStyle(el.querySelector('.mat-mdc-card')!).getPropertyValue('--mat-card-title-text-font').trim()).toBe('');
  });

  it('mit klebender Speichern-Leiste scrollt ein fokussiertes Feld über die Leiste statt darunter', () => {
    mount(`<form class="sheet form"><input><div class="actions form-save"><button class="btn primary">Kind anlegen</button></div></form>`);
    const pad = parseFloat(getComputedStyle(document.documentElement).scrollPaddingBottom);
    const bar = host.querySelector<HTMLElement>('.form-save')!;
    expect(pad).toBeGreaterThanOrEqual(bar.getBoundingClientRect().height);
  });
});

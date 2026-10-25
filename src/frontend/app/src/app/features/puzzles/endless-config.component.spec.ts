import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { EndlessConfigComponent } from './endless-config.component';
import { EndlessConfig } from './endless-storage.service';
import { EndlessFasttrackState } from './endless-fasttrack-state';
import { ThemePreset } from './puzzle-theme-presets';

/** Start-Bildschirm des Endlos-Modus (Zerlegung Z19); die Themen-Fälle stammen aus endless-puzzle.component.spec.ts. */
describe('EndlessConfigComponent', () => {
  function create(config: Partial<EndlessConfig> = {}, render = false) {
    TestBed.configureTestingModule({
      imports: [EndlessConfigComponent],
      providers: [provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' })],
    });
    const fixture = TestBed.createComponent(EndlessConfigComponent);
    const cfg: EndlessConfig = { startElo: 700, themes: '', stockfishDepth: 16, ...config };
    const fasttrack = new EndlessFasttrackState();
    fasttrack.compute(cfg, []);
    fixture.componentRef.setInput('config', cfg);
    fixture.componentRef.setInput('themes', cfg.themes);
    fixture.componentRef.setInput('fasttrack', fasttrack);
    fixture.componentRef.setInput('allThemes', ['advancedPawn', 'backRankMate', 'endgame', 'fork', 'pin']);
    if (render) fixture.detectChanges();
    const c = fixture.componentInstance as any;
    let saves = 0;
    c.configSave.subscribe(() => saves++);
    return { fixture, c, cfg, fasttrack, saves: () => saves, el: fixture.nativeElement as HTMLElement };
  }

  describe('Themen-Multiselect', () => {
    it('selectedThemes liest die leerzeichengetrennten Themen aus der Config', () => {
      const { c } = create({ themes: 'fork pin' });
      expect(c.selectedThemes).toEqual(['fork', 'pin']);
    });

    it('addThemeValue hängt ein Thema an und schreibt es als String zurück (keine Duplikate)', () => {
      const { c, cfg, saves } = create({ themes: 'fork' });
      c.addThemeValue('pin');
      expect(cfg.themes).toBe('fork pin');
      c.addThemeValue('pin');   // Duplikat ignoriert
      expect(cfg.themes).toBe('fork pin');
      expect(saves()).toBe(1);  // gespeichert wird nur die echte Änderung
    });

    it('removeTheme entfernt das Thema aus dem String', () => {
      const { c, cfg } = create({ themes: 'fork pin endgame' });
      c.removeTheme('pin');
      expect(cfg.themes).toBe('fork endgame');
    });

    it('filteredThemes blendet bereits gewählte aus und filtert nach dem Suchtext', () => {
      const { c } = create({ themes: 'fork' });   // fork ist gewählt → raus aus Vorschlägen
      c.themeInput = 'ba';                        // Suchtext
      expect(c.filteredThemes).toEqual(['backRankMate']);
    });

    it('onThemeInputTokenEnd übernimmt frei getippte Themen und leert das Eingabefeld', () => {
      const { c, cfg } = create();
      const clear = jasmine.createSpy('clear');
      c.onThemeInputTokenEnd({ value: 'zwischenzug', chipInput: { clear } });
      expect(cfg.themes).toBe('zwischenzug');
      expect(clear).toHaveBeenCalled();
      expect(c.themeInput).toBe('');
    });

    it('applyThemePreset setzt das Bündel und deaktiviert „schwächste Themen"', () => {
      const { c, cfg } = create({ worstTags: true });
      const preset = c.themePresets.find((p: ThemePreset) => p.labelKey === 'endless.themePreset.basicTactics')!;
      c.applyThemePreset(preset);
      expect(cfg.worstTags).toBe(false);
      expect(c.selectedThemes).toEqual(preset.themes);
      expect(c.isThemePresetActive(preset)).toBe(true);
    });

    it('isThemePresetActive ist false, sobald die Themen abweichen oder worstTags aktiv ist', () => {
      const { c, cfg } = create();
      const preset = c.themePresets[0];
      c.applyThemePreset(preset);
      expect(c.isThemePresetActive(preset)).toBe(true);
      c.addThemeValue('fork');               // Auswahl weicht ab
      expect(c.isThemePresetActive(preset)).toBe(false);
      c.applyThemePreset(preset);
      cfg.worstTags = true;
      expect(c.isThemePresetActive(preset)).toBe(false);
    });

    it('ein aktiver Preset wird beim zweiten Klick abgewählt; „schwächste Themen“ aus und Klick = Preset setzen', () => {
      const { c, cfg } = create();
      const preset = c.themePresets[0];
      c.applyThemePreset(preset);
      c.applyThemePreset(preset);
      expect(cfg.themes).toBe('');
      cfg.themes = preset.themes.join(' '); cfg.worstTags = true;
      c.applyThemePreset(preset);            // worstTags wird ZUERST abgeschaltet → Preset gilt als aktiv → abwählen
      expect(cfg.worstTags).toBe(false);
      expect(cfg.themes).toBe('');
    });
  });

  it('chainPreview: Marken sortiert, dedupliziert und auf den 30er-Block geklemmt', () => {
    const { fixture, c } = create({ startElo: 700 });
    fixture.componentRef.setInput('isFirstRun', true);   // erster Lauf → Anker 15/30
    const first = c.chainPreview;
    expect(first.map((p: any) => p.puzzle)).toEqual([1, 16, 30]);   // vorher: 1, 16, 31, 30
    // Puzzle-Nummern streng aufsteigend, letzte = Blockende
    expect(first[first.length - 1].puzzle).toBe(30);
    for (let i = 1; i < first.length; i++) expect(first[i].puzzle).toBeGreaterThan(first[i - 1].puzzle);
  });

  it('OnPush: asynchron nachgeladene Themen („schwächste Themen“) erscheinen über den themes-Input', () => {
    const { fixture, cfg, el } = create({}, true);
    expect(el.querySelectorAll('mat-chip-row').length).toBe(0);
    cfg.themes = 'fork pin';                         // so setzt der Container die geladenen Themen
    fixture.componentRef.setInput('themes', cfg.themes);
    fixture.detectChanges();
    expect(el.querySelectorAll('mat-chip-row').length).toBe(2);
  });

  it('Knöpfe melden sich als Outputs (Start, Hilfe, Einstellungen, Feineinstellungen)', () => {
    const { fixture, c, el } = create({}, true);
    const got: string[] = [];
    c.start.subscribe(() => got.push('start'));
    c.help.subscribe(() => got.push('help'));
    c.settings.subscribe(() => got.push('settings'));
    c.showAdvanced.subscribe((v: boolean) => got.push('adv:' + v));
    (el.querySelector('.start-btn-top') as HTMLButtonElement).click();
    (el.querySelector('.help-btn') as HTMLButtonElement).click();
    (el.querySelector('.settings-gear') as HTMLButtonElement).click();
    (el.querySelector('.adv-toggle') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(got).toEqual(['start', 'help', 'settings', 'adv:true']);
    expect(el.querySelector('.adv-fields')).not.toBeNull();
  });
});

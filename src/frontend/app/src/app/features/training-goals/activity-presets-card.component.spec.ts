import { of } from 'rxjs';
import { ActivityPresetsCardComponent } from './activity-presets-card.component';

function make() {
  const service = {
    listPresets: jasmine.createSpy('listPresets').and.returnValue(of([])),
    addPreset: jasmine.createSpy('addPreset').and.returnValue(of({ id: 1, label: 'X', kind: 'OfflineStudy', theme: null })),
    updatePreset: jasmine.createSpy('updatePreset').and.returnValue(of({ id: 1, label: 'Y', kind: 'OfflineStudy', theme: null })),
    deletePreset: jasmine.createSpy('deletePreset').and.returnValue(of({})),
  } as any;
  const snackbar = { info: () => {} } as any;
  const translate = { instant: (k: string) => k } as any;
  return { c: new ActivityPresetsCardComponent(service, snackbar, translate), service };
}

describe('ActivityPresetsCardComponent', () => {
  it('loads presets on init', () => {
    const { c, service } = make();
    c.ngOnInit();
    expect(service.listPresets).toHaveBeenCalled();
  });

  it('savePreset ignores an empty label', () => {
    const { c, service } = make();
    c.presetEdit = { label: '   ', kind: 'OfflineStudy', theme: null };
    c.savePreset();
    expect(service.addPreset).not.toHaveBeenCalled();
  });

  it('savePreset appends the created preset and resets the form', () => {
    const { c, service } = make();
    c.presetEdit = { label: 'Taktik 15m', kind: 'OfflinePuzzle', theme: null };
    c.savePreset();
    expect(service.addPreset).toHaveBeenCalled();
    expect(c.presets.length).toBe(1);
    expect(c.editingPresetId).toBeNull();
    expect(c.presetEdit.label).toBe('');
  });
});

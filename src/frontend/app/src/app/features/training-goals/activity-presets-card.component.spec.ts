import { of, throwError } from 'rxjs';
import { ActivityPresetsCardComponent } from './activity-presets-card.component';

function make() {
  const service = {
    listPresets: jasmine.createSpy('listPresets').and.returnValue(of([])),
    addPreset: jasmine.createSpy('addPreset').and.returnValue(of({ id: 1, label: 'X', kind: 'OfflineStudy', theme: null })),
    updatePreset: jasmine.createSpy('updatePreset').and.returnValue(of({ id: 1, label: 'Y', kind: 'OfflineStudy', theme: null })),
    deletePreset: jasmine.createSpy('deletePreset').and.returnValue(of({})),
  } as any;
  const snackbar = { info: jasmine.createSpy('info') } as any;
  const translate = { instant: (k: string) => k } as any;
  return { c: new ActivityPresetsCardComponent(service, snackbar, translate), service, snackbar };
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

  // Codereview A10-006: der Server antwortet einheitlich mit { message } (vorher { error }).
  it('savePreset shows the server message of a 400', () => {
    const { c, service, snackbar } = make();
    service.addPreset.and.returnValue(throwError(() => ({ status: 400, error: { message: 'Label too long.' } })));
    c.presetEdit = { label: 'Taktik', kind: 'OfflinePuzzle', theme: null };
    c.savePreset();
    expect(snackbar.info).toHaveBeenCalledWith('Label too long.', jasmine.anything());
    expect(c.savingPreset).toBeFalse();
  });
});

import { of } from 'rxjs';
import { ManualActivitiesCardComponent } from './manual-activities-card.component';

function make() {
  const service = {
    addManual: jasmine.createSpy('addManual').and.returnValue(of({})),
    updateManual: jasmine.createSpy('updateManual').and.returnValue(of({})),
    deleteManual: jasmine.createSpy('deleteManual').and.returnValue(of({})),
  } as any;
  const snackbar = { success: () => {}, warn: () => {} } as any;
  const translate = { instant: (k: string) => k } as any;
  return { c: new ManualActivitiesCardComponent(service, snackbar, translate), service };
}

describe('ManualActivitiesCardComponent', () => {
  it('manualMinutes is false for OtbGame, true otherwise', () => {
    const { c } = make();
    c.manualEdit.kind = 'OtbGame';
    expect(c.manualMinutes).toBeFalse();
    c.manualEdit.kind = 'OfflineStudy';
    expect(c.manualMinutes).toBeTrue();
  });

  it('saveManual adds and emits changed', () => {
    const { c, service } = make();
    const spy = jasmine.createSpy('changed');
    c.changed.subscribe(spy);
    c.manualEdit = { kind: 'OfflineStudy', date: '2026-07-02', amount: 30, note: '', theme: null };
    c.saveManual();
    expect(service.addManual).toHaveBeenCalled();
    expect(spy).toHaveBeenCalled();
  });

  it('editManual then cancel resets the form and edit id', () => {
    const { c } = make();
    c.editManual({ id: 5, kind: 'Coaching', date: '2026-07-01', amount: 60, note: 'x', theme: null } as any);
    expect(c.editingManualId).toBe(5);
    c.cancelManualEdit();
    expect(c.editingManualId).toBeNull();
    expect(c.manualEdit.kind).toBe('OtbGame');
  });
});

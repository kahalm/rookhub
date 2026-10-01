import { of } from 'rxjs';
import { ManualActivitiesCardComponent } from './manual-activities-card.component';

function make(confirmAnswer = true) {
  const service = {
    addManual: jasmine.createSpy('addManual').and.returnValue(of({})),
    updateManual: jasmine.createSpy('updateManual').and.returnValue(of({})),
    deleteManual: jasmine.createSpy('deleteManual').and.returnValue(of({})),
  } as any;
  const snackbar = { success: () => {}, warn: () => {} } as any;
  const translate = { instant: (k: string) => k } as any;
  const confirm = { ask: jasmine.createSpy('ask').and.returnValue(of(confirmAnswer)) } as any;
  return { c: new ManualActivitiesCardComponent(service, snackbar, translate, confirm), service, confirm };
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

  // F5-016: Löschen ist hart und ohne Rückgängig → erst nach Rückfrage.
  it('deleteManual: Abbrechen der Rückfrage löscht nichts', () => {
    const { c, service, confirm } = make(false);
    const spy = jasmine.createSpy('changed');
    c.changed.subscribe(spy);
    c.deleteManual({ id: 5, kind: 'OtbGame', date: '2026-07-01', amount: 90, note: null, theme: null } as any);
    expect(confirm.ask).toHaveBeenCalledWith('trainingGoals.manual.deleteConfirm',
      { kind: 'trainingGoals.manual.kinds.OtbGame', date: '2026-07-01' });
    expect(service.deleteManual).not.toHaveBeenCalled();
    expect(spy).not.toHaveBeenCalled();
  });

  it('deleteManual: nach Bestätigung gelöscht, laufende Bearbeitung beendet, changed gemeldet', () => {
    const { c, service } = make(true);
    const spy = jasmine.createSpy('changed');
    c.changed.subscribe(spy);
    const m = { id: 5, kind: 'Coaching', date: '2026-07-01', amount: 60, note: 'x', theme: null } as any;
    c.editManual(m);
    c.deleteManual(m);
    expect(service.deleteManual).toHaveBeenCalledWith(5);
    expect(c.editingManualId).toBeNull();
    expect(spy).toHaveBeenCalled();
  });
});

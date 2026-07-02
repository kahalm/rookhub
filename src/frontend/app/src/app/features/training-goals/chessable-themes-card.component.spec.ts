import { of } from 'rxjs';
import { ChessableThemesCardComponent } from './chessable-themes-card.component';

function make() {
  const service = {
    listChessableCourses: jasmine.createSpy('listChessableCourses').and.returnValue(of([
      { courseId: '111', courseName: 'Endgames', totalSeconds: 3600, assignedTheme: null, autoTheme: 'endgame', isAssigned: false },
    ])),
    setChessableCourseTheme: jasmine.createSpy('setChessableCourseTheme').and.returnValue(of({})),
    clearChessableCourseTheme: jasmine.createSpy('clearChessableCourseTheme').and.returnValue(of({})),
  } as any;
  const snackbar = { success: () => {}, warn: () => {} } as any;
  const translate = { instant: (k: string) => k, currentLang: 'en' } as any;
  return { c: new ChessableThemesCardComponent(service, snackbar, translate), service };
}

describe('ChessableThemesCardComponent', () => {
  it('loads courses on init', () => {
    const { c, service } = make();
    c.ngOnInit();
    expect(service.listChessableCourses).toHaveBeenCalledWith(false);
    expect(c.chessableCourses.length).toBe(1);
  });

  it('toggleUnassignedFilter reloads with the flag', () => {
    const { c, service } = make();
    c.toggleUnassignedFilter(true);
    expect(c.chessableUnassignedOnly).toBeTrue();
    expect(service.listChessableCourses).toHaveBeenCalledWith(true);
  });

  it('selectedTheme capitalizes the stored theme', () => {
    const { c } = make();
    expect(c.selectedTheme({ assignedTheme: 'opening' } as any)).toBe('Opening');
    expect(c.selectedTheme({ assignedTheme: null } as any)).toBeNull();
  });

  it('assignTheme sets a theme, reloads and emits changed', () => {
    const { c, service } = make();
    const emit = spyOn(c.changed, 'emit');
    c.assignTheme({ courseId: '111' } as any, 'Tactics');
    expect(service.setChessableCourseTheme).toHaveBeenCalledWith('111', 'Tactics');
    expect(c.savingCourseId).toBeNull();
    expect(service.listChessableCourses).toHaveBeenCalled();
    expect(emit).toHaveBeenCalled();
  });

  it('assignTheme with null clears the theme', () => {
    const { c, service } = make();
    c.assignTheme({ courseId: '222' } as any, null);
    expect(service.clearChessableCourseTheme).toHaveBeenCalledWith('222');
  });
});

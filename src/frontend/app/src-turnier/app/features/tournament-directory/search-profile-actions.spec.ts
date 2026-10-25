import { signal } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { TranslateService } from '@ngx-translate/core';
import { Observable, Subject, of, throwError } from 'rxjs';
import { SnackbarService } from '@rh/core/snackbar.service';
import { SearchProfileActions } from './search-profile-actions';
import { SearchProfileDialogComponent } from './search-profile-dialog.component';
import { SearchProfileService } from './search-profile.service';
import { SearchProfile } from './tournament-directory.model';

function profile(id: number, name: string): SearchProfile {
  return {
    id, name, placeQuery: null, lat: 47.27, lon: 11.39, radiusKm: 50, federations: [], speeds: [],
    weekendOnly: false, minPlayers: null, notifyNew: false, sortOrder: id,
  };
}

describe('SearchProfileActions', () => {
  let profiles: ReturnType<typeof signal<SearchProfile[]>>;
  let active: number | null;
  let selected: (number | null)[];
  let warnings: string[];
  let undo: Subject<void>;
  let shown: { message: string; opts: unknown }[];
  let dialogResult: SearchProfile | null;
  let dialogOpen: jasmine.Spy;
  let remove: (id: number) => Observable<void>;
  let create: (input: unknown) => Observable<SearchProfile>;
  let actions: SearchProfileActions;

  beforeEach(() => {
    profiles = signal([profile(1, 'Tirol'), profile(2, 'Wien'), profile(3, 'Graz')]);
    active = null;
    selected = [];
    warnings = [];
    shown = [];
    undo = new Subject<void>();
    dialogResult = null;
    remove = () => of(undefined);
    create = () => of(profile(9, 'Wien'));
    dialogOpen = jasmine.createSpy('open').and.callFake(() => ({ afterClosed: () => of(dialogResult) }));
    actions = new SearchProfileActions({
      dialog: { open: dialogOpen } as unknown as MatDialog,
      snackbar: {
        show: (message: string, opts: unknown) => { shown.push({ message, opts }); return { onAction: () => undo }; },
        warn: (message: string) => { warnings.push(message); return {}; },
      } as unknown as SnackbarService,
      translate: { instant: (key: string) => key } as unknown as TranslateService,
      service: {
        remove: (id: number) => remove(id),
        create: (input: unknown) => create(input),
      } as unknown as SearchProfileService,
      profiles,
      activeId: () => active,
      select: id => selected.push(id),
    });
  });

  it('öffnet den Dialog mit dem Profil und übernimmt ein neu gespeichertes', () => {
    dialogResult = profile(4, 'Linz');

    actions.open(null);

    expect(dialogOpen).toHaveBeenCalledWith(SearchProfileDialogComponent, { data: { profile: null }, width: '460px' });
    expect(profiles().map(p => p.name)).toEqual(['Tirol', 'Wien', 'Graz', 'Linz']);
    expect(selected).toEqual([4]);
  });

  it('ersetzt ein bearbeitetes Profil an seinem Platz und lässt bei Abbruch alles stehen', () => {
    dialogResult = { ...profile(2, 'Wien Umgebung') };
    actions.open(profile(2, 'Wien'));
    expect(profiles().map(p => p.name)).toEqual(['Tirol', 'Wien Umgebung', 'Graz']);

    dialogResult = null;
    actions.open(profile(1, 'Tirol'));
    expect(profiles().length).toBe(3);
    expect(selected).toEqual([2]);
  });

  it('löscht, wählt ein gelöschtes aktives Profil ab und bietet 6 s Rückgängig', () => {
    active = 2;

    actions.remove(profile(2, 'Wien'));

    expect(profiles().map(p => p.id)).toEqual([1, 3]);
    expect(selected).toEqual([null]);
    expect(shown).toEqual([{ message: 'tournamentDirectory.profile.deleted', opts: { action: 'common.undo', duration: 6000 } }]);
  });

  it('legt beim Rückgängig das Profil ohne alte Id an seinem alten Platz neu an und wählt es wieder', () => {
    active = 2;
    let input: unknown;
    create = i => { input = i; return of(profile(9, 'Wien')); };
    actions.remove(profile(2, 'Wien'));

    undo.next();

    expect(input).toEqual(jasmine.objectContaining({ name: 'Wien' }));
    expect(input).not.toEqual(jasmine.objectContaining({ id: jasmine.anything() }));
    expect(profiles().map(p => p.id)).toEqual([1, 9, 3]);
    expect(selected).toEqual([null, 9]);
  });

  it('wählt beim Rückgängig nur wieder, was vorher gewählt war', () => {
    actions.remove(profile(3, 'Graz'));
    undo.next();

    expect(profiles().map(p => p.id)).toEqual([1, 2, 9]);
    expect(selected).toEqual([]);
  });

  it('meldet gescheitertes Löschen und gescheitertes Rückgängig', () => {
    remove = () => throwError(() => new Error('500'));
    actions.remove(profile(1, 'Tirol'));
    expect(warnings).toEqual(['tournamentDirectory.profile.deleteError']);
    expect(profiles().length).toBe(3);

    remove = () => of(undefined);
    create = () => throwError(() => new Error('500'));
    actions.remove(profile(1, 'Tirol'));
    undo.next();
    expect(warnings).toEqual(['tournamentDirectory.profile.deleteError', 'tournamentDirectory.profile.undoFailed']);
  });
});

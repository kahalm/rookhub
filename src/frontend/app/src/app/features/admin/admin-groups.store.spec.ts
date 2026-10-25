import { TestBed } from '@angular/core/testing';
import { TranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';
import { AdminService } from '../../core/admin.service';
import { SnackbarService } from '../../core/snackbar.service';
import { AdminGroupsStore } from './admin-groups.store';

describe('AdminGroupsStore', () => {
  function make(admin: any) {
    const snackbar = { info: jasmine.createSpy('info') };
    TestBed.configureTestingModule({
      providers: [
        AdminGroupsStore,
        { provide: AdminService, useValue: admin },
        { provide: SnackbarService, useValue: snackbar },
        { provide: TranslateService, useValue: { instant: (k: string) => k } },
      ],
    });
    return { store: TestBed.inject(AdminGroupsStore), snackbar };
  }

  it('keptTabGroups ist null, solange nie geladen wurde (Menü kürzt dann nichts, F5-020)', () => {
    const { store } = make({});
    expect(store.keptTabGroups()).toBeNull();
  });

  it('loadGroups füllt volle Liste UND Auswahl', () => {
    const groups = [{ id: 1, name: 'A', memberCount: 2 }];
    const { store } = make({ getGroups: () => of(groups) });
    store.loadGroups();
    expect(store.groups()).toBe(groups as any);
    expect(store.groupOptions()).toBe(groups as any);
    expect(store.keptTabGroups()).toBe(groups as any);
    expect(store.groupsLoaded()).toBeTrue();
    expect(store.groupsLoading()).toBeFalse();
  });

  it('loadGroupOptions füllt nur die Auswahl (ohne groups.manage, F5-005-Nachlauf)', () => {
    const options = [{ id: 3, name: 'Verein', memberCount: 4 }];
    const { store } = make({ getGroupOptions: () => of(options) });
    store.loadGroupOptions();
    expect(store.groups()).toEqual([]);
    expect(store.keptTabGroups()).toBe(options);
  });

  it('Ladefehler melden sich und lassen die Auswahl ungeladen', () => {
    const { store, snackbar } = make({
      getGroups: () => throwError(() => ({ status: 500 })),
      getGroupOptions: () => throwError(() => ({ status: 500 })),
    });
    store.loadGroups();
    store.loadGroupOptions();
    expect(snackbar.info).toHaveBeenCalledWith('admin.groups.errors.load');
    expect(snackbar.info).toHaveBeenCalledTimes(2);
    expect(store.groupsLoading()).toBeFalse();
    expect(store.keptTabGroups()).toBeNull();
  });
});

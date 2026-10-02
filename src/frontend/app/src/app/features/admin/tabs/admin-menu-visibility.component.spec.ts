import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { AdminMenuVisibilityComponent } from './admin-menu-visibility.component';
import { Group, MenuItemConfig } from '../../../core/admin.service';

describe('AdminMenuVisibilityComponent', () => {
  it('creates (template AOT-compiles + DI resolves)', async () => {
    await TestBed.configureTestingModule({
      imports: [AdminMenuVisibilityComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(AdminMenuVisibilityComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  // F5-020: Der Tab bleibt nach dem ersten Öffnen bestehen, die Gruppenliste kommt von der Admin-Seite herein. Eine im
  // Gruppen-Tab gelöschte Gruppe darf nicht mehr mitgeschickt werden (der Server lehnte sie per Fremdschlüssel ab → 500).
  describe('Speichern mit der Gruppenliste der Admin-Seite (F5-020)', () => {
    function make(groups: Group[] | null) {
      const admin = {
        getGroups: jasmine.createSpy('getGroups').and.returnValue(of([])),
        saveMenuConfig: jasmine.createSpy('saveMenuConfig').and.callFake((items: MenuItemConfig[]) => of(items)),
      };
      const c = new AdminMenuVisibilityComponent(
        admin as any, { refresh: () => {} } as any, { info: () => {} } as any, { instant: (k: string) => k } as any);
      c.groups = groups;
      c.menuConfig = [
        { key: 'courses', level: 'Groups', groupIds: [1, 2] },
        { key: 'weekly', level: 'Admin', groupIds: [1] },
      ];
      return { c, admin };
    }

    it('kürzt groupIds auf die vorhandenen Gruppen', () => {
      const { c, admin } = make([{ id: 1, name: 'A' }, { id: 3, name: 'C' }] as Group[]);
      c.saveMenuConfig();
      expect(admin.saveMenuConfig).toHaveBeenCalledWith([
        { key: 'courses', level: 'Groups', groupIds: [1] },
        { key: 'weekly', level: 'Admin', groupIds: [] },
      ]);
      expect(admin.getGroups).not.toHaveBeenCalled();
    });

    it('lässt die Auswahl stehen, solange die Gruppenliste nie geladen wurde (leer hieße sonst: alle gelöscht)', () => {
      const { c, admin } = make(null);
      c.saveMenuConfig();
      expect(admin.saveMenuConfig.calls.mostRecent().args[0][0].groupIds).toEqual([1, 2]);
    });
  });
});

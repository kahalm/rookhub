import { Injectable, computed, inject, signal } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';
import { AdminService, Group, GroupOption } from '../../core/admin.service';
import { SnackbarService } from '../../core/snackbar.service';

/**
 * Gruppenliste der Admin-Seite — EINE Quelle für den Gruppen-Tab (volle Liste), die Bücher-Freigaben und die behaltenen
 * Tabs Menü und Rollen (Auswahl, F5-020). Je Seite eine Instanz (`providers` von `AdminComponent`): der Gruppen-Tab
 * erneuert sie nach jedem Anlegen/Löschen/Aufnehmen über {@link loadGroups}, alle anderen lesen sie nur.
 * Aus `AdminComponent` herausgelöst (Zerlegung Z30), Verhalten unverändert.
 */
@Injectable()
export class AdminGroupsStore {
  private readonly adminService = inject(AdminService);
  private readonly snackbar = inject(SnackbarService);
  private readonly translate = inject(TranslateService);

  /** Volle Gruppenliste (nur mit groups.manage geladen). */
  readonly groups = signal<Group[]>([]);
  readonly groupsLoading = signal(false);
  /** Gruppenliste mindestens einmal erfolgreich geladen. */
  readonly groupsLoaded = signal(false);
  /**
   * Gruppen als AUSWAHL (Bücher-Freigaben, Menü, Rollen): mit groups.manage dieselbe Liste wie {@link groups}, sonst die
   * schlanke `GET /api/admin/groups/options` (F5-005-Nachlauf) — eine Rolle mit nur menu.manage sah sonst keine Gruppen.
   */
  readonly groupOptions = signal<GroupOption[]>([]);
  /** Auswahl mindestens einmal erfolgreich geladen — siehe {@link keptTabGroups}. */
  readonly groupOptionsLoaded = signal(false);
  /**
   * Gruppenliste für die behaltenen Tabs Menü und Rollen (F5-020). `null`, solange die Liste nie geladen wurde: dann
   * kürzt der Menü-Tab beim Speichern nichts (eine leere Liste hieße sonst „alle Gruppen gelöscht").
   */
  readonly keptTabGroups = computed<GroupOption[] | null>(() => this.groupOptionsLoaded() ? this.groupOptions() : null);

  /** Nur Id + Name je Gruppe — für Rechteinhaber ohne groups.manage (F5-005-Nachlauf). */
  loadGroupOptions(): void {
    this.adminService.getGroupOptions().subscribe({
      next: options => {
        this.groupOptions.set(options);
        this.groupOptionsLoaded.set(true);
      },
      error: () => this.snackbar.info(this.translate.instant('admin.groups.errors.load')),
    });
  }

  loadGroups(): void {
    this.groupsLoading.set(true);
    this.adminService.getGroups().subscribe({
      next: groups => {
        this.groups.set(groups);
        this.groupsLoaded.set(true);
        this.groupOptions.set(groups);
        this.groupOptionsLoaded.set(true);
        this.groupsLoading.set(false);
      },
      error: () => {
        this.snackbar.info(this.translate.instant('admin.groups.errors.load'));
        this.groupsLoading.set(false);
      }
    });
  }
}

import { Component, OnInit, DestroyRef, inject, ChangeDetectionStrategy, computed, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTabsModule } from '@angular/material/tabs';
import { TranslatePipe } from '@ngx-translate/core';
import { Router, ActivatedRoute } from '@angular/router';
import { AdminService } from '../../core/admin.service';
import { AuthService } from '../../core/auth.service';
import { AdminGithubActionsComponent } from './admin-github-actions.component';
import { AdminChessableDownloadComponent } from './tabs/admin-chessable-download.component';
import { AdminDailyPuzzleComponent } from './tabs/admin-daily-puzzle.component';
import { AdminPuzzleTagsComponent } from './tabs/admin-puzzle-tags.component';
import { AdminMenuVisibilityComponent } from './tabs/admin-menu-visibility.component';
import { AdminMessagesComponent } from './tabs/admin-messages.component';
import { AdminRolesComponent } from './tabs/admin-roles.component';
import { AdminLeagueUploadsComponent } from './tabs/admin-league-uploads.component';
import { AdminBackgroundJobsComponent } from './tabs/admin-background-jobs.component';
import { AdminUsersTabComponent } from './tabs/admin-users-tab.component';
import { AdminBooksTabComponent } from './tabs/admin-books-tab.component';
import { AdminGroupsTabComponent } from './tabs/admin-groups-tab.component';
import { AdminGroupsStore } from './admin-groups.store';
import { adminTabIndex, AdminTabKey, visibleAdminTabs } from './admin-tabs';

/**
 * Admin-Seite: Kopf (Kibana-Link), Tab-Leiste je Recht (F5-005) und Deep-Links `?tab=<key>`. Jeder Tab ist eine eigene
 * Komponente in `tabs/` (Nutzer, Bücher, Gruppen seit der Zerlegung Z30); die Gruppenliste, die Gruppen-, Bücher-,
 * Menü- und Rollen-Tab teilen, hält der seiteneigene {@link AdminGroupsStore}.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-admin',
  standalone: true,
  imports: [
    CommonModule, MatButtonModule, MatIconModule, MatTabsModule, TranslatePipe,
    AdminUsersTabComponent, AdminBooksTabComponent, AdminGroupsTabComponent,
    AdminGithubActionsComponent, AdminChessableDownloadComponent,
    AdminDailyPuzzleComponent, AdminPuzzleTagsComponent, AdminMenuVisibilityComponent, AdminMessagesComponent,
    AdminRolesComponent,
    AdminLeagueUploadsComponent,
    AdminBackgroundJobsComponent,
  ],
  providers: [AdminGroupsStore],
  templateUrl: './admin.component.html',
  styleUrls: ['./admin.component.scss'],
})
export class AdminComponent implements OnInit {
  private readonly adminService = inject(AdminService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly groupsStore = inject(AdminGroupsStore);

  /** Kibana-URL aus dem Server-Env (leer = nicht konfiguriert → Link wird nicht angezeigt). */
  readonly kibanaUrl = signal('');

  /**
   * Die Tabs, die der Nutzer sieht (F5-005: je Recht, Zuordnung in `admin-tabs.ts`). Nur sie werden gerendert, die
   * mat-tab-Indizes zählen also über DIESE Liste — Deep-Links und `?tab=` gehen über den Key, nie über einen festen
   * Index. Ein Signal-`computed`: `AuthService.has` liest den Live-Stand der Rechte, ein Nachladen zieht nach.
   */
  readonly visibleTabs = computed(() => visibleAdminTabs(this.auth));
  /** Aktiver Tab als Key (für Deep-Links wie /admin?tab=messages); `null` = erster sichtbarer. */
  private readonly selectedTab = signal<AdminTabKey | null>(null);
  /** Index des aktiven Tabs unter den SICHTBAREN Tabs (für `[selectedIndex]`). */
  readonly selectedTabIndex = computed(() => Math.max(0, adminTabIndex(this.selectedTab(), this.visibleTabs())));
  /** Wird der Tab gerendert? */
  show(key: AdminTabKey): boolean { return this.visibleTabs().includes(key); }
  /**
   * Tabs mit eigenem, erst per Knopf gespeichertem Formular (Menü-Sichtbarkeit, Rollen): einmal geöffnet, bleiben sie
   * bestehen (F5-020). Als `matTabContent` wurden sie beim Verlassen zerstört und beim Zurückkehren neu geladen — ein
   * kurzer Blick in einen anderen Tab verwarf alle ungespeicherten Änderungen ohne Hinweis. Erst beim ersten Öffnen
   * gerendert (kein Laden beim Seitenaufruf), danach hält das `@if` im eigenen Template die Komponente am Leben.
   */
  readonly keptTabs = signal<ReadonlySet<AdminTabKey>>(new Set());
  private static readonly KeptTabKeys: readonly AdminTabKey[] = ['menu', 'roles'];
  /**
   * Gruppenliste für die behaltenen Tabs Menü und Rollen (F5-020): EINE Quelle ({@link AdminGroupsStore}), die der
   * Gruppen-Tab nach jedem Anlegen/Löschen erneuert. `null`, solange die Liste nie geladen wurde (siehe Store).
   */
  readonly keptTabGroups = this.groupsStore.keptTabGroups;

  /** Tabs, die Gruppen nur zur Auswahl brauchen (ohne groups.manage aus `loadGroupOptions`). */
  private static readonly GroupChoiceTabs: readonly AdminTabKey[] = ['books', 'menu', 'roles'];

  ngOnInit(): void {
    // Nur laden, was der Nutzer sehen darf (F5-005) — sonst hagelte es 403-Meldungen für fremde Tabs. Nutzer- und
    // Bücher-Tab laden selbst (sie werden nur mit ihrem Recht gerendert).
    // Gruppenliste: mit groups.manage die volle (Gruppen-Tab + Auswahl), sonst nur die Auswahl für Bücher/Menü/Rollen.
    if (this.auth.has('groups.manage')) this.groupsStore.loadGroups();
    else if (AdminComponent.GroupChoiceTabs.some(k => this.show(k))) this.groupsStore.loadGroupOptions();
    // Deep-Link: /admin?tab=<key> — Tab aus der URL wählen (der `?thread=`-Teil wird vom
    // Nachrichten-Tab selbst behandelt, siehe AdminMessagesComponent). Tabs ohne Recht fängt adminAreaGuard ab.
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(qp => {
      const key = qp.get('tab') as AdminTabKey | null;
      if (key && this.show(key)) this.selectTab(key);
    });

    if (this.auth.has('users.manage')) {
      this.adminService.getConfig().subscribe({
        next: cfg => { this.kibanaUrl.set(cfg.kibanaUrl || ''); },
        error: () => { /* still keine Pflicht — Link bleibt versteckt */ }
      });
    }
  }

  /** Tab-Wechsel: Index übernehmen UND als ?tab=<key> in die URL schreiben, damit Reload/Back
   *  den Tab behält (queryParamsHandling 'merge' lässt ?thread=… o. Ä. unberührt; replaceUrl
   *  vermeidet eine zusätzliche History-Position je Klick). */
  onTabChange(index: number): void {
    const tab = this.visibleTabs()[index];
    if (!tab) return;
    this.selectTab(tab);
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { tab },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  private selectTab(key: AdminTabKey): void {
    this.selectedTab.set(key);
    if (AdminComponent.KeptTabKeys.includes(key) && !this.keptTabs().has(key)) {
      this.keptTabs.update(s => new Set([...s, key]));
    }
  }
}

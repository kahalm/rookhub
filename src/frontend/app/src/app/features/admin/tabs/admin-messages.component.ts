import { Component, OnInit, DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { ActivatedRoute } from '@angular/router';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { Subject, of } from 'rxjs';
import { debounceTime, distinctUntilChanged, switchMap, catchError, tap } from 'rxjs/operators';
import { SnackbarService } from '../../../core/snackbar.service';
import { AuthService } from '../../../core/auth.service';
import { LoadingSpinnerComponent } from '../../../shared/loading-spinner/loading-spinner.component';
import { AdminService, AdminUser } from '../../../core/admin.service';
import { MessageService, AdminThreadSummary, ChatMessage } from '../../../core/message.service';

/**
 * Admin-Tab „Nachrichten" (Admin↔User-Direktnachrichten): Thread-Liste, Konversationsansicht,
 * Claim/Freigabe und Neustart einer Konversation per User-Suche. Aus <c>AdminComponent</c>
 * ausgegliedert; öffnet per Deep-Link <c>?thread=&lt;userId&gt;</c> direkt den passenden Thread
 * (eigenes queryParamMap-Abo — der `?tab=`-Teil bleibt im Parent).
 */
@Component({
  selector: 'app-admin-messages',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule,
    MatMenuModule, TranslateModule, LoadingSpinnerComponent,
  ],
  templateUrl: './admin-messages.component.html',
  styleUrl: './admin-messages.component.scss',
})
export class AdminMessagesComponent implements OnInit {
  threads: AdminThreadSummary[] = [];
  threadsLoading = false;
  selectedThreadUserId: number | null = null;
  selectedThreadName = '';
  threadMessages: ChatMessage[] = [];
  threadLoading = false;
  adminDraft = '';
  adminSending = false;
  msgUserSearch = '';
  msgUserResults: AdminUser[] = [];
  msgSearching = false;

  /** Per Deep-Link zu öffnender Thread (User-Id), sobald die Thread-Liste geladen ist. */
  private pendingThreadUserId: number | null = null;
  private destroyRef = inject(DestroyRef);
  // Such-Trigger für die User-Suche: gedrosselt + switchMap, damit nicht jeder Tastendruck einen
  // Request feuert und eine ältere Antwort keine neuere überschreibt (Out-of-order-Race).
  private msgUserSearchTrigger = new Subject<string>();

  constructor(
    private messageService: MessageService,
    private adminService: AdminService,
    private snackbar: SnackbarService,
    private translate: TranslateService,
    private auth: AuthService,
    private route: ActivatedRoute,
  ) {}

  ngOnInit(): void {
    this.msgUserSearchTrigger.pipe(
      debounceTime(250),
      distinctUntilChanged(),
      tap(() => this.msgSearching = true),
      switchMap(q => this.adminService.getUsers(q, 1, 10).pipe(catchError(() => of(null)))),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe(res => { this.msgUserResults = res ? res.items : this.msgUserResults; this.msgSearching = false; });

    // Deep-Link aus Benachrichtigungen: /admin?tab=messages&thread=<userId>. Als laufendes Abo, damit
    // aufeinanderfolgende Glocken-Klicks auf verschiedene Threads jeweils greifen.
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(qp => {
      const thread = qp.get('thread');
      if (thread && !isNaN(+thread)) {
        const uid = +thread;
        const t = this.threads.find(x => x.userId === uid);
        if (t) this.openThread(uid, t.username);   // Liste schon geladen → sofort öffnen
        else this.pendingThreadUserId = uid;        // sonst öffnet loadThreads(), sobald die Liste da ist
      }
    });

    this.loadThreads();
  }

  loadThreads(): void {
    this.threadsLoading = true;
    this.messageService.getThreads().subscribe({
      next: list => {
        this.threads = list;
        this.threadsLoading = false;
        if (this.pendingThreadUserId != null) {
          const uid = this.pendingThreadUserId;
          this.pendingThreadUserId = null;
          const t = this.threads.find(x => x.userId === uid);
          if (t) this.openThread(uid, t.username);
        }
      },
      error: () => { this.threadsLoading = false; },
    });
  }

  /** Bestehenden Thread öffnen + User-Antworten als gelesen markieren. */
  openThread(userId: number, username: string): void {
    this.selectedThreadUserId = userId;
    this.selectedThreadName = username;
    this.msgUserResults = [];
    this.msgUserSearch = '';
    this.threadLoading = true;
    this.messageService.getAdminThread(userId).subscribe({
      next: list => {
        this.threadMessages = list;
        this.threadLoading = false;
        if (list.some(m => !m.fromAdmin && !m.readByRecipient)) {
          this.messageService.markAdminSeen(userId).subscribe({
            next: () => this.loadThreads(),
            error: () => {},
          });
        }
      },
      error: () => { this.threadLoading = false; },
    });
  }

  /** Nachricht des Admins absenden (startet den Thread, falls neu). */
  sendAdminMessage(): void {
    const body = this.adminDraft.trim();
    if (!body || this.adminSending || this.selectedThreadUserId == null) return;
    this.adminSending = true;
    this.messageService.sendToUser(this.selectedThreadUserId, body).subscribe({
      next: m => {
        this.threadMessages = [...this.threadMessages, m];
        this.adminDraft = '';
        this.adminSending = false;
        this.loadThreads();
      },
      error: () => {
        this.adminSending = false;
        this.snackbar.show(this.translate.instant('messages.sendError'), { duration: 3000 });
      },
    });
  }

  /** User für eine neue Konversation suchen (Username/E-Mail). */
  searchMsgUsers(): void {
    const q = this.msgUserSearch.trim();
    if (q.length < 1) { this.msgUserResults = []; this.msgSearching = false; return; }
    this.msgUserSearchTrigger.next(q);
  }

  /** Neue (oder bestehende) Konversation mit dem gewählten User beginnen. */
  startConversation(user: AdminUser): void {
    const existing = this.threads.find(t => t.userId === user.id);
    if (existing) { this.openThread(user.id, user.username); return; }
    this.selectedThreadUserId = user.id;
    this.selectedThreadName = user.username;
    this.threadMessages = [];
    this.msgUserResults = [];
    this.msgUserSearch = '';
  }

  closeThread(): void {
    this.selectedThreadUserId = null;
    this.threadMessages = [];
    this.adminDraft = '';
  }

  /** Übersicht-Eintrag des gerade offenen Threads (für Claim-Status im Detail-Header). */
  get selectedThread(): AdminThreadSummary | null {
    return this.threads.find(t => t.userId === this.selectedThreadUserId) ?? null;
  }

  /** Eigene Admin-Id (zum Erkennen „von mir übernommen"). */
  get myId(): number | null {
    return this.auth.currentUser?.userId ?? null;
  }

  /** Thread übernehmen (Zuweisung an mich). */
  claimThread(userId: number): void {
    this.messageService.claimThread(userId).subscribe({
      next: () => this.loadThreads(),
      error: () => {},
    });
  }

  /** Thread wieder freigeben. */
  releaseThread(userId: number): void {
    this.messageService.releaseThread(userId).subscribe({
      next: () => this.loadThreads(),
      error: () => {},
    });
  }
}

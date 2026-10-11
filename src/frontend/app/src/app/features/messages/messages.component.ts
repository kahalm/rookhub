import { Component, HostListener, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AuthService } from '../../core/auth.service';
import { MessageService, ChatMessage } from '../../core/message.service';
import { SnackbarService } from '../../core/snackbar.service';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { LoadErrorComponent } from '../../shared/load-error/load-error.component';

/** Nachrichten-Thread des Users mit dem Admin-Team: Verlauf lesen + antworten. */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-messages',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, MatCardModule, MatIconModule, MatButtonModule, MatFormFieldModule, MatInputModule, TranslatePipe, LoadingSpinnerComponent, LoadErrorComponent],
  template: `
    <div class="msg-container">
      <h1>{{ 'messages.title' | translate }}</h1>

      @if (loading) {
        <app-loading-spinner />
      } @else if (loadError) {
        <app-load-error (retry)="retry()" />
      } @else {
        <!-- UI-Sweep 2026-10-11 (r2-rk-admin-own-inbox): ein Admin schriebe hier sich selbst — die
             Konversationen der Nutzer liegen im Admin-Bereich. Schreibfeld und Leertext entfallen. -->
        @if (isAdmin) {
          <mat-card class="admin-hint">
            <mat-card-content>
              <p>{{ 'messages.adminHint' | translate }}</p>
              <a mat-flat-button color="primary" routerLink="/admin" [queryParams]="{ tab: 'messages' }">
                <mat-icon>forum</mat-icon> {{ 'messages.toConversations' | translate }}
              </a>
            </mat-card-content>
          </mat-card>
        }
        @if (messages.length === 0) {
          @if (!isAdmin) {
            <p class="empty">{{ 'messages.emptyUserCanWrite' | translate }}</p>
          }
        } @else {
          <mat-card>
            <mat-card-content class="thread">
              @for (m of messages; track m.id) {
                <div class="bubble-row" [class.mine]="!m.fromAdmin">
                  <div class="bubble" [class.admin]="m.fromAdmin">
                    <div class="sender">{{ (m.fromAdmin ? 'messages.adminTeam' : 'messages.you') | translate }}</div>
                    <div class="body">{{ m.body }}</div>
                    <div class="meta">{{ m.createdAt | date:'short' }}</div>
                  </div>
                </div>
              }
            </mat-card-content>
          </mat-card>
        }

        @if (!isAdmin) {
        <div class="reply">
          <mat-form-field appearance="outline" class="reply-field">
            <mat-label>{{ (messages.length === 0 ? 'messages.writeLabel' : 'messages.replyLabel') | translate }}</mat-label>
            <textarea matInput [(ngModel)]="draft" rows="3" maxlength="4000"
                      (keydown.control.enter)="send()" [disabled]="sending"></textarea>
          </mat-form-field>
          <button mat-raised-button color="primary" (click)="send()" [disabled]="sending || !draft.trim()">
            <mat-icon>send</mat-icon> {{ 'common.send' | translate }}
          </button>
        </div>
        }
      }
    </div>
  `,
  styles: [`
    .msg-container { max-width: 760px; margin: 24px auto; padding: 0 16px; }
    .empty { color: color-mix(in srgb, currentColor 60%, transparent); font-style: italic; padding: 16px 0; }
    .thread { display: flex; flex-direction: column; gap: 10px; padding: 12px 4px; }
    .bubble-row { display: flex; }
    .bubble-row.mine { justify-content: flex-end; }
    .bubble { max-width: 80%; padding: 8px 12px; border-radius: 12px;
              background: color-mix(in srgb, currentColor 8%, transparent); }
    .bubble.admin { background: color-mix(in srgb, var(--mat-sys-primary, #3f51b5) 16%, transparent); }
    .sender { font-size: 0.72rem; font-weight: 600; opacity: 0.7; margin-bottom: 2px; }
    .body { white-space: pre-wrap; overflow-wrap: anywhere; line-height: 1.35; }
    .meta { font-size: 0.72rem; opacity: 0.55; margin-top: 4px; text-align: right; }
    .admin-hint { margin-bottom: 16px; }
    .admin-hint p { margin: 0 0 12px; }
    .reply { display: flex; flex-direction: column; gap: 8px; margin-top: 16px; }
    .reply-field { width: 100%; }
    .reply button { align-self: flex-end; }
  `]
})
export class MessagesComponent implements OnInit {
  messages: ChatMessage[] = [];
  loading = true;
  /** Erstes Laden gescheitert — sonst saehe die Seite aus wie „noch keine Nachrichten". */
  loadError = false;
  sending = false;
  draft = '';

  constructor(
    private messageService: MessageService,
    private snackbar: SnackbarService,
    private translate: TranslateService,
    private auth: AuthService,
  ) {}

  /** UI-Sweep 2026-10-11 (r2-rk-admin-own-inbox): Admins bekommen den Weg zu den Konversationen statt eines Schreibfelds. */
  get isAdmin(): boolean { return this.auth.isAdmin; }

  ngOnInit(): void { this.load(true); }

  /** Kommt der User zum Tab/Fenster zurück, den Thread frisch laden — sonst zeigt /messages eine neue
   *  Admin-Antwort erst nach Reload, während das Navbar-Badge schon hochgezählt hätte (Read-State driftet).
   *  Still im Hintergrund (kein Spinner) und nicht während eines laufenden Sendens. */
  @HostListener('window:focus')
  onWindowFocus(): void {
    if (!this.loading && !this.sending) this.load(true);
  }

  /** Erneut-Knopf des Fehlerzustands. */
  retry(): void {
    this.loading = true;
    this.loadError = false;
    this.load(true);
  }

  private load(markSeen: boolean): void {
    this.messageService.getThread().subscribe({
      next: list => {
        this.messages = list;
        this.loading = false;
        this.loadError = false;
        // Beim Öffnen die Admin-Nachrichten als gelesen markieren (leert das Navbar-Badge).
        if (markSeen && list.some(m => m.fromAdmin && !m.readByRecipient)) {
          this.messageService.markUserSeen().subscribe({ error: () => {} });
        }
      },
      // Nur das erste Laden (mit Spinner) wird zum Fehlerzustand; ein Hintergrund-Nachladen beim
      // Fokuswechsel bleibt still und laesst den angezeigten Verlauf stehen.
      error: () => { if (this.loading) this.loadError = true; this.loading = false; },
    });
  }

  send(): void {
    const body = this.draft.trim();
    if (!body || this.sending) return;
    this.sending = true;
    this.messageService.send(body).subscribe({
      next: m => { this.messages = [...this.messages, m]; this.draft = ''; this.sending = false; },
      // Entwurf bleibt stehen; der Nutzer muss aber erfahren, dass nichts angekommen ist.
      error: () => {
        this.sending = false;
        this.snackbar.warn(this.translate.instant('messages.sendError'));
      },
    });
  }
}

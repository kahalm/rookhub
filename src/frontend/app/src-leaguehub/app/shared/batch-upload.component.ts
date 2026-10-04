import { ChangeDetectionStrategy, Component, input, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { ClubClient } from '../core/club-api.service';

/** Gründe des Servers beim Stapel-Upload → Text. */
export function batchErrorText(reason: string | undefined): string {
  switch (reason) {
    case 'type': return 'nur Bilder oder PDF';
    case 'tooLarge': return 'zu groß';
    case 'tooMany': return 'mehr als 1000 Bilder in einem Stapel gehen nicht';
    case 'dailyLimit': return 'für heute ist über diesen Link genug hochgeladen — morgen geht es weiter';
    case 'finished': return 'der Stapel ist schon abgeschlossen';
    default: return 'hat nicht geklappt';
  }
}

/**
 * Stapel-Upload (0.651.0, Wunsch 2026-10-04): beliebig viele Formular-Bilder auf einmal. Sie werden NICHT eingelesen,
 * nur am Server abgelegt — die Admins bekommen eine Nachricht und kümmern sich darum. Jedes Bild geht einzeln hoch
 * (so bleibt jede Anfrage klein); ein Bild, das nicht durchgeht, hält die übrigen nicht auf.
 */
@Component({
  selector: 'lh-batch-upload',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <details class="batch" [open]="!!files().length || !!done()">
      <summary>Viele Formulare auf einmal hochladen (Stapel)</summary>
      <p class="small muted">Für einen ganzen Stoß Formulare: Die Bilder werden nur abgelegt, nicht sofort gelesen — ein Admin
        bekommt eine Nachricht und kümmert sich darum. Bilder oder PDF, bis 30 MB je Datei.</p>
      @if (done(); as d) {
        <p class="batch-done" role="status">Danke — {{ d.files }} {{ d.files === 1 ? 'Bild ist' : 'Bilder sind' }} abgelegt, die Admins sind informiert.</p>
        @if (failed().length) { <p class="small err">Nicht hochgeladen: {{ failed().join(', ') }}</p> }
        <button type="button" class="btn-sec" (click)="reset()">Noch einen Stapel</button>
      } @else {
        <label class="field">Bilder
          <input type="file" multiple accept="image/*,application/pdf" [disabled]="busy()" (change)="pick($event)" />
        </label>
        <label class="field">Kommentar <span class="muted small">(optional, z. B. Runde oder Mannschaft)</span>
          <textarea rows="2" maxlength="1000" [disabled]="busy()" [value]="comment()" (input)="comment.set($any($event.target).value)"></textarea>
        </label>
        <div class="actions">
          <button type="button" class="btn" [disabled]="busy() || !files().length" (click)="send()">
            {{ files().length ? files().length + (files().length === 1 ? ' Bild' : ' Bilder') + ' hochladen' : 'Hochladen' }}</button>
          @if (busy()) { <span class="small" role="status">{{ sent() }} von {{ files().length }} …</span> }
        </div>
        @if (error()) { <p class="small err" role="status">{{ error() }}</p> }
      }
    </details>
  `,
})
export class BatchUploadComponent {
  readonly client = input.required<ClubClient>();

  readonly files = signal<File[]>([]);
  readonly comment = signal('');
  readonly busy = signal(false);
  readonly sent = signal(0);
  readonly failed = signal<string[]>([]);
  readonly error = signal<string | null>(null);
  readonly done = signal<{ files: number } | null>(null);

  pick(ev: Event): void {
    const input = ev.target as HTMLInputElement;
    this.files.set(Array.from(input.files ?? []));
    this.error.set(null);
  }

  reset(): void {
    this.files.set([]);
    this.comment.set('');
    this.failed.set([]);
    this.done.set(null);
    this.sent.set(0);
  }

  async send(): Promise<void> {
    const files = this.files();
    if (!files.length || this.busy()) return;
    this.busy.set(true);
    this.error.set(null);
    this.failed.set([]);
    this.sent.set(0);
    const failed: string[] = [];
    let ok = 0;
    try {
      const { key } = await this.client().batchStart(this.comment());
      for (const f of files) {
        try {
          await this.client().batchFile(key, f);
          ok++;
        } catch (err) {
          const reason = err instanceof HttpErrorResponse ? err.error?.reason : undefined;
          failed.push(`${f.name} (${batchErrorText(reason)})`);
          if (reason === 'dailyLimit' || reason === 'tooMany' || reason === 'finished') break;
        }
        this.sent.update(n => n + 1);
      }
      if (!ok) {
        this.error.set(`Kein Bild hochgeladen: ${failed.join(', ')}`);
        await this.client().batchFinish(key).catch(() => undefined);   // leerer Stapel wird verworfen
        return;
      }
      const r = await this.client().batchFinish(key);
      this.failed.set(failed);
      this.done.set({ files: r.files });
    } catch (err) {
      const reason = err instanceof HttpErrorResponse ? err.error?.reason : undefined;
      this.error.set(`Hochladen ${batchErrorText(reason)}.`);
    } finally {
      this.busy.set(false);
    }
  }
}

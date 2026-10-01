import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, input, output, signal } from '@angular/core';
import { ConfirmService } from '@rh/shared/confirm-dialog/confirm-dialog.component';
import { firstValueFrom } from 'rxjs';
import { ClubApiService } from '../core/club-api.service';
import { Photo } from '../core/club.models';

/**
 * Die Fotos einer Trainingseinheit (Wunsch 2026-09-30: „zu Trainings mehrere Fotos hochladen"): Vorschaubilder im
 * Raster, ein Tipp öffnet das Bild groß, mit `editable` lässt sich eines löschen. Die Bilder liegen hinter der Anmeldung —
 * ein `<img src>` schickt keinen Bearer, deshalb holt die Komponente jedes Bild als Blob über die HTTP-Kette und zeigt es
 * über eine Objekt-Adresse, die sie beim Verlassen wieder freigibt. Hochladen macht die Seite (sie braucht die Einheit).
 */
@Component({
  selector: 'ch-session-photos',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (photos().length) {
      <ul class="photos" [class.small]="!editable()">
        @for (p of photos(); track p.id) {
          <li>
            <button type="button" class="photo" [attr.aria-label]="'Foto ' + ($index + 1) + ' groß zeigen'" (click)="open(p)">
              @if (thumbs()[p.id]; as src) { <img [src]="src" alt="" [width]="p.width" [height]="p.height"> }
              @else { <span class="photo-wait" aria-hidden="true"></span> }
            </button>
            @if (editable()) {
              <button type="button" class="photo-del" [attr.aria-label]="'Foto ' + ($index + 1) + ' löschen'" [disabled]="busy()" (click)="remove(p)">×</button>
            }
          </li>
        }
      </ul>
    }
    @if (large(); as l) {
      <div class="lightbox" (click)="close()">
        <img [src]="l" alt="Foto der Einheit" (click)="$event.stopPropagation()">
        <button type="button" class="lightbox-close" aria-label="Schließen" (click)="close()">×</button>
      </div>
    }
    <p class="err status-line" role="alert">{{ error() ?? '' }}</p>
  `,
})
export class SessionPhotosComponent {
  private readonly api = inject(ClubApiService);
  private readonly confirm = inject(ConfirmService);
  readonly sessionId = input.required<number>();
  readonly photos = input<Photo[]>([]);
  readonly editable = input(false);
  /** Ein Foto wurde gelöscht — die Seite nimmt es aus ihrer Liste. */
  readonly deleted = output<number>();

  /** Objekt-Adressen der Vorschaubilder je Foto-Kennung. */
  readonly thumbs = signal<Record<number, string>>({});
  readonly large = signal<string | null>(null);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  private readonly urls = new Set<string>();

  constructor() {
    inject(DestroyRef).onDestroy(() => this.revokeAll());
    // Neue Fotos nachladen, verschwundene freigeben — je Kennung genau einmal geholt.
    effect(() => {
      const wanted = new Set(this.photos().map(p => p.id));
      const have = this.thumbs();
      for (const [id, url] of Object.entries(have)) if (!wanted.has(Number(id))) this.forget(Number(id), url);
      for (const p of this.photos()) if (!(p.id in have)) void this.loadThumb(p.id);
    });
  }

  private async loadThumb(id: number): Promise<void> {
    this.thumbs.update(t => ({ ...t, [id]: '' }));                     // als „unterwegs" merken
    try {
      const blob = await this.api.photoBlob(this.sessionId(), id, true);
      const url = URL.createObjectURL(blob);
      this.urls.add(url);
      this.thumbs.update(t => (id in t ? { ...t, [id]: url } : t));
    } catch {
      this.thumbs.update(t => { const { [id]: _, ...rest } = t; return rest; });
    }
  }

  private forget(id: number, url: string): void {
    if (url) { URL.revokeObjectURL(url); this.urls.delete(url); }
    this.thumbs.update(t => { const { [id]: _, ...rest } = t; return rest; });
  }

  private revokeAll(): void {
    for (const url of this.urls) URL.revokeObjectURL(url);
    this.urls.clear();
  }

  async open(p: Photo): Promise<void> {
    this.error.set(null);
    try {
      const blob = await this.api.photoBlob(this.sessionId(), p.id, false);
      const url = URL.createObjectURL(blob);
      this.urls.add(url);
      this.large.set(url);
    } catch {
      this.error.set('Das Foto konnte nicht geladen werden.');
    }
  }

  close(): void {
    const url = this.large();
    if (url) { URL.revokeObjectURL(url); this.urls.delete(url); }
    this.large.set(null);
  }

  async remove(p: Photo): Promise<void> {
    if (!(await firstValueFrom(this.confirm.ask('Dieses Foto löschen?')))) return;
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.api.deletePhoto(this.sessionId(), p.id);
      this.deleted.emit(p.id);
    } catch {
      this.error.set('Das Foto konnte nicht gelöscht werden.');
    } finally {
      this.busy.set(false);
    }
  }
}

import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { ClubApiService } from '../core/club-api.service';
import { MemberPhotoStore } from '../core/member-photo.store';

/**
 * Das Bild eines Karteiblatts als kleines Porträt (Kartei-Zeile, Kopf des Blatts, Formular). Ohne Bild — oder solange es
 * lädt — steht der Anfangsbuchstabe da, damit die Zeilen einer Liste gleich breit beginnen. Mit `zoom` öffnet ein Tipp das
 * Bild groß. Geholt wird über den `MemberPhotoStore` (Blob mit Anmeldung, je Marke einmal).
 */
@Component({
  selector: 'ch-member-photo',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'avatar', '(document:keydown.escape)': 'close()' },
  template: `
    @if (src(); as s) {
      @if (zoom()) {
        <button type="button" class="avatar-open" [attr.aria-label]="'Bild von ' + name() + ' groß zeigen'" (click)="open()"><img [src]="s" alt=""></button>
      } @else {
        <img [src]="s" alt="">
      }
    } @else {
      <span class="avatar-ph" aria-hidden="true">{{ initial() }}</span>
    }
    @if (large(); as l) {
      <div class="lightbox" (click)="close()">
        <img [src]="l" [alt]="'Bild von ' + name()" (click)="$event.stopPropagation()">
        <button type="button" class="lightbox-close" aria-label="Schließen" (click)="close()">×</button>
      </div>
    }
  `,
})
export class MemberPhotoComponent {
  private readonly api = inject(ClubApiService);
  private readonly store = inject(MemberPhotoStore);

  readonly memberId = input.required<number>();
  /** Marke des Bilds (`photoVersion`); leer = kein Bild. */
  readonly version = input<number | null | undefined>(null);
  readonly name = input('');
  /** Ein Tipp zeigt das Bild groß (Kopf des Blatts). */
  readonly zoom = input(false);

  readonly src = computed(() => this.store.url(this.memberId(), this.version()));
  readonly initial = computed(() => this.name().trim().charAt(0).toUpperCase() || '?');
  readonly large = signal<string | null>(null);
  /** Die selbst erzeugte Adresse des großen Bilds — nur die wird beim Schließen freigegeben. */
  private ownLarge: string | null = null;

  constructor() {
    effect(() => this.store.request(this.memberId(), this.version()));
    inject(DestroyRef).onDestroy(() => this.close());
  }

  async open(): Promise<void> {
    const version = this.version();
    if (version == null) return;
    try {
      const blob = await this.api.memberPhotoBlob(this.memberId(), false, version);
      this.close();
      this.ownLarge = URL.createObjectURL(blob);
      this.large.set(this.ownLarge);
    } catch {
      this.large.set(this.src());                   // das große Bild kam nicht — dann wenigstens das Vorschaubild groß
    }
  }

  close(): void {
    if (this.ownLarge) URL.revokeObjectURL(this.ownLarge);
    this.ownLarge = null;
    this.large.set(null);
  }
}

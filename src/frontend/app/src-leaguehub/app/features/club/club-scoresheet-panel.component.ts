import { ChangeDetectionStrategy, Component, ElementRef, input, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { BatchUploadComponent } from '../../shared/batch-upload.component';
import { FilePickDirective } from '../../shared/file-pick.directive';
import { ScoresheetUploadStore } from './scoresheet-upload-store';

/**
 * Art „Partieformular" der Hinzufügen-Seite: Foto(s) wählen, Notation und Seite, „Formular einlesen", die eigenen
 * Formulare (mit Uhr, „Prüfen und übernehmen", „Verwerfen"), für Verwalter die offenen Formulare anderer und der
 * Stapel-Upload. Der Zustand liegt in {@link ScoresheetUploadStore} — er überlebt den Wechsel der Art.
 */
@Component({
  selector: 'lh-club-scoresheet-panel',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FilePickDirective, RouterLink, BatchUploadComponent],
  styles: [':host { display: contents; }'],
  templateUrl: './club-scoresheet-panel.component.html',
})
export class ClubScoresheetPanelComponent {
  readonly store = input.required<ScoresheetUploadStore>();
  private readonly photoInput = viewChild<ElementRef<HTMLInputElement>>('photoInput');

  upload(): Promise<void> {
    return this.store().upload(() => {
      const input = this.photoInput()?.nativeElement;
      if (input) input.value = '';
    });
  }
}

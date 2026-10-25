import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import { IconLabelDirective } from '../../../shared/icon-label/icon-label.directive';

/**
 * Unterer Teil der Seitenspalte im Kalkulations-Modus (Zerlegung Z20): Kapitelende samt Weg ins nächste Kapitel,
 * Bearbeitungs- und Speicherstand und — ohne Konto — der Hinweis „nur auf diesem Gerät" bzw. die Warnung, dass
 * gerade gar nichts gespeichert werden kann. Reine Darstellung; was gilt, entscheidet die `CalculationComponent`.
 */
@Component({
  selector: 'app-calc-status-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, RouterLink, MatButtonModule, MatIconModule, TranslatePipe, IconLabelDirective],
  templateUrl: './calc-status-panel.component.html',
  styleUrls: ['./calc-status-panel.component.scss'],
})
export class CalcStatusPanelComponent {
  /** Am Kapitelende ANGEKOMMEN (nicht dort eingestiegen) — nur dann „durchgearbeitet". */
  readonly atChapterEnd = input(false);
  readonly atLastPosition = input(false);
  readonly hasNextChapter = input(false);
  readonly chapterName = input('');
  readonly nextChapterName = input('');
  readonly doneCount = input(0);
  readonly positionCount = input(0);
  readonly saving = input(false);
  readonly savedAt = input<Date | null>(null);
  readonly showLocalNotice = input(false);
  readonly showLocalWarning = input(false);
  /** Anmelde-Link führt GENAU hierher zurück (inkl. `?pos=`/`?chapter=`). */
  readonly loginReturnUrl = input('');

  readonly nextChapter = output<void>();
  readonly dismissNotice = output<void>();
  readonly dismissWarning = output<void>();
}

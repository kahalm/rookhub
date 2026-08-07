import {
  ChangeDetectionStrategy, Component, Input, OnDestroy, OnInit, ChangeDetectorRef,
} from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { TranslateService } from '@ngx-translate/core';
import { fullscreenSupported, isFullscreen, onFullscreenChange, toggleFullscreen } from './fullscreen.util';

/**
 * Vollbild-Knopf für ein Schachbrett: schickt das übergebene Element ins echte Vollbild (über
 * Taskleiste/Browserleiste hinweg) und wieder heraus.
 *
 * <p>Platzierung: als schmale Zeile DIREKT ÜBER dem Brett (rechtsbündig, im Fluss) — bewusst
 * nicht als Overlay in der Brett-Ecke, das verdeckte dort das Eckfeld. Im Vollbild löst sich
 * der Knopf aus dem Fluss und schwebt fix rechts oben im schwarzen Balken (das Vollbild-Element
 * ist der Containing-Block für <c>position: fixed</c>).</p>
 *
 * <p>Im APP-Vollbild (ganze Oberfläche, Host-Klasse <c>app-fullscreen</c>) wandert er ebenfalls aus
 * dem Fluss — direkt neben den App-Vollbild-Beenden-Knopf oben rechts. Dort kostet er keine
 * Bretthöhe mehr (die Zeile über dem Brett verschwindet, das Brett rutscht nach oben), und beide
 * Vollbild-Ausgänge sitzen beieinander.</p>
 *
 * <p>Er sitzt INNERHALB des Vollbild-Elements — nur so bleibt er im Brett-Vollbild sichtbar und
 * man kommt ohne Tastatur wieder heraus. Erklärt wird er per nativem `title` statt per
 * `matTooltip` — im Vollbild-Element selbst ist das die einfachere Wahl (CDK-Overlays müssen
 * dafür erst über den `FullscreenOverlayService` mit umziehen).</p>
 *
 * <p>Kann der Browser kein Element-Vollbild (iOS-Safari), rendert die Komponente nichts.</p>
 */
@Component({
  selector: 'app-board-fullscreen-button',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatIconModule],
  template: `
    @if (supported && target) {
      <button type="button" class="board-fs-btn" [class.board-fs-btn--on]="active"
              (click)="toggle($event)"
              [attr.title]="label" [attr.aria-label]="label">
        <mat-icon>{{ active ? 'fullscreen_exit' : 'fullscreen' }}</mat-icon>
      </button>
    }
  `,
  styles: [`
    /* Schmale Zeile über dem Brett, Knopf rechtsbündig — überdeckt kein Feld. */
    :host {
      display: flex;
      justify-content: flex-end;
      line-height: 0;
    }
    .board-fs-btn {
      width: 22px; height: 22px;
      display: grid; place-items: center;
      padding: 0;
      margin-bottom: 2px;
      border: 0;
      border-radius: 4px;
      cursor: pointer;
      background: transparent;
      color: currentColor;
      opacity: 0.45;
      transition: opacity 0.12s ease-in-out, background 0.12s ease-in-out;
    }
    .board-fs-btn mat-icon {
      font-size: 18px; width: 18px; height: 18px; line-height: 18px;
    }
    .board-fs-btn:hover, .board-fs-btn:focus-visible {
      opacity: 1;
      background: color-mix(in srgb, currentColor 12%, transparent);
    }
    /* Im Vollbild: raus aus dem Fluss, fix rechts oben im schwarzen Balken — dort verdeckt er
       nichts und ist der einzige Ausweg neben Esc. (position: fixed löst gegen das
       Vollbild-Element auf, nicht gegen die Seite.) */
    .board-fs-btn--on {
      position: fixed;
      top: 8px; right: 8px;
      z-index: 90;
      width: 34px; height: 34px;
      background: rgba(0, 0, 0, 0.35);
      color: #fff;
      opacity: 0.8;
    }
    .board-fs-btn--on mat-icon { font-size: 24px; width: 24px; height: 24px; line-height: 24px; }
    .board-fs-btn--on:hover, .board-fs-btn--on:focus-visible {
      opacity: 1;
      background: rgba(0, 0, 0, 0.6);
    }
    /* App-Vollbild (Host-Klasse app-fullscreen auf app-root): der Knopf verlaesst den Fluss und
       legt sich links neben den App-Vollbild-Beenden-Knopf oben rechts. Damit verschwindet die
       schmale Zeile über dem Brett (der Host hat dann keine Kinder mehr im Fluss → Höhe 0) und
       das Brett rutscht genau darum nach oben; beide Vollbild-Ausgänge liegen beieinander.
       NICHT im Brett-Vollbild (--on): dort gilt die eigene Position, und der App-Knopf wird
       ohnehin nicht gerendert (der Browser zeigt nur den Brett-Teilbaum). Dialog-Bretter sind
       nicht betroffen — CDK-Overlays hängen außerhalb von app-root. */
    :host-context(.app-fullscreen) .board-fs-btn:not(.board-fs-btn--on) {
      position: fixed;
      top: 6px; right: 42px;      /* 30px Knopfbreite + 6px Abstand zum Beenden-Knopf */
      z-index: 1000;
      width: 30px; height: 30px;
      margin: 0;
      background: rgba(0, 0, 0, 0.35);
      color: #fff;
      opacity: 0.35;
    }
    :host-context(.app-fullscreen) .board-fs-btn:not(.board-fs-btn--on) mat-icon {
      font-size: 20px; width: 20px; height: 20px; line-height: 20px;
    }
    :host-context(.app-fullscreen) .board-fs-btn:not(.board-fs-btn--on):hover,
    :host-context(.app-fullscreen) .board-fs-btn:not(.board-fs-btn--on):focus-visible {
      opacity: 1;
      background: rgba(0, 0, 0, 0.6);
    }
  `],
})
export class BoardFullscreenButtonComponent implements OnInit, OnDestroy {
  /** Element, das ins Vollbild geht — die Vollbild-Hülle um das Brett (.board-fs-host & Co.). */
  @Input() target: HTMLElement | null = null;

  readonly supported = fullscreenSupported();
  active = false;

  private off?: () => void;
  private destroyed = false;

  constructor(private cdr: ChangeDetectorRef, private translate: TranslateService) {}

  ngOnInit(): void {
    if (!this.supported) return;
    this.off = onFullscreenChange(() => {
      // Das Ereignis hängt am `document` und kann noch eintreffen, während Angular die Ansicht
      // abbaut — ein markForCheck auf der zerstörten Ansicht wäre NG0205.
      if (this.destroyed) return;
      this.active = isFullscreen(this.target);
      this.cdr.markForCheck();
    });
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.off?.();
  }

  get label(): string {
    return this.translate.instant(this.active ? 'common.fullscreenExit' : 'common.fullscreen');
  }

  /** Klick nicht ans Brett durchreichen — sonst zieht chessground darunter eine Figur an. */
  toggle(event: Event): void {
    event.preventDefault();
    event.stopPropagation();
    if (this.target) void toggleFullscreen(this.target);
  }
}

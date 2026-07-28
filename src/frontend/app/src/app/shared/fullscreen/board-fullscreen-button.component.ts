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
 * <p>Er sitzt bewusst INNERHALB des Vollbild-Elements (Brett-Ecke) — nur so bleibt er im Vollbild
 * sichtbar und man kommt ohne Tastatur wieder heraus. Aus demselben Grund erklärt ihn ein natives
 * `title` und kein `matTooltip`: CDK-Overlays hängen am `<body>` und werden im Vollbild nicht
 * gerendert.</p>
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
    /* In der Brett-Ecke, halbtransparent — beim Überfahren des Bretts deutlich sichtbar.
       Der Elternknoten (Brett-Wrapper) setzt position: relative. */
    .board-fs-btn {
      position: absolute;
      top: 2px; right: 2px;
      z-index: 60;
      width: 24px; height: 24px;
      display: grid; place-items: center;
      padding: 0;
      border: 0;
      border-radius: 4px;
      cursor: pointer;
      background: rgba(0, 0, 0, 0.35);
      color: #fff;
      opacity: 0.3;
      transition: opacity 0.12s ease-in-out;
    }
    .board-fs-btn mat-icon {
      font-size: 18px; width: 18px; height: 18px; line-height: 18px;
    }
    :host-context(.board-wrapper:hover) .board-fs-btn,
    :host-context(.ab-wrap:hover) .board-fs-btn,
    :host-context(.cb-wrap:hover) .board-fs-btn { opacity: 0.85; }
    .board-fs-btn:hover, .board-fs-btn:focus-visible { opacity: 1; background: rgba(0, 0, 0, 0.6); }
    /* Im Vollbild ist der Knopf der einzige Ausweg neben Esc — immer gut sichtbar, größer. */
    .board-fs-btn--on {
      opacity: 0.8;
      width: 34px; height: 34px;
      top: 6px; right: 6px;
    }
    .board-fs-btn--on mat-icon { font-size: 24px; width: 24px; height: 24px; line-height: 24px; }
  `],
})
export class BoardFullscreenButtonComponent implements OnInit, OnDestroy {
  /** Element, das ins Vollbild geht — üblicherweise der Brett-Wrapper (= exakt die Brettfläche). */
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

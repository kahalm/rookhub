import {
  ChangeDetectionStrategy, Component, DestroyRef, ElementRef, effect, inject, input, signal, untracked, viewChild,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { TranslatePipe } from '@ngx-translate/core';
import { EinkService } from '../../core/eink.service';
import { formatSecondsClock } from '../../shared/clock-format.util';
import { TrainingVideosService } from './training-videos.service';

/**
 * „Erklärung hören“ im Nachspielen: EIN kompaktes `<video>` spielt nur den Ausschnitt `start`–`end` der Stunde und hält
 * am Ende an. „nur Ton“ blendet das Bild aus (das Element bleibt im DOM und spielt den Ton) — im E-Ink-Modus Vorgabe an,
 * weil bewegtes Bild dort nur schliert. Der Zustand steht als Text im Knopf („Anhalten“) und in der Zeile daneben,
 * nicht nur als Animation. Die Quelle wird erst beim ersten Abspielen gesetzt (`preload="none"`).
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-training-clip-player',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, MatSlideToggleModule, TranslatePipe],
  template: `
    <div class="clip">
      <div class="clip-row">
        <button mat-stroked-button type="button" class="clip-btn" (click)="toggle()" [attr.aria-pressed]="playing()">
          <mat-icon>{{ playing() ? 'stop' : 'play_arrow' }}</mat-icon>
          {{ (playing() ? 'trainingVideos.drill.stop' : 'trainingVideos.drill.listen') | translate }}
        </button>
        <span class="range">{{ clock(start()) }}–{{ clock(end()) }}</span>
        <mat-slide-toggle class="audio-only" [checked]="audioOnly()" (change)="audioOnly.set($event.checked)">
          {{ 'trainingVideos.drill.audioOnly' | translate }}
        </mat-slide-toggle>
      </div>
      @if (failed()) { <p class="clip-error" role="alert">{{ 'trainingVideos.drill.clipFailed' | translate }}</p> }
      <video #v class="clip-video" [class.hidden]="audioOnly()" preload="none" playsinline
             [class.no-src]="!src()" (error)="onError()"></video>
    </div>
  `,
  styles: [`
    .clip { display: flex; flex-direction: column; gap: 6px; }
    .clip-row { display: flex; flex-wrap: wrap; align-items: center; gap: 8px 12px; }
    .clip-btn { min-height: 44px; }
    .range { font-variant-numeric: tabular-nums; color: var(--mat-sys-on-surface-variant); font-size: .85rem; }
    .clip-video { width: min(100%, 360px); border-radius: 6px; background: black; }
    .clip-video.no-src { display: none; }
    .clip-video.hidden { display: none; }
    .clip-error { color: var(--rh-error); margin: 0; }
  `],
})
export class TrainingClipPlayerComponent {
  private readonly svc = inject(TrainingVideosService);
  private readonly eink = inject(EinkService);

  readonly lessonId = input.required<number>();
  readonly start = input.required<number>();
  readonly end = input.required<number>();

  readonly audioOnly = signal(this.eink.enabled());
  readonly playing = signal(false);
  readonly failed = signal(false);
  readonly src = signal<string | null>(null);

  private readonly video = viewChild<ElementRef<HTMLVideoElement>>('v');
  /** Lektion, zu der `src` gehört. */
  private srcLesson: number | null = null;
  private waitingForMeta = false;

  constructor() {
    // Neue Stellung (anderer Ausschnitt) oder andere Stunde: was läuft, hält an.
    effect(() => {
      this.lessonId(); this.start(); this.end();
      untracked(() => this.stop());
    });
    effect(onCleanup => {
      const el = this.video()?.nativeElement;
      if (!el) return;
      const onTime = () => { if (this.playing() && el.currentTime >= this.end()) this.stop(); };
      const onPause = () => this.playing.set(false);
      const onMeta = () => { if (this.waitingForMeta) { this.waitingForMeta = false; this.startPlayback(); } };
      el.addEventListener('timeupdate', onTime);
      el.addEventListener('pause', onPause);
      el.addEventListener('loadedmetadata', onMeta);
      onCleanup(() => {
        el.removeEventListener('timeupdate', onTime);
        el.removeEventListener('pause', onPause);
        el.removeEventListener('loadedmetadata', onMeta);
      });
    });
    inject(DestroyRef).onDestroy(() => this.video()?.nativeElement.pause());
  }

  toggle(): void {
    if (this.playing()) { this.stop(); return; }
    this.failed.set(false);
    const id = this.lessonId();
    if (this.srcLesson === id && this.src()) { this.startPlayback(); return; }
    this.svc.accessToken().subscribe({
      next: token => {
        this.srcLesson = id;
        this.waitingForMeta = true;
        this.playing.set(true);
        const url = this.svc.videoUrl(id, token);
        this.src.set(url);
        const el = this.video()?.nativeElement;
        if (el) { el.src = url; el.load(); }
      },
      error: () => this.failed.set(true),
    });
  }

  private startPlayback(): void {
    const el = this.video()?.nativeElement;
    if (!el) return;
    if (el.readyState < 1) { this.waitingForMeta = true; this.playing.set(true); el.load(); return; }
    el.currentTime = this.start();
    this.playing.set(true);
    void el.play().catch(() => { this.playing.set(false); });
  }

  stop(): void {
    this.waitingForMeta = false;
    this.playing.set(false);
    const el = this.video()?.nativeElement;
    if (el && !el.paused) el.pause();
  }

  onError(): void {
    if (!this.src()) return;
    this.playing.set(false);
    this.failed.set(true);
    this.svc.forgetToken();
    this.srcLesson = null;
  }

  clock(sec: number): string { return formatSecondsClock(sec); }
}

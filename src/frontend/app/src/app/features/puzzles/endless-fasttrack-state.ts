import { EndlessConfig, EndlessSession } from './endless-storage.service';
import { autoFasttrackThresholds, fasttrackSteps } from './endless-prefetch.util';

/**
 * Kapselt den Fasttrack-Schwellen-State eines Endless-Laufs getrennt von der (großen)
 * `endless-puzzle.component`: die beiden T1/T2-Mittelwerte (`avgFirst`/`avgSecond`), die
 * automatisch aus der Lauf-Historie abgeleiteten Vorschläge (`autoFirst`/`autoSecond`) und
 * die daraus folgenden Phasen-Schrittweiten (`phase1Step`/`phase2Step`).
 *
 * Die eigentliche Mathematik liegt weiterhin in `endless-prefetch.util`
 * ({@link autoFasttrackThresholds}, {@link fasttrackSteps}); diese Klasse kapselt nur das
 * Zusammenspiel Auto-Werte ↔ manuelle Config-Overrides ↔ abgeleitete Schritte und ist so
 * losgelöst von Angular/HTTP rein unit-testbar.
 */
export class EndlessFasttrackState {
  avgFirst = 0;
  avgSecond = 0;
  autoFirst = 0;
  autoSecond = 0;
  phase1Step = 0;
  phase2Step = 0;

  /**
   * Leitet die Auto-Schwellen aus der Historie ab, übernimmt vorhandene manuelle
   * Config-Overrides (sonst die Auto-Werte) und berechnet die Phasen-Schritte neu.
   */
  compute(config: EndlessConfig, sessionHistory: EndlessSession[]): void {
    const auto = autoFasttrackThresholds(config, sessionHistory);
    this.autoFirst = auto.first;
    this.autoSecond = auto.second;
    this.avgFirst = config.fasttrackThreshold1 ?? this.autoFirst;
    this.avgSecond = config.fasttrackThreshold2 ?? this.autoSecond;
    this.recalcSteps(config.startElo);
  }

  /**
   * Nach einer manuellen Eingabe: nur von den Auto-Werten abweichende Schwellen als
   * Override in die Config schreiben (sonst `undefined` = „folge Auto"), Schritte neu.
   */
  applyOverrides(config: EndlessConfig): void {
    config.fasttrackThreshold1 = this.avgFirst !== this.autoFirst ? this.avgFirst : undefined;
    config.fasttrackThreshold2 = this.avgSecond !== this.autoSecond ? this.avgSecond : undefined;
    this.recalcSteps(config.startElo);
  }

  /** Setzt eine Schwelle (1 oder 2) auf ihren Auto-Wert zurück und entfernt den Override. */
  reset(which: 1 | 2, config: EndlessConfig): void {
    if (which === 1) {
      this.avgFirst = this.autoFirst;
      config.fasttrackThreshold1 = undefined;
    } else {
      this.avgSecond = this.autoSecond;
      config.fasttrackThreshold2 = undefined;
    }
    this.recalcSteps(config.startElo);
  }

  private recalcSteps(startElo: number): void {
    const steps = fasttrackSteps(startElo, this.avgFirst, this.avgSecond);
    this.phase1Step = steps.phase1Step;
    this.phase2Step = steps.phase2Step;
  }
}

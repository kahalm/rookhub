import { Chess } from 'chess.js';
import { clearLegalMoveCache } from '../../../shared/chess/legal-moves';
import { clearMoveMemo } from '../explanation-lines';
import { mergeGameSection } from './summary-game-text.util';
import { SummaryBlock, linkSummaryMoves, parseSummary, positionsOf } from './summary-markdown.util';
import { SUMMARY_FIXTURE_EXPECTED, SUMMARY_FIXTURE_MARKDOWN, SUMMARY_FIXTURE_PGN, linkSignature } from './summary-linking.fixture';

/** Das, was die Seite rechnet, bevor der Text erscheint (`mergedSection` + `linkedBlocks` der Komponente). */
function linkAll(): { blocks: SummaryBlock[]; merged: boolean } {
  const positions = positionsOf(SUMMARY_FIXTURE_PGN);
  const merged = mergeGameSection(parseSummary(SUMMARY_FIXTURE_MARKDOWN), positions);
  return { blocks: linkSummaryMoves(merged.blocks, positions), merged: merged.merged };
}

/**
 * Züge im Text klickbar machen blockierte die Seite über eine Sekunde (0.978.1): ~700 Proben mit
 * `chess.moves({ verbose: true })`, ~25 000 Zuggenerierungen für eine Partie mit 36 Zügen. Seitdem kommen die legalen
 * Züge je Stellung aus einem Zwischenspeicher (shared/chess/legal-moves.ts). Diese Spec hält zwei Dinge fest:
 * das Ergebnis ist dasselbe wie vorher, und die Rechenarbeit rutscht nicht wieder hinein.
 */
describe('Zusammenfassung verlinken: realistischer Text (0.978.1)', () => {
  beforeEach(() => { clearLegalMoveCache(); clearMoveMemo(); });

  it('liefert Block für Block dieselbe Verlinkung wie die Fassung vor dem Zwischenspeicher (0.978.0)', () => {
    const { blocks, merged } = linkAll();
    expect(merged).toBeTrue();
    const got = linkSignature(blocks);
    expect(got.length).toBe(SUMMARY_FIXTURE_EXPECTED.length);
    got.forEach((line, i) => expect(line).withContext(`Block ${i}`).toBe(SUMMARY_FIXTURE_EXPECTED[i]));
    // 360 klickbare Züge, wie auf der Seite gezählt (button.mv + button.gt-mv)
    expect(got.join('\n').match(/@\d+\/\d+/g)?.length).toBe(360);
  });

  it('ein zweiter Lauf mit gefülltem Speicher liefert dasselbe (keine Reste aus dem ersten Lauf)', () => {
    const first = linkSignature(linkAll().blocks);
    expect(linkSignature(linkAll().blocks)).toEqual(first);
  });

  it('deckelt die Zuggenerierungen: je Stellung höchstens einmal, nie mehr die teure ausführliche Liste', () => {
    const proto = Chess.prototype as unknown as { moves: (...a: unknown[]) => unknown; move: (...a: unknown[]) => unknown; _moves?: (...a: unknown[]) => unknown };
    const positions = positionsOf(SUMMARY_FIXTURE_PGN);
    const blocks = parseSummary(SUMMARY_FIXTURE_MARKDOWN);
    const moves = spyOn(proto, 'moves').and.callThrough();
    const move = spyOn(proto, 'move').and.callThrough();
    // Die eigentliche Zuggenerierung von chess.js (intern) — gibt es sie unter diesem Namen nicht mehr, zählen nur die beiden oben.
    const generate = typeof proto._moves === 'function' ? spyOn(proto as { _moves: (...a: unknown[]) => unknown }, '_moves').and.callThrough() : null;

    const merged = mergeGameSection(blocks, positions);
    linkSummaryMoves(merged.blocks, positions);

    // Gemessen 0.978.1: 196 × moves(), 230 × move(), 865 Generierungen. Vorher 696 / 693 / ~24 800.
    expect(moves.calls.count()).toBeLessThanOrEqual(260);
    expect(move.calls.count()).toBeLessThanOrEqual(300);
    expect(moves.calls.allArgs().some(a => (a[0] as { verbose?: boolean } | undefined)?.verbose)).toBeFalse();
    if (generate) expect(generate.calls.count()).toBeLessThanOrEqual(1500);

    // Dieselbe Arbeit noch einmal (z. B. die Komponente rechnet neu): nichts wird neu erzeugt.
    const before = moves.calls.count() + move.calls.count();
    linkSummaryMoves(mergeGameSection(blocks, positions).blocks, positions);
    expect(moves.calls.count() + move.calls.count()).toBe(before);
  });
});

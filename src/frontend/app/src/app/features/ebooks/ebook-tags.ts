import { Pipe, PipeTransform, inject } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';

/**
 * Mehrsprachige E-Book-Schlagwörter (0.877.0, Nutzerwunsch 29.10.): gespeichert bleibt das deutsche Schlagwort
 * (`Ebook.Tags`, CSV) — es ist der kanonische Wert für Filter (`?tag=`), Suche, Bearbeiten-Dialog und Einlese-Werkzeuge.
 * Übersetzt wird NUR die Anzeige: Schlagwort → `ebooks.tags.<slug>` (en/de/hr/hu gepflegt, andere Sprachen fallen über
 * `fallbackLang: 'en'` aufs Englische zurück). Freie Schlagwörter und Eigennamen von Listen/Preisen („Top 100“,
 * „ChessDojo“, „ECF Book of the Year“) stehen hier nicht und erscheinen wörtlich.
 *
 * Die Schlüssel stehen als Literale da, damit `i18n-usage.spec` sie findet; ein neues Schlagwort = Zeile hier + Text in
 * allen vier gepflegten Sprachdateien.
 */
export const EBOOK_TAG_KEYS: Readonly<Record<string, string>> = {
  'Eröffnung': 'ebooks.tags.opening',
  'Kommentierte Partien': 'ebooks.tags.annotatedGames',
  'Lehrbuch': 'ebooks.tags.textbook',
  'Geschichte': 'ebooks.tags.history',
  'Strategie': 'ebooks.tags.strategy',
  'Partien': 'ebooks.tags.games',
  'Taktik': 'ebooks.tags.tactics',
  'Anfänger': 'ebooks.tags.beginners',
  'Aufgaben': 'ebooks.tags.exercises',
  'Training': 'ebooks.tags.training',
  'Mittelspiel': 'ebooks.tags.middlegame',
  'Biografie': 'ebooks.tags.biography',
  'Endspiel': 'ebooks.tags.endgame',
  'Spiele': 'ebooks.tags.otherGames',
  'Belletristik': 'ebooks.tags.fiction',
  'Problemschach': 'ebooks.tags.problemChess',
  'Psychologie': 'ebooks.tags.psychology',
  'Klassiker': 'ebooks.tags.classics',
  'Verteidigung': 'ebooks.tags.defence',
  'Angriff': 'ebooks.tags.attack',
  'Nachschlagewerk': 'ebooks.tags.reference',
  'Turnier': 'ebooks.tags.tournament',
  'Engine-Partiensammlung': 'ebooks.tags.engineGames',
  'Französisch': 'ebooks.tags.french',
  'Russisch': 'ebooks.tags.russian',
  'Deutsch': 'ebooks.tags.german',
  'Sizilianisch': 'ebooks.tags.sicilian',
  'Königsindisch': 'ebooks.tags.kingsIndian',
  'Spanisch': 'ebooks.tags.spanish',
  'Repertoire': 'ebooks.tags.repertoire',
  'Kombinationen': 'ebooks.tags.combinations',
  'Mattsetzen': 'ebooks.tags.checkmating',
  'Komposition': 'ebooks.tags.composition',
  'Schachvariante': 'ebooks.tags.chessVariant',
  'Berechnung': 'ebooks.tags.calculation',
  'Bauernstruktur': 'ebooks.tags.pawnStructure',
  'Königsgambit': 'ebooks.tags.kingsGambit',
  'Damenbauer': 'ebooks.tags.queensPawn',
  'Computerschach': 'ebooks.tags.computerChess',
  'Technologie': 'ebooks.tags.technology',
  'Meisterschaft': 'ebooks.tags.championship',
  'Muster': 'ebooks.tags.patterns',
  'Verbesserung': 'ebooks.tags.improvement',
  'Sowjetisch': 'ebooks.tags.soviet',
  'Figuren': 'ebooks.tags.pieces',
  'FIDE-Buchpreis': 'ebooks.tags.fideBookPrize',
  'FIDE-Buchpreis Shortlist': 'ebooks.tags.fideBookPrizeShortlist',
  'Heisman-Empfehlung': 'ebooks.tags.heismanRecommendation',
  'QC: Beste des 20. Jh.': 'ebooks.tags.qcBest20thCentury',
  'Aljechin': 'ebooks.tags.alekhine',
  'Geschlossen': 'ebooks.tags.closed',
  'Humor': 'ebooks.tags.humour',
  'Anthologie': 'ebooks.tags.anthology',
  'Slawisch': 'ebooks.tags.slav',
  'Britisch': 'ebooks.tags.british',
  'Fehler': 'ebooks.tags.mistakes',
  'Amerikanisch': 'ebooks.tags.american',
  'Lösen': 'ebooks.tags.solving',
  'Halbslawisch': 'ebooks.tags.semiSlav',
  'Damengambit': 'ebooks.tags.queensGambit',
  'Turmendspiele': 'ebooks.tags.rookEndgames',
  'Studien': 'ebooks.tags.studies',
  'Opfer': 'ebooks.tags.sacrifice',
  'Kriegsspiel': 'ebooks.tags.kriegspiel',
  'Künstliche Intelligenz': 'ebooks.tags.artificialIntelligence',
  'Zentrum': 'ebooks.tags.centre',
  'Fallen': 'ebooks.tags.traps',
  'Blockade': 'ebooks.tags.blockade',
  'Prophylaxe': 'ebooks.tags.prophylaxis',
  'Überdeckung': 'ebooks.tags.overprotection',
  'Italienisch': 'ebooks.tags.italian',
  'Hypermodern': 'ebooks.tags.hypermodern',
  'Damenindisch': 'ebooks.tags.queensIndian',
  'Katalanisch': 'ebooks.tags.catalan',
  'Zeitschrift': 'ebooks.tags.magazine',
  'Nimzoindisch': 'ebooks.tags.nimzoIndian',
  'Zweispringerspiel': 'ebooks.tags.twoKnights',
};

const KEY_BY_LOWER = new Map(Object.entries(EBOOK_TAG_KEYS).map(([tag, key]) => [tag.toLocaleLowerCase('de'), key]));

/** Übersetzungs-Schlüssel eines gespeicherten Schlagworts (Groß-/Kleinschreibung egal), sonst `null`. */
export function ebookTagKey(tag: string | null | undefined): string | null {
  return tag ? KEY_BY_LOWER.get(tag.trim().toLocaleLowerCase('de')) ?? null : null;
}

/** Anzeigetext eines Schlagworts in der aktuellen Sprache; ohne Übersetzung (oder bevor die Texte geladen sind) wörtlich. */
export function ebookTagLabel(tag: string, translate: Pick<TranslateService, 'instant'>): string {
  const key = ebookTagKey(tag);
  if (!key) return tag;
  const text = translate.instant(key);
  return typeof text === 'string' && text && text !== key ? text : tag;
}

/**
 * `{{ tag | ebookTagLabel }}` — unrein wie die `translate`-Pipe, damit ein Sprachwechsel die Beschriftung sofort umstellt
 * (die Arbeit ist ein Map-Zugriff + `instant`).
 */
@Pipe({ name: 'ebookTagLabel', standalone: true, pure: false })
export class EbookTagLabelPipe implements PipeTransform {
  private translate = inject(TranslateService);
  transform(tag: string | null | undefined): string {
    return tag ? ebookTagLabel(tag, this.translate) : '';
  }
}

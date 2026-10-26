// @ts-check
//
// Linter fuer alle Projekte des Workspaces (app, turnier, kidhub, leaguehub, clubhub) — Codereview F8-004.
//
// Vorher gab es weder Linter noch Formatter, und die Codebasis trug fuer dieselbe Sache zwei Stile
// nebeneinander (Default- neben OnPush-Change-Detection, Konstruktor-DI neben inject(), @Input() neben
// input()). Jede neue Seite wuchs im Stil des zuletzt kopierten Vorbilds. Wegen der Angular-22-Falle
// (nach HTTP ohne View-Mark kein Neuzeichnen, siehe render-after-http.interceptor) ist der CD-Stil
// keine Geschmacksfrage.
//
// PHASE 1 (erledigt) zaehlte nur. PHASE 2 = RATSCHE (aktiv): jede Regel bleibt 'warn', `npm run lint` endet
// gruen, aber eslint-baseline.json haelt je Regel die erlaubte Anzahl, und scripts/eslint-ratchet.mjs
// (`npm run lint:ratchet`, CI-Schritt in test.yml, build-frontend) macht den Job ROT, sobald eine Regel
// darueber liegt — der Bestand darf nur sinken. Nach dem Abbau von Warnungen `npm run lint:ratchet:update`
// (senkt die Baseline, hebt nie ohne --force). Eine neue Regel hier startet mit Baseline 0.
// PHASE 3 (aktiv seit 26.10.2026): eine Regel, deren Baseline 0 ist, steht auf 'error'; CiWorkflowTests verbietet
// 'error' fuer jede Regel mit Bestand (sonst waere `npm run lint` dauerhaft rot). Die sieben Stilregeln oben haben
// noch Bestand und bleiben 'warn' (Ratsche); die 'error'-Regeln im zweiten Block hatten beim Einfuehren 0 Treffer
// und halten den sauberen Stand fest. Die Ratsche meldet eine 'warn'-Regel, die auf 0 gesunken ist, als
// Kandidaten fuer 'error'. Ablauf und Abhilfe: docs/ci.md.
//
// Bewusst NUR die gemessenen Stilachsen, keine kompletten recommended-Saetze: jede Warnung soll eine
// bekannte Luecke sein, kein Rauschen. Ohne Typinformation (kein parserOptions.project) — schnell und
// unabhaengig vom Build. Specs, e2e und Konfigurationsdateien bleiben aussen vor.
import { defineConfig, globalIgnores } from 'eslint/config';
import tseslint from 'typescript-eslint';
import angular from 'angular-eslint';

export default defineConfig(
  globalIgnores(['dist/', '.angular/', 'e2e/', 'public*/', '*.js', '*.mjs', '**/*.spec.ts']),
  {
    // src = RookHub, src-* = die uebrigen Projekte; ein neues Projekt src-<name> ist automatisch dabei.
    files: ['src*/**/*.ts'],
    languageOptions: { parser: tseslint.parser },
    plugins: {
      '@typescript-eslint': tseslint.plugin,
      '@angular-eslint': angular.tsPlugin,
    },
    rules: {
      // Change-Detection: neue Komponenten OnPush (+ Signale), nicht Default.
      '@angular-eslint/prefer-on-push-component-change-detection': 'warn',
      // DI: inject() statt Konstruktor-Parameter.
      '@angular-eslint/prefer-inject': 'warn',
      // input()/viewChild() statt @Input()/@ViewChild(), Signal-Felder readonly.
      '@angular-eslint/prefer-signals': 'warn',
      '@typescript-eslint/no-explicit-any': 'warn',
      'no-console': 'warn',
      // Leere catch-Bloecke verschlucken Fehler still — ein Kommentar im Block erklaert die Absicht.
      'no-empty': ['warn', { allowEmptyCatch: false }],
      // Faengt u. a. `error: () => {}` (Fehler stumm geschluckt) und leere Lifecycle-Hooks.
      '@typescript-eslint/no-empty-function': 'warn',

      // ---- PHASE 3: 'error' — beim Einfuehren 0 Treffer, Baseline 0 (eslint-baseline.json) ----
      // Angular-Architektur (src/frontend/CLAUDE.md): nur Standalone-Komponenten, Lebenszyklus sauber deklariert.
      '@angular-eslint/prefer-standalone': 'error',
      '@angular-eslint/use-lifecycle-interface': 'error',
      '@angular-eslint/contextual-lifecycle': 'error',
      '@angular-eslint/no-empty-lifecycle-method': 'error',
      '@angular-eslint/no-inputs-metadata-property': 'error',
      '@angular-eslint/no-outputs-metadata-property': 'error',
      '@angular-eslint/no-output-on-prefix': 'error',
      '@angular-eslint/no-output-rename': 'error',
      // Rueckfragen nie nativ (F8-005, ConfirmService/PromptService) — fuer alert/confirm/prompt.
      'no-alert': 'error',
      // Kein Code aus Zeichenketten, kein liegengebliebenes debugger, kein var.
      'no-eval': 'error',
      'no-implied-eval': 'error',
      'no-new-func': 'error',
      'no-debugger': 'error',
      'no-var': 'error',
      // @ts-ignore/@ts-nocheck verbergen Typfehler; @ts-expect-error nur mit Begruendung.
      '@typescript-eslint/ban-ts-comment': 'error',
    },
  },
);

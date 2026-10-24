using RookHub.Tools.LibraryImport.Commands;
using RookHub.Tools.LibraryImport.Infrastructure;

// ---------------------------------------------------------------------------
// Rohbestand befuellen. Vier Schritte, jeder fuer sich wiederholbar:
//
//   import <datei>   Partien einlesen (Kopfdaten, Kommentar-Merkmale, Zug-Hash)
//   dedupe           gleiche Zugfolgen zusammenfassen (DuplicateOfId, Status)
//   rehash-results   Altbestand mit nacktem „1/2" am Ende nachrechnen [--dry-run] — danach dedupe
//   openings         Eroeffnungszeile + ersten kommentierten Halbzug nachtragen
//   languages        Sprache der Kommentare bestimmen
//   score            Eignungsnote fuer die Punktepartie berechnen
//   queue            die besten Partien zum Rechnen einreihen
//   comments         die Kommentare eingereihter Partien nach Sprachen trennen
//   analysis-openings  Eroeffnungszeile der eingereihten Partien nachtragen
//   translate        fehlende Sprachen uebersetzen lassen (TextLlm__BaseUrl = eigene Hardware, sonst Anthropic:TextApiKey)
//                    — auch einen KURS: translate --to de --course <bookId> [--parallel p]
//   resplit          als einsprachig vermerkte, aber zweisprachige Partien nachtraeglich zerlegen [--dry-run]
//   tree             Eroeffnungsbaum einer Stellung gegen die echte Datenbank nachsehen
//   embed            Kommentare fuer „Frag die Kommentare" einbetten (Embedding__BaseUrl)
//   stats            zeigen, was drinsteht (Vorgabe ohne Befehl)
//   migrate          Schema per Migrationen anlegen (nur fuer eine WEGWERF-MariaDB, z. B. zum Trainieren)
//   league-import    eine Liga aus dem SBV-Ligamanager oder dem Schachkreis Zugspitze einspielen (ohne Ansichten;
//                    --profiles = auch Spielerkarten) — siehe Commands/LeagueImportCommand
//   league-train     Prognose-Modell einer Region trainieren + Backtest (--region bayern --out league-model-bayern.json)
//
// Verbindung ueber ConnectionStrings__DefaultConnection. Laeuft NICHT als API-Instanz —
// ein zweiter RookHub.Api gegen dieselbe Datenbank streitet sich mit dem Auftrags-Worker
// um die Engines. Hier oeffnet nur ein DbContext.
//
// Aufbau: je Befehl eine Klasse unter Commands/ (<Name>Command.RunAsync), die Tabelle Name → Befehl in
// Commands/CommandTable.cs, Gemeinsames (Kommandozeile, DbContext-Fabrik, Fortschritt, Logger) unter Infrastructure/.
// ---------------------------------------------------------------------------

var command = args.FirstOrDefault() ?? CommandTable.Default;
var connection = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
if (string.IsNullOrWhiteSpace(connection))
{
    Console.Error.WriteLine("ConnectionStrings__DefaultConnection fehlt.");
    return 1;
}

if (!CommandTable.All.TryGetValue(command, out var run))
{
    Console.Error.WriteLine($"Unbekannter Befehl: {command}");
    return 1;
}
return await run(new ToolContext(new ToolOptions(args), new ToolDb(connection)));

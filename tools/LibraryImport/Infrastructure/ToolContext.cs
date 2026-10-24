namespace RookHub.Tools.LibraryImport.Infrastructure;

/// <summary>Was jeder Befehl bekommt: seine Kommandozeile und die Datenbank-Fabrik.</summary>
public sealed record ToolContext(ToolOptions Options, ToolDb Db);

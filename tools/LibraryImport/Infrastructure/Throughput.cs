namespace RookHub.Tools.LibraryImport.Infrastructure;

/// <summary>Fortschrittsanzeige der langen Laeufe.</summary>
public static class Throughput
{
    /// <summary>„1 234/s" seit <paramref name="started"/>; leer, solange keine Zeit vergangen ist.</summary>
    public static string Rate(DateTime started, int done)
    {
        var seconds = (DateTime.UtcNow - started).TotalSeconds;
        return seconds <= 0 ? "" : $"{done / seconds:N0}/s";
    }
}

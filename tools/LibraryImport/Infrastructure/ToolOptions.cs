namespace RookHub.Tools.LibraryImport.Infrastructure;

/// <summary>
/// Die Kommandozeile eines Befehls — der ROHE Argumentvektor, Befehlsname an Stelle 0 eingeschlossen.
/// <para>Bewusst so schlicht wie vorher in Program.cs: eine Option ist der Name, ihr Wert das Element danach;
/// bei mehrfachem Vorkommen gilt das ERSTE. Ein Wert, der keine Zahl ist, zaehlt bei <see cref="Int"/> als
/// nicht angegeben (die Vorgabe des Befehls greift). Fehlt das Element nach dem Namen, ebenfalls.</para>
/// </summary>
public sealed class ToolOptions(string[] args)
{
    /// <summary>Alle Argumente in der gegebenen Reihenfolge (Stelle 0 = Befehl).</summary>
    public IReadOnlyList<string> All => args;

    /// <summary>Das Argument an Stelle <paramref name="index"/> oder <c>null</c> (Stelle 1 = erstes nach dem Befehl).</summary>
    public string? Positional(int index) => args.ElementAtOrDefault(index);

    /// <summary>Steht der Schalter (z. B. <c>--dry-run</c>) irgendwo in der Zeile?</summary>
    public bool Has(string flag) => args.Contains(flag);

    /// <summary>Der Wert hinter <paramref name="name"/> oder <c>null</c>.</summary>
    public string? String(string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    /// <summary>Der Wert hinter <paramref name="name"/> als Zahl oder <c>null</c> (fehlt oder keine Zahl).</summary>
    public int? Int(string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var v) ? v : null;
    }

    /// <summary>
    /// <c>--shard i/n</c> fuer parallele Laeufe: ohne die Option <c>(0, 1)</c>, sonst die beiden Zahlen mit
    /// <c>0 &lt;= i &lt; n</c>. <c>false</c>, wenn die Option da, aber unbrauchbar ist (<paramref name="raw"/> = ihr Text).
    /// </summary>
    public bool TryShard(out int index, out int count, out string? raw)
    {
        index = 0;
        count = 1;
        raw = String("--shard");
        if (raw is null) return true;
        var parts = raw.Split('/');
        return parts.Length == 2 && int.TryParse(parts[0], out index)
            && int.TryParse(parts[1], out count)
            && count >= 1 && index >= 0 && index < count;
    }
}

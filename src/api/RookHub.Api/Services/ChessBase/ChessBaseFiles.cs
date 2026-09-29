using System.IO.Compression;

namespace RookHub.Api.Services.ChessBase;

/// <summary>Welches ChessBase-Format eine Dateimenge ist.</summary>
public enum ChessBaseFormat
{
    /// <summary>Klassisch (<c>.cbh</c>, seit ChessBase 6).</summary>
    Cbh,
    /// <summary>ChessBase 17 und später (<c>.2cbh</c>).</summary>
    Cb2,
}

/// <summary>
/// Die Dateien EINER ChessBase-Datenbank, nach Endung (klein, mit Punkt). Eine Datenbank ist keine Datei, sondern eine
/// Menge gleichnamiger Dateien — die <c>.cbh</c> allein ist nur das Inhaltsverzeichnis, die Züge stehen in <c>.cbg</c>,
/// die Namen in <c>.cbp</c>/<c>.cbt</c> (2CBH: <c>.2cbg</c>, <c>.2lid</c>). Hochgeladen wird deshalb alles, was im Ordner
/// liegt, oder ein ZIP davon; was nicht gebraucht wird (Suchbeschleuniger, Anmerkungen, <c>.ini</c>), bleibt liegen —
/// LeagueHub schickt deshalb gleich nur <see cref="Upload"/>.
/// </summary>
public sealed class ChessBaseFiles
{
    /// <summary>Was zum Lesen der Partien nötig ist — Anmerkungen und Suchbeschleuniger braucht die Hauptvariante nicht.</summary>
    public static readonly IReadOnlyDictionary<ChessBaseFormat, string[]> Required = new Dictionary<ChessBaseFormat, string[]>
    {
        [ChessBaseFormat.Cbh] = new[] { ".cbh", ".cbg", ".cbp", ".cbt" },
        [ChessBaseFormat.Cb2] = new[] { ".2cbh", ".2cbg", ".2lid" },
    };

    /// <summary>Was LeagueHub aus einer Auswahl hochlädt: das Nötige plus die Kommentatoren (<c>.cbc</c>, sonst fehlt
    /// <c>[Annotator]</c>).</summary>
    public static readonly string[] Upload = { ".cbh", ".cbg", ".cbp", ".cbt", ".cbc", ".2cbh", ".2cbg", ".2lid" };

    /// <summary>Alle Endungen, die zu einer Datenbank gehören können (und damit im Auswahlfeld stehen dürfen).</summary>
    public static readonly string[] KnownExtensions =
    {
        ".cbh", ".cbj", ".cbg", ".cba", ".cbp", ".cbt", ".cbtt", ".cbc", ".cbs", ".cbe", ".cbl", ".cbm", ".cbb", ".cbgi",
        ".cit", ".cib", ".cit2", ".cib2", ".flags", ".2cbh", ".2cbg", ".2cba", ".2lid", ".2lgd", ".2lcd", ".ini",
    };

    /// <summary>Höchstens so viele Bytes, ausgepackt, je Upload — ein ZIP darf nicht zur Bombe werden.</summary>
    public const long MaxTotalBytes = 64L * 1024 * 1024;

    private readonly Dictionary<string, byte[]> _byExtension;

    private ChessBaseFiles(string name, ChessBaseFormat format, Dictionary<string, byte[]> byExtension)
    {
        Name = name;
        Format = format;
        _byExtension = byExtension;
    }

    /// <summary>Gemeinsamer Name der Dateien (ohne Endung), z. B. „MeineSpiele".</summary>
    public string Name { get; }

    public ChessBaseFormat Format { get; }

    public byte[]? Get(string extension) => _byExtension.TryGetValue(extension, out var b) ? b : null;

    public byte[] Require(string extension) =>
        Get(extension) ?? throw new ChessBaseFormatException("missingFile", $"{Name}{extension} fehlt.");

    /// <summary>Die Endung einer Datei, wie sie zur Datenbank gehört („.2cbh"), sonst <c>null</c>.</summary>
    public static string? ExtensionOf(string fileName)
    {
        var name = Path.GetFileName(fileName ?? string.Empty);
        var dot = name.LastIndexOf('.');
        if (dot <= 0) return null;
        var ext = name[dot..].ToLowerInvariant();
        return Array.IndexOf(KnownExtensions, ext) >= 0 ? ext : null;
    }

    /// <summary>Aus einzelnen Dateien (Name, Inhalt). Genau EINE Datenbank: liegen zwei im Ordner (auch dieselbe in
    /// beiden Formaten), wäre offen, welche gemeint ist → <c>multipleDatabases</c>.</summary>
    public static ChessBaseFiles FromFiles(IEnumerable<(string Name, byte[] Data)> files)
    {
        var groups = new Dictionary<string, Dictionary<string, byte[]>>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var (name, data) in files)
        {
            var ext = ExtensionOf(name);
            if (ext == null) continue;
            total += data.LongLength;
            if (total > MaxTotalBytes) throw TooLarge();
            var file = Path.GetFileName(name);
            var key = file[..^ext.Length];
            if (!groups.TryGetValue(key, out var g)) groups[key] = g = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            g[ext] = data;
        }

        var databases = new List<(string Name, ChessBaseFormat Format, Dictionary<string, byte[]> Files)>();
        foreach (var (key, g) in groups)
        {
            if (g.ContainsKey(".2cbh")) databases.Add((key, ChessBaseFormat.Cb2, g));
            if (g.ContainsKey(".cbh")) databases.Add((key, ChessBaseFormat.Cbh, g));
        }
        if (databases.Count == 0)
            throw new ChessBaseFormatException("noDatabase", "Keine ChessBase-Datenbank (.cbh oder .2cbh) dabei.");
        if (databases.Count > 1)
            throw new ChessBaseFormatException("multipleDatabases", "Bitte nur eine Datenbank auf einmal.");

        var (dbName, format, dbFiles) = databases[0];
        var missing = Required[format].Where(e => !dbFiles.ContainsKey(e)).ToList();
        if (missing.Count > 0)
            throw new ChessBaseFormatException("missingFile",
                $"Es fehlt: {string.Join(", ", missing.Select(e => dbName + e))}. Bitte alle Dateien der Datenbank auswählen.");
        return new ChessBaseFiles(dbName, format, dbFiles);
    }

    /// <summary>Was ein Upload bringt, in beliebiger Mischung: einzelne Dateien, einzeln gepackte (<c>MeineSpiele.2cbg.gz</c>
    /// — so schickt LeagueHub sie, eine kommentierte <c>.cbg</c> schrumpft dabei auf ein Siebtel) und ZIPs. Alles zählt
    /// AUSGEPACKT gegen <see cref="MaxTotalBytes"/>, gemessen beim Lesen, nicht an einer Längenangabe.</summary>
    public static ChessBaseFiles FromUploads(IEnumerable<(string Name, byte[] Data)> uploads)
    {
        var files = new List<(string, byte[])>();
        long left = MaxTotalBytes;
        foreach (var (name, data) in uploads)
        {
            var file = Path.GetFileName(name ?? string.Empty);
            if (file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var entry in Unzip(new MemoryStream(data), ref left)) files.Add(entry);
            }
            else if (file.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            {
                var inner = file[..^3];
                if (ExtensionOf(inner) == null) continue;
                try
                {
                    using var gz = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
                    var raw = ReadLimited(gz, left);
                    left -= raw.LongLength;
                    files.Add((inner, raw));
                }
                catch (InvalidDataException)
                {
                    throw new ChessBaseFormatException("invalidFile", $"{inner} lässt sich nicht auspacken.");
                }
            }
            else if (ExtensionOf(file) != null)
            {
                left -= data.LongLength;
                if (left < 0) throw TooLarge();
                files.Add((file, data));
            }
        }
        return FromFiles(files);
    }

    /// <summary>Aus einem ZIP — Ordner darin sind egal, gezählt wird ausgepackt (<see cref="MaxTotalBytes"/>).</summary>
    public static ChessBaseFiles FromZip(Stream zip)
    {
        long left = MaxTotalBytes;
        return FromFiles(Unzip(zip, ref left));
    }

    private static List<(string Name, byte[] Data)> Unzip(Stream zip, ref long left)
    {
        var files = new List<(string, byte[])>();
        try
        {
            using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.EndsWith('/') || ExtensionOf(entry.Name) == null) continue;
                if (entry.Length > left) throw TooLarge();
                using var s = entry.Open();
                // Nicht der Längenangabe im Verzeichnis trauen: gelesen wird höchstens, was noch ins Budget passt.
                var data = ReadLimited(s, Math.Min(left, entry.Length));
                left -= data.LongLength;
                files.Add((entry.Name, data));
            }
        }
        catch (InvalidDataException)
        {
            throw new ChessBaseFormatException("invalidZip", "Das ZIP lässt sich nicht lesen.");
        }
        return files;
    }

    /// <summary>Liest höchstens <paramref name="limit"/> Bytes — ein Byte mehr ist <c>tooLarge</c>.</summary>
    private static byte[] ReadLimited(Stream s, long limit)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = s.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (ms.Length + read > limit) throw TooLarge();
            ms.Write(buffer, 0, read);
        }
        return ms.ToArray();
    }

    private static ChessBaseFormatException TooLarge() => new("tooLarge", "Die Datenbank ist zu groß.");
}

/// <summary>Die Datenbank lässt sich nicht lesen; <see cref="Reason"/> ist der Grund-Code für die Oberfläche.</summary>
public sealed class ChessBaseFormatException(string reason, string message) : Exception(message)
{
    public string Reason { get; } = reason;
}

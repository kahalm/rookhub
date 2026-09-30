using System.Text.Json;

namespace RookHub.Api.Services;

/// <summary>Die Antwort des Modells, gelesen (Schema aus <see cref="ScoresheetPrompt.Schema"/>).</summary>
public sealed class ScoresheetTranscription
{
    public string? NotationLanguage { get; set; }
    public string? Event { get; set; }
    public string? Site { get; set; }
    public string? Date { get; set; }
    public string? DateIso { get; set; }
    public string? Round { get; set; }
    public string? White { get; set; }
    public string? Black { get; set; }
    public string? Result { get; set; }
    public List<Entry> Moves { get; set; } = new();
    public string? Remarks { get; set; }
    /// <summary>Maße des Bildes, das das Modell bekam (von uns an die gespeicherte Antwort gehängt, nicht vom Modell) —
    /// die Kästen stehen in dessen Pixeln. Fehlt bei Einlesungen vor 0.551.3.</summary>
    public int? ImageWidth { get; set; }
    public int? ImageHeight { get; set; }
    /// <summary>Maße JEDER Seite als [Breite, Höhe] — nur bei einem Formular über mehrere Fotos (0.600.0); die erste
    /// steht zusätzlich in <see cref="ImageWidth"/>/<see cref="ImageHeight"/>.</summary>
    public List<int[]>? PageSizes { get; set; }

    /// <summary>Über wie viele Fotos das Formular geht (1, solange <see cref="PageSizes"/> fehlt).</summary>
    public int PageCount => PageSizes is { Count: > 1 } p ? p.Count : 1;

    /// <summary>Die Antwort des Modells mit den Bildmaßen daneben (<see cref="ImageWidth"/>/<see cref="ImageHeight"/>);
    /// unverändert, wenn es keine Maße oder kein JSON-Objekt ist.</summary>
    public static string? WithImageSize(string? json, (int Width, int Height)? size)
        => WithImageSize(json, size is { } s ? new[] { s } : Array.Empty<(int, int)>());

    /// <summary>Dasselbe je Seite: die erste als <c>imageWidth</c>/<c>imageHeight</c>, bei mehreren Seiten alle als
    /// <c>pageSizes</c>.</summary>
    public static string? WithImageSize(string? json, IReadOnlyList<(int Width, int Height)> sizes)
    {
        if (json == null || sizes.Count == 0) return json;
        try
        {
            if (System.Text.Json.Nodes.JsonNode.Parse(json) is not System.Text.Json.Nodes.JsonObject o) return json;
            o["imageWidth"] = sizes[0].Width;
            o["imageHeight"] = sizes[0].Height;
            if (sizes.Count > 1)
                o["pageSizes"] = new System.Text.Json.Nodes.JsonArray(sizes
                    .Select(s => (System.Text.Json.Nodes.JsonNode?)new System.Text.Json.Nodes.JsonArray(s.Width, s.Height)).ToArray());
            return o.ToJsonString();
        }
        catch (JsonException) { return json; }
    }

    /// <summary>Je Eintrag seine Seite (1-basiert), in 1..<see cref="PageCount"/> geklemmt — bei einer Seite immer 1.</summary>
    public List<int> EntryPages() => Moves.Select(m => PageOf(m)).ToList();

    private int PageOf(Entry m) => PageCount > 1 ? Math.Clamp(m.Page ?? 1, 1, PageCount) : 1;

    /// <summary>
    /// Je Eintrag der Kasten in 0..1000 des aufrechten Fotos (so rechnet die Korrekturseite), <c>null</c> = unbrauchbar.
    /// Mit Bildmaßen: Pixel → Promille. Ohne (Einlesungen von 0.550.0): die Kästen SOLLTEN schon 0..1000 sein; greift
    /// aber auch nur einer darüber hinaus, war es in Wahrheit Pixel eines unbekannten Bildes — dann alle verwerfen statt
    /// an den Rand gequetschte Ausschnitte zu zeigen (so auf Dev passiert: y bis 1790).
    /// </summary>
    public List<int[]?> NormalizedBoxes()
    {
        // Mehrere Seiten: jeder Kasten in den Pixeln SEINER Seite.
        if (PageCount > 1)
            return Moves.Select(m => PageSizes![PageOf(m) - 1] is { Length: 2 } s ? m.NormalizedBox(s[0], s[1]) : null).ToList();
        if (ImageWidth is int w && w > 0 && ImageHeight is int h && h > 0)
            return Moves.Select(m => m.NormalizedBox(w, h)).ToList();
        var outOfRange = Moves.Any(m => m.Box is { Count: 4 } b && b.Any(v => v > 1000));
        return Moves.Select(m => outOfRange ? null : m.NormalizedBox()).ToList();
    }

    public sealed class Entry
    {
        public int MoveNumber { get; set; }
        public string? Color { get; set; }
        public string Written { get; set; } = string.Empty;
        public string? San { get; set; }
        public List<string>? Alternatives { get; set; }
        public string? Confidence { get; set; }
        public string? Note { get; set; }
        /// <summary>Wo der Eintrag auf dem Foto steht: [x0, y0, x1, y1] in PIXELN des Bildes, das das Modell bekam
        /// (<see cref="ImageWidth"/> × <see cref="ImageHeight"/>, aufrecht wie im Browser); bei Einlesungen von 0.550.0
        /// als 0..1000 angefordert. Fehlt davor und bei dots.ocr.</summary>
        public List<int>? Box { get; set; }
        /// <summary>Auf welchem Foto der Eintrag steht (1 = erstes) — nur bei einem Formular über mehrere Fotos.</summary>
        public int? Page { get; set; }

        /// <summary>Pixel eines <paramref name="width"/>×<paramref name="height"/>-Bildes → 0..1000; ein Kasten, der
        /// deutlich (über 3 %) aus dem Bild ragt, stammt nicht aus diesem Bild → <c>null</c>.</summary>
        public int[]? NormalizedBox(int width, int height)
        {
            if (Box is not { Count: 4 } || width <= 0 || height <= 0) return null;
            if (Box.Any(v => v < -0.03 * Math.Max(width, height))
                || Box[0] > width * 1.03 || Box[2] > width * 1.03 || Box[1] > height * 1.03 || Box[3] > height * 1.03)
                return null;
            return Promille(new[]
            {
                (int)Math.Round(Box[0] * 1000.0 / width), (int)Math.Round(Box[1] * 1000.0 / height),
                (int)Math.Round(Box[2] * 1000.0 / width), (int)Math.Round(Box[3] * 1000.0 / height),
            });
        }

        /// <summary>Der Kasten, wenn er brauchbar ist: vier Werte, in 0..1000 geklemmt, Ecken sortiert, nicht leer —
        /// sonst <c>null</c>. Ein Modell kann Ecken vertauschen oder über den Rand greifen; ein kaputter Kasten soll
        /// die Korrekturseite nicht mit einem leeren Ausschnitt füllen.</summary>
        public int[]? NormalizedBox() => Box is { Count: 4 } ? Promille(Box.ToArray()) : null;

        private static int[]? Promille(int[] box)
        {
            var c = box.Select(v => Math.Clamp(v, 0, 1000)).ToArray();
            int x0 = Math.Min(c[0], c[2]), x1 = Math.Max(c[0], c[2]), y0 = Math.Min(c[1], c[3]), y1 = Math.Max(c[1], c[3]);
            return x1 - x0 < 2 || y1 - y0 < 2 ? null : new[] { x0, y0, x1, y1 };
        }

        public ScannedPly ToScanned() => new(Written ?? string.Empty, string.IsNullOrWhiteSpace(San) ? null : San,
            Alternatives?.Where(a => !string.IsNullOrWhiteSpace(a)).Take(3).ToList(), Confidence);
    }

    public List<ScannedPly> Scanned() => Moves.Select(m => m.ToScanned()).ToList();

    /// <summary><c>null</c> bei kaputtem JSON.</summary>
    public static ScoresheetTranscription? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var t = JsonSerializer.Deserialize<ScoresheetTranscription>(json, ScoresheetScanService.Json);
            if (t == null) return null;
            t.Moves = t.Moves.Take(600).ToList();
            return t;
        }
        catch (JsonException) { return null; }
    }
}

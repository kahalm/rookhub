using System.Globalization;

namespace RookHub.Api.Services.TrainingVideos;

/// <summary>Zeitmarken der Trainingsdateien: <c>h:mm:ss</c>, <c>m:ss</c> oder reine Sekunden ⇄ Sekunden.</summary>
public static class TrainingTime
{
    /// <summary>Sekunden zu <c>"0:04:32"</c>, <c>"4:32"</c>, <c>"272"</c> (auch mit Bruchteil <c>"4:32.5"</c>); <c>null</c> = unlesbar.</summary>
    public static int? ParseSeconds(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Trim().Split(':');
        if (parts.Length > 3) return null;
        double total = 0;
        foreach (var part in parts)
        {
            if (!double.TryParse(part, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v) || v < 0) return null;
            total = total * 60 + v;
        }
        return (int)Math.Floor(total);
    }

    /// <summary>Sekunden als <c>h:mm:ss</c> (immer mit Stunde, wie in den Dateien).</summary>
    public static string Format(int seconds)
    {
        if (seconds < 0) seconds = 0;
        return $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}";
    }
}

using System.Security.Cryptography;
using System.Text;

namespace RookHub.Api.Services;

/// <summary>
/// Der Fingerabdruck eines Kurs-Textes (<see cref="Models.CommentText.SourceHash"/>): 16 Hex-Zeichen,
/// Praefix von SHA-256 ueber den NORMALISIERTEN Text. Nirgends selbst hashen — Uebersetzer, Localizer und
/// Wiederverwendung muessen denselben Wert rechnen, sonst gilt jede Uebersetzung als veraltet.
///
/// <para><b>Normalisiert</b> heisst: <c>\r\n</c> → <c>\n</c>, aussen getrimmt. Mehr nicht — eine
/// geaenderte Zeichensetzung ist eine geaenderte Vorlage.</para>
///
/// <para><b>Warum 16 Zeichen reichen:</b> 64 Bit. Verglichen wird immer nur mit dem Text, der gerade an
/// derselben Stelle steht (veraltet?), bzw. unter Texten derselben Sprache (wiederverwenden?) — bei rund
/// 10⁷ Texten liegt die Wahrscheinlichkeit einer Kollision irgendwo im Bestand bei etwa 10⁻⁶.</para>
/// </summary>
public static class CourseTextHash
{
    /// <summary>Laenge des gespeicherten Fingerabdrucks (Spaltenbreite).</summary>
    public const int Length = 16;

    /// <summary>Der normalisierte Text — so geht er auch ans Modell.</summary>
    public static string Normalize(string text) => text.Replace("\r\n", "\n").Trim();

    /// <summary>Der Fingerabdruck des (noch nicht normalisierten) Textes, klein geschrieben.</summary>
    public static string Of(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(text)));
        return Convert.ToHexStringLower(bytes)[..Length];
    }
}

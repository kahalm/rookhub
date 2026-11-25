using System.Text;

namespace RookHub.Api.Services;

/// <summary>
/// Einzelne UTF-16-Surrogate (eine Hälfte eines Zeichenpaars ohne die andere) unschädlich machen (1.011.1). Anlass: die
/// Buch-Übersetzung 911 (Prod, 25.11.2026) brach bei Text 3236 von 4143 als GANZES ab — das Modell hatte in seiner
/// JSON-Antwort ein <c>\udc4b</c> (die untere Hälfte von 👋 U+1F44B) ohne die obere geschrieben. <c>JsonDocument.Parse</c>
/// lässt das durch, erst <c>JsonElement.GetString()</c> wirft — und zwar eine <see cref="InvalidOperationException"/>, keine
/// <see cref="System.Text.Json.JsonException"/>, an der jeder <c>catch (JsonException)</c> vorbeilief.
/// Ersatz ist immer U+FFFD (�), wie es auch <c>System.Text.Json</c> beim Schreiben tut.
/// </summary>
public static class Utf16Text
{
    public const char Replacement = '�';

    /// <summary>Enthält <paramref name="text"/> eine einzelne Surrogat-Hälfte?</summary>
    public static bool HasLoneSurrogate(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) { i++; continue; }
            if (char.IsSurrogate(c)) return true;
        }
        return false;
    }

    /// <summary>Jede einzelne Surrogat-Hälfte durch U+FFFD ersetzen; vollständige Paare bleiben.</summary>
    public static string ReplaceLoneSurrogates(string text)
    {
        if (!HasLoneSurrogate(text)) return text;
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                sb.Append(c).Append(text[++i]);
                continue;
            }
            sb.Append(char.IsSurrogate(c) ? Replacement : c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Höchstens <paramref name="max"/> UTF-16-Einheiten, ohne ein Zeichenpaar zu zerschneiden (ein <c>s[..max]</c> mitten
    /// in einem Emoji hinterließe eine einzelne Hälfte, die beim Schreiben in die Datenbank oder als JSON kaputtgeht).
    /// </summary>
    public static string Truncate(string text, int max)
    {
        if (text.Length <= max) return text;
        if (max <= 0) return "";
        var cut = max;
        if (char.IsHighSurrogate(text[cut - 1]) && char.IsLowSurrogate(text[cut])) cut--;
        return text[..cut];
    }

    /// <summary>
    /// JSON-Text reparieren, dessen Escapes einzelne Surrogate bilden (<c>"\udc4b"</c>, <c>"\ud83d x"</c>): jede solche
    /// Escape-Folge wird <c>�</c>, vollständige Paare (<c>👋</c>) und alle anderen Escapes (auch
    /// <c>\\udc4b</c> = ein Backslash als Text) bleiben, wie sie sind. Rohe einzelne Surrogate im Text ebenfalls → U+FFFD.
    /// Danach liest <c>GetString()</c> den Text ohne Ausnahme.
    /// </summary>
    public static string SanitizeJsonEscapes(string json)
    {
        if (json.IndexOf("\\u", StringComparison.OrdinalIgnoreCase) < 0) return ReplaceLoneSurrogates(json);
        var sb = new StringBuilder(json.Length);
        var i = 0;
        while (i < json.Length)
        {
            var c = json[i];
            if (c != '\\' || i + 1 >= json.Length)
            {
                if (char.IsHighSurrogate(c) && i + 1 < json.Length && char.IsLowSurrogate(json[i + 1]))
                {
                    sb.Append(c).Append(json[i + 1]);
                    i += 2;
                    continue;
                }
                sb.Append(char.IsSurrogate(c) ? Replacement : c);
                i++;
                continue;
            }
            if (json[i + 1] != 'u' || !TryHex4(json, i + 2, out var unit))
            {
                sb.Append(c).Append(json[i + 1]);   // \\ \" \n … unverändert (und damit nie als Beginn eines \u gelesen)
                i += 2;
                continue;
            }
            if (char.IsHighSurrogate(unit) && i + 7 < json.Length &&json[i + 6] == '\\' && json[i + 7] == 'u'
                && TryHex4(json, i + 8, out var low) && char.IsLowSurrogate(low))
            {
                sb.Append(json, i, 12);
                i += 12;
                continue;
            }
            if (char.IsSurrogate(unit)) sb.Append("\\uFFFD");
            else sb.Append(json, i, 6);
            i += 6;
        }
        return sb.ToString();
    }

    private static bool TryHex4(string s, int start, out char unit)
    {
        unit = '\0';
        if (start + 4 > s.Length) return false;
        var v = 0;
        for (var k = start; k < start + 4; k++)
        {
            var d = s[k] switch
            {
                >= '0' and <= '9' => s[k] - '0',
                >= 'a' and <= 'f' => s[k] - 'a' + 10,
                >= 'A' and <= 'F' => s[k] - 'A' + 10,
                _ => -1,
            };
            if (d < 0) return false;
            v = v * 16 + d;
        }
        unit = (char)v;
        return true;
    }
}

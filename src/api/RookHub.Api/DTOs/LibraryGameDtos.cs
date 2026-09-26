namespace RookHub.Api.DTOs;

/// <summary>
/// Eine Zeile der Bestandssuche — bewusst OHNE das PGN.
///
/// <para>Der Rohbestand hat 130 000 Zeilen mit zusammen 338 MB Partietext. Was die Auswahl braucht,
/// steht in den Zahlen daneben: wer gegen wen, wie lang, wie dicht kommentiert, von wem, in welcher
/// Sprache. Die Zuege selbst gehen erst mit, wenn jemand die Partie anfordert — und dann in die
/// Analyse, nicht in den Browser.</para>
/// </summary>
public class LibraryGameDto
{
    public int Id { get; set; }
    public string? White { get; set; }
    public string? Black { get; set; }
    public int? WhiteElo { get; set; }
    public int? BlackElo { get; set; }
    public string? Result { get; set; }
    public string? Event { get; set; }
    public DateOnly? PlayedOn { get; set; }
    public string? Eco { get; set; }
    public int? PlyCount { get; set; }

    public string? Annotator { get; set; }
    /// <summary>Wie viele HALBZUEGE einen Kommentar tragen — die Zahl, die ueber die Eignung entscheidet.</summary>
    public int? CommentedPlies { get; set; }
    public int? CommentChars { get; set; }
    public string? Languages { get; set; }
    /// <summary>Eignungsnote 0–100 (<c>GuessSuitability</c>); Vorgabe-Sortierung der Suche.</summary>
    public int? Score { get; set; }
    public string? SourceTitle { get; set; }

    /// <summary>Diese Partie liegt schon im KURATIERTEN Bestand — jeder kann sie sofort spielen.</summary>
    public bool InPool { get; set; }
    /// <summary>Der Aufrufer hat sie bereits angefordert; <c>GameAnalysisId</c> zeigt auf seine Analyse.</summary>
    public bool Requested { get; set; }
    /// <summary>Die spielbare Analyse — die eigene angeforderte oder die des kuratierten Bestands.</summary>
    public int? GameAnalysisId { get; set; }
}

/// <summary>Eine Seite der Bestandssuche.</summary>
public class LibraryGamePageDto
{
    public List<LibraryGameDto> Items { get; set; } = new();
    /// <summary>Gesamtzahl der Treffer — die Suche zeigt sie, damit man weiss, ob es sich lohnt,
    /// weiter einzugrenzen.</summary>
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

/// <summary>Warum eine Anforderung abgelehnt wurde — dieselben Gruende wie beim Einwurf, plus
/// „gibt es nicht".</summary>
public static class LibraryRequestReason
{
    public const string NotFound = "not-found";
    public const string TooManyOpen = "too-many-open";
    public const string NoEngine = "no-engine";
    public const string InvalidPgn = "invalid-pgn";
}

/// <summary>Ergebnis einer Anforderung: die (neue oder schon vorhandene) Analyse, oder ein Grund.</summary>
public record LibraryRequestResult(GameAnalysisDto? Analysis, string? Reason, bool AlreadyPlayable);

/// <summary>„Frag die Kommentare" (0.536.0): Treffer der semantischen Suche, je Partie die passendsten Stellen.</summary>
public class LibrarySemanticPageDto
{
    /// <summary>Ein Embedding-Modell ist eingerichtet — ohne gibt es diese Suche nicht.</summary>
    public bool Available { get; set; }
    /// <summary>So viele Textstücke sind eingebettet (0 = der Bestand wurde noch nicht vorbereitet).</summary>
    public long Indexed { get; set; }
    public List<LibrarySemanticHitDto> Items { get; set; } = new();
}

public class LibrarySemanticHitDto
{
    public LibraryGameDto Game { get; set; } = new();
    /// <summary>Die passendsten Stücke dieser Partie, bestes zuerst.</summary>
    public List<LibrarySemanticMatchDto> Matches { get; set; } = new();
}

public class LibrarySemanticMatchDto
{
    /// <summary>Erster kommentierter Halbzug des Stücks (−1 = Einleitung) — dorthin springt die Partie.</summary>
    public int FromPly { get; set; }
    public string Text { get; set; } = string.Empty;
    /// <summary>Ähnlichkeit 0..1 (1 − Kosinus-Abstand).</summary>
    public double Score { get; set; }
}

/// <summary>„Ähnliche Meisterpartien" (0.543.0) zu einer gespeicherten Partie (<c>GET /api/games/{id}/similar</c>,
/// <c>…/shared/{token}/similar</c>).</summary>
public class SimilarGamesDto
{
    /// <summary>Eröffnungsname aus dem PGN der Partie (<c>[Opening]</c> bzw. chess.coms <c>[ECOUrl]</c>), sonst <c>null</c>.</summary>
    public string? Opening { get; set; }
    /// <summary>So viele Halbzüge teilt die ähnlichste Meisterpartie (0 = keine gefunden).</summary>
    public int SharedPlies { get; set; }
    /// <summary>Diese Züge, nummeriert („1.e4 c6 2.d4 d5 3.f3").</summary>
    public string? SharedLine { get; set; }
    public List<SimilarGameDto> Items { get; set; } = new();
}

public class SimilarGameDto
{
    /// <summary>Die Meisterpartie — wie in der Bestandssuche, ohne Züge, mit <c>inPool</c>/<c>requested</c>.</summary>
    public LibraryGameDto Game { get; set; } = new();
    public int SharedPlies { get; set; }
    /// <summary>Der letzte gemeinsame Zug („3...dxe4").</summary>
    public string? LastSharedMove { get; set; }
    /// <summary>Der Zug des Meisters an der Abzweigung („4.Nc3"); <c>null</c>, wenn seine Zeile dort endet.</summary>
    public string? MasterMove { get; set; }
    /// <summary>Der Zug der eigenen Partie an derselben Stelle („4.fxe4").</summary>
    public string? GameMove { get; set; }
}

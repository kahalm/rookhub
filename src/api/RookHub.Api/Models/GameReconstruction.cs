namespace RookHub.Api.Models;

/// <summary>Art eines Bruchstücks: eine Zugfolge oder eine Stellung.</summary>
public enum ReconstructionPartKind
{
    /// <summary>SAN-Zugfolge („e4 e5 Nf3"). Zugnummern und Ergebnis werden beim Speichern entfernt.</summary>
    Moves = 0,
    /// <summary>Eine Stellung als FEN — der Anker, an dem die nächste Zugfolge weitergeht.</summary>
    Position = 1,
}

/// <summary>
/// Eine Partie, die aus Bruchstücken wieder zusammengesetzt wird („Partie rekonstruieren").
///
/// <para>Der Anlass: von einer am Brett gespielten Partie weiß man meist die ersten Züge
/// vollständig, danach nur noch einzelne Stellungen und Zugfolgen. Dieses Modell hält genau das
/// fest — eine GEORDNETE Liste von Bruchstücken (<see cref="GameReconstructionPart"/>), nicht eine
/// fertige Zugfolge. Lücken sind der Normalfall und ausdrücklich erlaubt; sie zu schließen ist ein
/// späterer Schritt (Suche), hier wird nur aufgezeichnet und ergänzt.</para>
///
/// <para>Bewusst NICHT als PGN mit Kommentaren gespeichert: ein PGN kann eine Stellung ohne den Weg
/// dorthin nicht ausdrücken, und genau diese Teile sind hier die Daten. Was sich verketten lässt,
/// rechnet <see cref="Services.ReconstructionChain"/> beim Lesen aus.</para>
/// </summary>
public class GameReconstruction
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>Frei gewählter Titel („Vereinsmeisterschaft 2026, Runde 3").</summary>
    public string Title { get; set; } = string.Empty;

    public string? White { get; set; }
    public string? Black { get; set; }
    public string? Event { get; set; }

    /// <summary>Wann die Partie gespielt wurde, falls bekannt.</summary>
    public DateOnly? PlayedOn { get; set; }

    /// <summary>Ergebnis (<c>1-0</c>/<c>0-1</c>/<c>1/2-1/2</c>/<c>*</c>), falls bekannt.</summary>
    public string? Result { get; set; }

    /// <summary>Freitext zur ganzen Partie (Erinnerungen, offene Fragen).</summary>
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<GameReconstructionPart> Parts { get; set; } = new();
}

/// <summary>
/// EIN Bruchstück einer rekonstruierten Partie: entweder eine Zugfolge oder eine Stellung.
///
/// <para><see cref="Ordinal"/> ist die Reihenfolge, in der die Teile in der Partie vorkommen —
/// sie ist die einzige Aussage über den Zusammenhang, solange keine Halbzug-Nummer bekannt ist.
/// <see cref="FromPly"/> ist ein optionaler Anker („das war Zug 21", 0-basiert als Halbzug); ohne
/// ihn steht das Teil einfach zwischen seinen Nachbarn.</para>
/// </summary>
public class GameReconstructionPart
{
    public int Id { get; set; }

    public int GameReconstructionId { get; set; }
    public GameReconstruction? Reconstruction { get; set; }

    /// <summary>Position in der Partie (0-basiert, lückenlos innerhalb einer Rekonstruktion).</summary>
    public int Ordinal { get; set; }

    public ReconstructionPartKind Kind { get; set; }

    /// <summary>Nur bei <see cref="ReconstructionPartKind.Moves"/>: die Züge in SAN, durch Leerzeichen getrennt.</summary>
    public string? Moves { get; set; }

    /// <summary>Nur bei <see cref="ReconstructionPartKind.Position"/>: die Stellung als FEN.</summary>
    public string? Fen { get; set; }

    /// <summary>
    /// Schließt dieses Teil NAHTLOS an das vorige an? Vorgabe ist <c>false</c>: Bruchstücke stammen
    /// von verschiedenen Stellen der Partie — hingen sie aneinander, wären sie ein Teil. Erst mit
    /// diesem Haken wird die Zugfolge an der Stellung davor geprüft (und zählt zur bekannten Partie).
    /// </summary>
    public bool ContinuesPrevious { get; set; }

    /// <summary>
    /// Bin ich mir bei diesem Bruchstück SICHER? Vorgabe <c>true</c> — wer etwas aufschreibt, meint
    /// es zunächst; die Auskunft, auf die es ankommt, ist das Gegenteil („hier bin ich mir nicht
    /// sicher"). Eine unsichere Stellung ist ein Kandidat für einen zweiten Blick, wenn die Lücke
    /// daneben nicht aufgeht — und genau deshalb steht sie am TEIL und nicht in einer Notiz.
    /// </summary>
    public bool Certain { get; set; } = true;

    /// <summary>
    /// Nur bei <see cref="ReconstructionPartKind.Moves"/> OHNE Anschluss: beginnt das Bruchstück mit
    /// einem Zug von SCHWARZ? Bei einer Stellung steht die Seite in der FEN, und bei einem Teil, das
    /// an das vorige anschließt, in der Stellung davor — dort wird dieses Feld nicht gelesen.
    ///
    /// <para>Ohne die Angabe ließe sich „und dann schlug er auf f7" gar nicht aufzeichnen: das Brett
    /// im Editor stünde auf Weiß am Zug, und der erinnerte Zug wäre nicht spielbar. Beim ERSTEN Teil
    /// heißt der Haken außerdem, dass es NICHT die Eröffnung ist — eine Partie fängt nicht mit einem
    /// schwarzen Zug an; das Bruchstück hängt dann an keiner bekannten Stellung.</para>
    /// </summary>
    public bool BlackToMove { get; set; }

    /// <summary>Bekannter Halbzug, an dem dieses Teil beginnt (0 = Grundstellung); null = unbekannt.</summary>
    public int? FromPly { get; set; }

    /// <summary>Freitext zu diesem Teil („danach kam ein Turmtausch").</summary>
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

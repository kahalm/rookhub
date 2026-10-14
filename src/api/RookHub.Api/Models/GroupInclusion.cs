namespace RookHub.Api.Models;

/// <summary>Gruppe in Gruppe (Wunsch 2026-10-14: „bei Gruppen auch Gruppen Gruppen zuweisen — dann sind alle User der
/// ersten Gruppe auch in der 2."): alle Mitglieder von <see cref="IncludedGroupId"/> gelten zusätzlich als Mitglieder von
/// <see cref="GroupId"/> — mit deren Rollen, Freigaben, Vereinen und Menüpunkten. Wirkt über mehrere Stufen; die
/// aufgelöste Hülle steht in <see cref="GroupClosure"/> (gepflegt von <c>GroupMembership.RebuildClosureAsync</c>).</summary>
public class GroupInclusion
{
    /// <summary>Die umfassende Gruppe.</summary>
    public int GroupId { get; set; }
    public Group? Group { get; set; }

    /// <summary>Die enthaltene Gruppe — ihre Mitglieder zählen in <see cref="GroupId"/> mit.</summary>
    public int IncludedGroupId { get; set; }
    public Group? IncludedGroup { get; set; }
}

/// <summary>Transitive Hülle von <see cref="GroupInclusion"/>: Mitglieder von <see cref="DescendantId"/> sind (über eine
/// oder mehrere Stufen) auch in <see cref="AncestorId"/>. Ohne Selbst-Zeilen — die direkte Mitgliedschaft steht in
/// <see cref="UserGroup"/>. Nur abgeleitet, nie von Hand schreiben: so bleibt „ist das Konto in Gruppe X?" eine
/// einfache SQL-Abfrage statt einer Rekursion.</summary>
public class GroupClosure
{
    public int AncestorId { get; set; }
    public int DescendantId { get; set; }
}

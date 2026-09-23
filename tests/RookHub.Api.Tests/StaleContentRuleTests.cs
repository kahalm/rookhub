using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// <see cref="StaleContentRule"/> ist die EINE Regel für Status, Lauf und die (!)-Markierung der Listen. Hier nur
/// die reine Entscheidung; dass Status und Lauf ihr folgen, prüfen <c>ImportReprocessServiceTests</c> und
/// <c>ReprocessWithoutChessableTests</c>.
/// </summary>
public class StaleContentRuleTests
{
    [Theory]
    // Kein Chessable-Repertoire: das PGN IST die Quelle → Versions-Mark, auch wenn es oids trägt (von Hand
    // hochgeladenes piratechess-PGN ohne Kurs-Id).
    [InlineData(false, false, true, StaleAction.Local)]
    [InlineData(false, true, true, StaleAction.Local)]
    [InlineData(false, false, false, StaleAction.Local)]
    // Chessable MIT oids → immer aus dem Linien-Cache, auch mit eigenem Chessable-Weg und auch ohne ihn.
    [InlineData(true, true, true, StaleAction.Cache)]
    [InlineData(true, true, false, StaleAction.Cache)]
    // Chessable OHNE oids → nur ein echter Abruf bringt sie; ohne eigenen Weg ein Showstopper.
    [InlineData(true, false, true, StaleAction.Refetch)]
    [InlineData(true, false, false, StaleAction.Manual)]
    public void ActionForRepertoire_FolgtDerselbenReihenfolgeWieBeiKursen(
        bool isChessable, bool sourceModern, bool chessableEnabled, StaleAction expected)
    {
        Assert.Equal(expected, StaleContentRule.ActionForRepertoire(isChessable, sourceModern, chessableEnabled));
    }

    [Fact]
    public void ActionForRepertoire_ChessableMitOids_NieMehrNurVersionsMark()
    {
        // Ein Versions-Mark setzte das Repertoire auf die aktuelle Version, ohne dass sich sein Text geändert
        // hätte — danach wäre es für den Cache-Weg verbrannt (dieselbe Begründung wie bei ActionForBook).
        Assert.NotEqual(StaleAction.Local, StaleContentRule.ActionForRepertoire(true, true, true));
        Assert.NotEqual(StaleAction.Local, StaleContentRule.ActionForRepertoire(true, true, false));
    }
}

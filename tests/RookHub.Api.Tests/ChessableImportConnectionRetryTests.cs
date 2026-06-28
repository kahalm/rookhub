using System.Net;
using System.Net.Sockets;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Verbindungs-Resilienz: reine Transport-/Verbindungsfehler zu piratechess (Container-Neustart/
/// kurzer Ausfall) dürfen NICHT sofort als Import-Fehler gewertet werden (sonst killt ein kurzer
/// Recreate die ganze Queue). Echte Antwort-Fehler (ChessableProxyException) sind dagegen final.
/// </summary>
public class ChessableImportConnectionRetryTests
{
    [Fact]
    public void HttpRequestException_IsTransient()
        => Assert.True(ChessableImportService.IsTransientConnectionError(
            new HttpRequestException("Connection refused (piratechess-api:8080)")));

    [Fact]
    public void SocketException_IsTransient()
        => Assert.True(ChessableImportService.IsTransientConnectionError(
            new SocketException((int)SocketError.ConnectionRefused)));

    [Fact]
    public void InnerSocketException_IsTransient()
        => Assert.True(ChessableImportService.IsTransientConnectionError(
            new Exception("wrap", new SocketException((int)SocketError.HostUnreachable))));

    [Fact]
    public void HttpTimeout_TaskCanceled_IsTransient()
        => Assert.True(ChessableImportService.IsTransientConnectionError(
            new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout")));

    [Theory]
    [InlineData("Name or service not known")]
    [InlineData("No route to host")]
    [InlineData("connection timed out")]
    public void TypicalTransportMessages_AreTransient(string msg)
        => Assert.True(ChessableImportService.IsTransientConnectionError(new Exception(msg)));

    [Fact]
    public void ChessableProxyException_IsNotTransient()
        => Assert.False(ChessableImportService.IsTransientConnectionError(
            new ChessableProxyException(HttpStatusCode.BadRequest, "Bearer ungültig")));

    [Fact]
    public void GenericError_IsNotTransient()
        => Assert.False(ChessableImportService.IsTransientConnectionError(
            new InvalidOperationException("Kurs-Abruf fehlgeschlagen")));
}

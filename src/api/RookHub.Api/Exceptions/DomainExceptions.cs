namespace RookHub.Api.Exceptions;

// Domänen-Ausnahmen: ein Dienst meldet damit einen ERWARTETEN Fehlerfall, und der globale
// DomainExceptionFilter macht daraus die bisherige Antwortform { message } mit festem Status.
// Die Controller fangen dafür nichts mehr selbst.
//
// Warum eigene Typen: Bisher fingen die Controller die BCL-Typen (KeyNotFound, InvalidOperation,
// Argument, UnauthorizedAccess) von Hand und gaben ex.Message zurück. Damit wurde auch jeder echte
// Programmierfehler dieser Typen — ein Dictionary-Zugriff, ein LINQ-First ohne Treffer, ein
// verworfener DbContext (ObjectDisposedException erbt von InvalidOperationException) — still zu
// einem 4xx mit internem Framework-Text, ohne Log. Mit eigenen Typen bleibt die Grenze scharf:
// nur was ein Dienst bewusst als Domänenfehler wirft, wird 4xx; alles andere läuft in den globalen
// Handler (500 + Error-Log mit Stacktrace).
//
// DIE MELDUNG GEHT WÖRTLICH AN DEN AUFRUFER. Nur für Nutzer geschriebene Texte übergeben — keine
// Ausnahmetexte, keine Ids fremder Datensätze, keine Pfade.
//
// Die Typen erben bewusst vom jeweiligen BCL-Typ, den die Controller bisher fingen: Solange noch
// nicht alle Controller umgestellt sind, darf ein Dienst schon Domänen-Ausnahmen werfen, und ein
// alter catch (KeyNotFoundException …) fängt sie weiterhin (Strangler-Umbau, Controller für
// Controller). Beim Umstellen eines Dienstes trotzdem jeden Aufrufer prüfen: DomainValidation erbt
// von InvalidOperation — ein alter catch (ArgumentException) sieht sie nicht, ein alter
// catch (InvalidOperationException) → 409 machte daraus 409.

/// <summary>Gesuchtes existiert nicht (oder ist für den Aufrufer unsichtbar) → 404.</summary>
public class NotFoundException : KeyNotFoundException
{
    public NotFoundException(string userMessage) : base(userMessage) { }
    public NotFoundException(string userMessage, Exception? inner) : base(userMessage, inner) { }
}

/// <summary>Anfrage verletzt eine fachliche Regel oder ist in diesem Zustand nicht erlaubt → 400.</summary>
public class DomainValidationException : InvalidOperationException
{
    public DomainValidationException(string userMessage) : base(userMessage) { }
    public DomainValidationException(string userMessage, Exception? inner) : base(userMessage, inner) { }
}

/// <summary>Anfrage kollidiert mit dem aktuellen Stand (Dublette, schon erledigt, gleichzeitig geändert) → 409.</summary>
public class ConflictException : InvalidOperationException
{
    public ConflictException(string userMessage) : base(userMessage) { }
    public ConflictException(string userMessage, Exception? inner) : base(userMessage, inner) { }
}

/// <summary>Angemeldet, aber für DIESE Sache nicht berechtigt → 403.</summary>
public class ForbiddenException : UnauthorizedAccessException
{
    public ForbiddenException(string userMessage) : base(userMessage) { }
    public ForbiddenException(string userMessage, Exception? inner) : base(userMessage, inner) { }
}

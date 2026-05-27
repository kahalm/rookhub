using System.Text.RegularExpressions;

namespace RookHub.Api.Validation;

public static partial class TournamentIdValidator
{
    // Tournament IDs: numeric DB IDs or alphanumeric ChessResults IDs, max 20 chars
    [GeneratedRegex(@"^[a-zA-Z0-9]{1,20}$")]
    private static partial Regex ValidIdPattern();

    public static bool IsValid(string? id)
        => !string.IsNullOrEmpty(id) && ValidIdPattern().IsMatch(id);
}

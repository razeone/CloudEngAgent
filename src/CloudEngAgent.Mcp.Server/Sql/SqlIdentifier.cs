using System.Diagnostics.CodeAnalysis;
using ModelContextProtocol;

namespace CloudEngAgent.Mcp.Server.Sql;

/// <summary>
/// Helpers for validating and quoting SQL identifiers (schema/table/column
/// names). The MCP tools never accept arbitrary SQL: every identifier passed
/// in by a caller must pass <see cref="IsValid"/> AND be present in
/// <c>INFORMATION_SCHEMA</c> before it is interpolated into a query.
/// Values are always sent as parameters; identifiers go through
/// <see cref="Quote(string)"/>.
/// </summary>
public static class SqlIdentifier
{
    private const int MaxLength = 128;

    /// <summary>
    /// Returns true when <paramref name="identifier"/> is a "safe" SQL
    /// identifier: 1-128 chars, starts with a letter or underscore, and
    /// contains only ASCII letters, digits, and underscores. We deliberately
    /// reject anything else (dots, brackets, quotes, spaces, hyphens) so that
    /// SQL-injection attempts via identifier slots are rejected before any
    /// query is built.
    /// </summary>
    public static bool IsValid([NotNullWhen(true)] string? identifier)
    {
        if (string.IsNullOrEmpty(identifier) || identifier.Length > MaxLength)
        {
            return false;
        }

        var first = identifier[0];
        if (!IsAsciiLetter(first) && first != '_')
        {
            return false;
        }

        for (var i = 1; i < identifier.Length; i++)
        {
            var c = identifier[i];
            if (!IsAsciiLetter(c) && !IsAsciiDigit(c) && c != '_')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Validates and quotes <paramref name="identifier"/> using T-SQL bracket
    /// quoting (e.g., <c>dbo</c> → <c>[dbo]</c>). Throws
    /// <see cref="McpException"/> with <see cref="McpErrorCode.InvalidParams"/>
    /// when the identifier fails <see cref="IsValid"/>.
    /// </summary>
    public static string Quote(string? identifier)
    {
        if (!IsValid(identifier))
        {
            throw new McpException(
                $"Invalid SQL identifier '{identifier}'. Only ASCII letters, digits, and underscores are allowed (must start with a letter or underscore, max {MaxLength} chars).",
                McpErrorCode.InvalidParams);
        }

        // Identifier is already constrained to [A-Za-z0-9_], so no embedded
        // ']' is possible — bracket quoting is sufficient. Defense-in-depth:
        // double any ']' anyway in case the validation surface ever changes.
        return "[" + identifier.Replace("]", "]]", StringComparison.Ordinal) + "]";
    }

    private static bool IsAsciiLetter(char c) =>
        (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

    private static bool IsAsciiDigit(char c) => c >= '0' && c <= '9';
}

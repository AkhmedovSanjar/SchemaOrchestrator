using System.Text;
using Microsoft.EntityFrameworkCore;
using SchemaOrchestrator.Models;

namespace SchemaOrchestrator.Services;

internal static class CreateScriptSnapshotBuilder
{
    public static DatabaseSchema Build(DbContext dbContext, string defaultSchema)
    {
        var script = dbContext.Database.GenerateCreateScript();
        var statements = SplitStatements(script);
        var tables = new List<TableSchema>();
        var indexes = new List<IndexSchema>();

        foreach (var statement in statements)
        {
            var trimmed = statement.Trim();
            if (!trimmed.StartsWith("CREATE TABLE", StringComparison.OrdinalIgnoreCase))
            {
                if (trimmed.StartsWith("CREATE INDEX", StringComparison.OrdinalIgnoreCase)
                    || trimmed.StartsWith("CREATE UNIQUE INDEX", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryParseIndex(trimmed, defaultSchema, out var index))
                    {
                        indexes.Add(index);
                    }
                }

                continue;
            }

            if (!TryParseTable(trimmed, defaultSchema, out var table))
            {
                continue;
            }

            tables.Add(table);
        }

        return new DatabaseSchema
        {
            Tables = tables
                .GroupBy(t => t.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.Last())
                .OrderBy(t => t.Key, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Indexes = indexes
                .GroupBy(i => i.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.Last())
                .OrderBy(i => i.Key, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static List<string> SplitStatements(string script)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var inSingleQuote = false;

        foreach (var ch in script)
        {
            if (ch == '\'')
            {
                inSingleQuote = !inSingleQuote;
            }

            if (ch == ';' && !inSingleQuote)
            {
                var statement = sb.ToString();
                if (!string.IsNullOrWhiteSpace(statement))
                {
                    result.Add(statement);
                }

                sb.Clear();
                continue;
            }

            sb.Append(ch);
        }

        if (sb.Length > 0)
        {
            var statement = sb.ToString();
            if (!string.IsNullOrWhiteSpace(statement))
            {
                result.Add(statement);
            }
        }

        return result;
    }

    private static bool TryParseTable(string statement, string defaultSchema, out TableSchema table)
    {
        table = default!;

        var openParen = statement.IndexOf('(');
        if (openParen < 0)
        {
            return false;
        }

        var header = statement[..openParen].Trim();
        var tableNameToken = header["CREATE TABLE".Length..].Trim();
        if (string.IsNullOrWhiteSpace(tableNameToken))
        {
            return false;
        }

        ParseQualifiedName(tableNameToken, defaultSchema, out var schema, out var name);

        var closeParen = statement.LastIndexOf(')');
        if (closeParen <= openParen)
        {
            return false;
        }

        var body = statement[(openParen + 1)..closeParen];
        var columnLines = SplitTopLevel(body);
        var columns = new List<ColumnSchema>();

        foreach (var rawLine in columnLines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.StartsWith("CONSTRAINT", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("PRIMARY KEY", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("FOREIGN KEY", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("UNIQUE", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("CHECK", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!TryParseColumn(line, out var column))
            {
                continue;
            }

            columns.Add(column);
        }

        table = new TableSchema
        {
            Schema = schema,
            Name = name,
            Columns = columns
        };

        return true;
    }

    private static void ParseQualifiedName(string token, string defaultSchema, out string schema, out string table)
    {
        token = token.Trim();
        var parts = token.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(UnquoteIdentifier)
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        if (parts.Length >= 2)
        {
            schema = parts[^2];
            table = parts[^1];
            return;
        }

        schema = defaultSchema;
        table = parts.Length == 1 ? parts[0] : UnquoteIdentifier(token);
    }

    private static bool TryParseColumn(string line, out ColumnSchema column)
    {
        column = default!;

        var name = ReadLeadingIdentifier(line, out var indexAfterName);
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var rest = line[indexAfterName..].Trim();
        if (string.IsNullOrWhiteSpace(rest))
        {
            return false;
        }

        var storeType = ExtractStoreType(rest);
        var isNullable = !rest.Contains("NOT NULL", StringComparison.OrdinalIgnoreCase);
        var isPrimaryKey = rest.Contains("PRIMARY KEY", StringComparison.OrdinalIgnoreCase);

        column = new ColumnSchema(name, storeType, isNullable, isPrimaryKey);
        return true;
    }

    private static string ExtractStoreType(string rest)
    {
        var keywords = new[]
        {
            " NOT ", " NULL", " CONSTRAINT", " PRIMARY", " REFERENCES", " DEFAULT", " CHECK", " COLLATE", " GENERATED", " IDENTITY", " AUTOINCREMENT"
        };

        var cut = rest.Length;
        foreach (var keyword in keywords)
        {
            var idx = rest.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0 && idx < cut)
            {
                cut = idx;
            }
        }

        var typeToken = rest[..cut].Trim();
        return string.IsNullOrWhiteSpace(typeToken) ? "TEXT" : typeToken;
    }

    private static List<string> SplitTopLevel(string text)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var depth = 0;
        var inSingleQuote = false;

        foreach (var ch in text)
        {
            if (ch == '\'')
            {
                inSingleQuote = !inSingleQuote;
            }

            if (!inSingleQuote)
            {
                if (ch == '(') depth++;
                if (ch == ')') depth--;
            }

            if (ch == ',' && depth == 0 && !inSingleQuote)
            {
                result.Add(sb.ToString());
                sb.Clear();
                continue;
            }

            sb.Append(ch);
        }

        if (sb.Length > 0)
        {
            result.Add(sb.ToString());
        }

        return result;
    }

    private static string ReadLeadingIdentifier(string text, out int endIndex)
    {
        text = text.TrimStart();
        var offset = 0;

        if (text.StartsWith("[", StringComparison.Ordinal))
        {
            var end = text.IndexOf(']');
            if (end < 0)
            {
                endIndex = 0;
                return string.Empty;
            }

            endIndex = offset + end + 1;
            return text[1..end];
        }

        if (text.StartsWith("\"", StringComparison.Ordinal))
        {
            var end = text.IndexOf('"', 1);
            if (end < 0)
            {
                endIndex = 0;
                return string.Empty;
            }

            endIndex = offset + end + 1;
            return text[1..end];
        }

        var i = 0;
        while (i < text.Length && !char.IsWhiteSpace(text[i]))
        {
            i++;
        }

        endIndex = i;
        return i == 0 ? string.Empty : text[..i];
    }

    private static string UnquoteIdentifier(string value)
    {
        value = value.Trim();
        if (value.StartsWith("[") && value.EndsWith("]") && value.Length > 1)
        {
            return value[1..^1];
        }

        if (value.StartsWith("\"") && value.EndsWith("\"") && value.Length > 1)
        {
            return value[1..^1];
        }

        return value;
    }

    private static bool TryParseIndex(string statement, string defaultSchema, out IndexSchema index)
    {
        index = default!;

        var normalized = statement.Trim();
        var isUnique = normalized.StartsWith("CREATE UNIQUE INDEX", StringComparison.OrdinalIgnoreCase);
        var token = isUnique ? "CREATE UNIQUE INDEX" : "CREATE INDEX";
        var afterKeyword = normalized[token.Length..].Trim();
        var onIdx = afterKeyword.IndexOf(" ON ", StringComparison.OrdinalIgnoreCase);
        if (onIdx < 0)
        {
            return false;
        }

        var indexName = UnquoteIdentifier(afterKeyword[..onIdx].Trim());
        var afterOn = afterKeyword[(onIdx + 4)..].Trim();
        var open = afterOn.IndexOf('(');
        var close = afterOn.LastIndexOf(')');
        if (open < 0 || close <= open)
        {
            return false;
        }

        var tableToken = afterOn[..open].Trim();
        ParseQualifiedName(tableToken, defaultSchema, out var schema, out var table);
        var columnsRaw = afterOn[(open + 1)..close];
        var columns = columnsRaw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(UnquoteIdentifier)
            .Select(static c => c.Trim())
            .Where(static c => !string.IsNullOrWhiteSpace(c))
            .ToArray();

        if (columns.Length == 0)
        {
            return false;
        }

        index = new IndexSchema(schema, table, indexName, columns, isUnique);
        return true;
    }
}

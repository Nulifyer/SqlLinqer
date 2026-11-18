using System;
using System.Text;

namespace SqlLinqer.Compilation
{
    // Finds @name value markers in hand-written SQL. String literals, quoted identifiers,
    // comments, and dollar-quoted bodies are copied unchanged, so text such as an email
    // address inside a literal is never treated as a marker.
    internal static class SqlMarkers
    {
        internal static string Replace(string sql, DbFlavor flavor, Func<string, string> replace)
        {
            bool backslashEscapes = flavor == DbFlavor.MySql;
            bool brackets = flavor == DbFlavor.SqlServer || flavor == DbFlavor.AzureSql || flavor == DbFlavor.Sqlite;
            bool dollarQuotes = flavor == DbFlavor.PostgreSql || flavor == DbFlavor.DuckDb;
            var result = new StringBuilder(sql.Length + 16);
            int position = 0;
            while (position < sql.Length)
            {
                char current = sql[position];
                char next = position + 1 < sql.Length ? sql[position + 1] : '\0';
                int end;
                if (current == '\'')
                {
                    // PostgreSQL E'...' strings use backslash escapes.
                    bool escapes = backslashEscapes || (flavor == DbFlavor.PostgreSql && position > 0
                        && (sql[position - 1] == 'E' || sql[position - 1] == 'e')
                        && (position == 1 || !IsNamePart(sql[position - 2])));
                    end = Quoted(sql, position, '\'', escapes);
                }
                else if (current == '"') end = Quoted(sql, position, '"', backslashEscapes);
                else if (current == '`') end = Quoted(sql, position, '`', false);
                else if (current == '[' && brackets) end = Quoted(sql, position, ']', false);
                else if (current == '-' && next == '-') end = LineEnd(sql, position);
                else if (current == '#' && flavor == DbFlavor.MySql) end = LineEnd(sql, position);
                else if (current == '/' && next == '*') end = BlockComment(sql, position);
                else if (current == '$' && dollarQuotes && (position == 0 || !IsNamePart(sql[position - 1]))
                    && DollarQuote(sql, position, out end)) { }
                else if (current == '@')
                {
                    end = position + 1;
                    if (next == '@')
                    {
                        // @@ROWCOUNT and the PostgreSQL @@ operator are not markers.
                        while (end < sql.Length && sql[end] == '@') end++;
                        while (end < sql.Length && IsNamePart(sql[end])) end++;
                    }
                    else if (IsNameStart(next) && (position == 0 || (!IsNamePart(sql[position - 1]) && sql[position - 1] != ':')))
                    {
                        while (end < sql.Length && IsNamePart(sql[end])) end++;
                        result.Append(replace(sql.Substring(position + 1, end - position - 1)));
                        position = end;
                        continue;
                    }
                }
                else end = position + 1;
                result.Append(sql, position, end - position);
                position = end;
            }
            return result.ToString();
        }

        private static bool IsNameStart(char value) => value == '_' || (value >= 'A' && value <= 'Z') || (value >= 'a' && value <= 'z');
        private static bool IsNamePart(char value) => IsNameStart(value) || (value >= '0' && value <= '9');

        private static int Quoted(string sql, int start, char close, bool backslashEscapes)
        {
            for (int i = start + 1; i < sql.Length; i++)
            {
                if (backslashEscapes && sql[i] == '\\') { i++; continue; }
                if (sql[i] != close) continue;
                if (i + 1 < sql.Length && sql[i + 1] == close) { i++; continue; }
                return i + 1;
            }
            return sql.Length;
        }

        private static int LineEnd(string sql, int start)
        {
            int end = sql.IndexOf('\n', start);
            return end < 0 ? sql.Length : end + 1;
        }

        private static int BlockComment(string sql, int start)
        {
            int depth = 1;
            for (int i = start + 2; i < sql.Length - 1; i++)
            {
                if (sql[i] == '/' && sql[i + 1] == '*') { depth++; i++; }
                else if (sql[i] == '*' && sql[i + 1] == '/')
                {
                    i++;
                    if (--depth == 0) return i + 1;
                }
            }
            return sql.Length;
        }

        private static bool DollarQuote(string sql, int start, out int end)
        {
            end = start + 1;
            int tagEnd = start + 1;
            if (tagEnd < sql.Length && IsNameStart(sql[tagEnd]))
                while (tagEnd < sql.Length && IsNamePart(sql[tagEnd])) tagEnd++;
            if (tagEnd >= sql.Length || sql[tagEnd] != '$') return false;
            string tag = sql.Substring(start, tagEnd - start + 1);
            int close = sql.IndexOf(tag, tagEnd + 1, StringComparison.Ordinal);
            end = close < 0 ? sql.Length : close + tag.Length;
            return true;
        }
    }
}

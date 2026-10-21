using System;
using System.Security.Cryptography;
using SqlLinqer.Connections;

namespace SqlLinqer.Extensions.StringExtensions
{
    /// <summary>
    /// A set of string extensions
    /// </summary>
    public static class StringExtensions
    {
        /// <summary>
        /// Returns the MD5 hash of the string in GUID format.
        /// </summary>
        /// <param name="str">The current string</param>
        public static string GetHashString(this string str)
        {
            using (var provider = new MD5CryptoServiceProvider())
            {
                byte[] bytes = System.Text.Encoding.Default.GetBytes(str);
                byte[] hashed = provider.ComputeHash(bytes);
                return new Guid(hashed).ToString("n");
            }
        }

        /// <summary>
        /// Get a random GUID string
        /// </summary>
        public static string GetRandomHashString()
        {
            return Guid.NewGuid().ToString("n");
        }
        /// <summary>
        /// Quotes a SQL identifier for the selected dialect, escaping embedded closing delimiters.
        /// Existing delimiters are preserved for legacy server dialects.
        /// </summary>
        /// <param name="str">The current string</param>
        /// <param name="flavor">The type of SQL database</param>
        public static string DbWrap(this string str, DbFlavor flavor)
        {
            if (flavor == DbFlavor.Sqlite || flavor == DbFlavor.DuckDb)
                return "\"" + str.Replace("\"", "\"\"") + "\"";
            char begin, end;

            switch (flavor)
            {
                case DbFlavor.PostgreSql:
                    begin = end = '"';
                    break;
                case DbFlavor.MySql:
                    begin = end = '`';
                    break;
                case DbFlavor.SqlServer:
                case DbFlavor.AzureSql:
                    begin = '[';
                    end = ']';
                    break;
                default:
                    throw new NotSupportedException($"The {nameof(SqlLinqer.DbFlavor)} {flavor} is not supported");
            }

            if (str.StartsWith(begin.ToString()) && str.EndsWith(end.ToString()))
                return str;
            else
                return $"{begin}{str.Replace(end.ToString(), new string(end, 2))}{end}";
        }
        /// <summary>
        /// Quotes a SQL string literal, escaping embedded apostrophes.
        /// Existing quotes are preserved for legacy server dialects. Use parameters for application values.
        /// </summary>
        /// <param name="str">The current string</param>
        /// <param name="flavor">The type of SQL database</param>
        public static string DbWrapString(this string str, DbFlavor flavor)
        {
            if (flavor == DbFlavor.Sqlite || flavor == DbFlavor.DuckDb)
                return "'" + str.Replace("'", "''") + "'";
            char begin, end;

            switch (flavor)
            {
                case DbFlavor.PostgreSql:
                case DbFlavor.MySql:
                case DbFlavor.SqlServer:
                case DbFlavor.AzureSql:
                    begin = end = '\'';
                    break;
                default:
                    throw new NotSupportedException($"The {nameof(SqlLinqer.DbFlavor)} {flavor} is not supported");
            }

            if (str.StartsWith(begin.ToString()) && str.EndsWith(end.ToString()))
                return str;
            else
                return $"{begin}{str.Replace("'", "''")}{end}";
        }
        /// <summary>
        /// Escape the string for use in a SQL like
        /// </summary>
        /// <param name="str">The current string</param>
        /// <param name="flavor">The flavor of database</param>
        public static string EscapeForSqlLike(this string str, DbFlavor flavor)
        {
            switch (flavor)
            {
                case DbFlavor.SqlServer:
                case DbFlavor.AzureSql:
                    return str
                        .Replace("[", "[[]")
                        .Replace("_", "[_]")
                        .Replace("%", "[%]");
                case DbFlavor.MySql:
                case DbFlavor.DuckDb:
                case DbFlavor.PostgreSql:
                case DbFlavor.Sqlite:
                    return str
                        .Replace("\\", "\\\\")
                        .Replace("_", "\\_")
                        .Replace("%", "\\%");
                default:
                    throw new NotSupportedException($"Unsupported {nameof(DbFlavor)} {flavor}");
            }
        }
    }
}

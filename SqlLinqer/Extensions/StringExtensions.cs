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
        /// Get a unique GUID for the current string
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
        /// Wrap the string in the correct SQL characters given the flavor
        /// </summary>
        /// <param name="str">The current string</param>
        /// <param name="flavor">The type of SQL database</param>
        public static string DbWrap(this string str, DbFlavor flavor)
        {
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
                    begin = '[';
                    end = ']';
                    break;
                default:
                    throw new NotSupportedException($"The {nameof(SqlLinqer.DbFlavor)} {flavor} is not supported");
            }

            if (str.StartsWith(begin.ToString()) && str.EndsWith(end.ToString()))
                return str;
            else
                return $"{begin}{str}{end}";
        }
        /// <summary>
        /// Wrap the string in the correct SQL characters given the flavor
        /// </summary>
        /// <param name="str">The current string</param>
        /// <param name="flavor">The type of SQL database</param>
        public static string DbWrapString(this string str, DbFlavor flavor)
        {
            char begin, end;

            switch (flavor)
            {
                case DbFlavor.PostgreSql:
                case DbFlavor.MySql:
                case DbFlavor.SqlServer:
                    begin = end = '\'';
                    break;
                default:
                    throw new NotSupportedException($"The {nameof(SqlLinqer.DbFlavor)} {flavor} is not supported");
            }

            if (str.StartsWith(begin.ToString()) && str.EndsWith(end.ToString()))
                return str;
            else
                return $"{begin}{str}{end}";
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
                    return str
                        .Replace("[", "[[]")
                        .Replace("_", "[_]")
                        .Replace("%", "[%]");
                case DbFlavor.MySql:
                case DbFlavor.PostgreSql:
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
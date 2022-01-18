using System;
using System.Data.Common;

namespace SqlLinqer
{
    /// <summary>
    /// Various extensions for SqlLinqer 
    /// </summary>
    public static class SqlLinqerExtensions
    {
        /// <summary>
        /// Escape a like string value
        /// </summary>
        /// <param name="str">string to escape</param>
        /// <returns>like escaped string</returns>
        public static string EscapeLikeString(this string str)
        {
            return str
                .Replace("[", "[[]")
                .Replace("_", "[_]")
                .Replace("%", "[%]");
        }
        /// <summary>
        /// Get command text with values instead of parameter placeholders
        /// </summary>
        /// <param name="cmd">database command with text and parameters</param>
        public static string CommandTextWithParamValues(this DbCommand cmd)
        {
            string cmd_text = cmd.CommandText;
            foreach (DbParameter item in cmd.Parameters)
            {
                string value_text = null;
                switch (Type.GetTypeCode(item.Value.GetType()))
                {
                    case TypeCode.Boolean:
                        value_text = ((bool)item.Value) ? "1" : "0";
                        break;
                    case TypeCode.Byte:
                    case TypeCode.SByte:
                    case TypeCode.UInt16:
                    case TypeCode.UInt32:
                    case TypeCode.UInt64:
                    case TypeCode.Int16:
                    case TypeCode.Int32:
                    case TypeCode.Int64:
                    case TypeCode.Decimal:
                    case TypeCode.Double:
                    case TypeCode.Single:
                        value_text = item.Value.ToString();
                        break;
                    default:
                        value_text = $"'{item.Value}'";
                        break;
                }
                cmd_text = cmd_text.Replace(item.ParameterName, value_text);
            }
            return cmd_text;
        }
    }
}

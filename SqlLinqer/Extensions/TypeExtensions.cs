using System;
using System.Linq;
using System.Data;
using System.Collections.Generic;
using System.Reflection;

namespace SqlLinqer.Extensions.TypeExtensions
{
    /// <summary>
    /// A set of <see cref="Type"/> extensions
    /// </summary>
    public static class TypeExtensions
    {
        /// <summary>
        /// Get a list of members that are instance field and properties
        /// </summary>
        /// <param name="type">The current type</param>
        public static IEnumerable<MemberInfo> GetInstanceFieldsAndPropertyMembers(this Type type)
        {
            foreach (var field in type
                .GetFields(BindingFlags.Public | BindingFlags.Instance)
                .OrderBy(x => x.MetadataToken))
            {
                yield return field;
            }

            foreach (var prop in type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .OrderBy(x => x.MetadataToken))
            {
                yield return prop;
            }
        }
        /// <summary>
        /// Get the underlying type of a IEnumerable type (List T, T[], IEnumberable T, etc)
        /// </summary>
        /// <param name="type">The current type</param>
        public static Type GetItemTypeFromIEnumerable(this Type type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            if (!typeof(System.Collections.IEnumerable).IsAssignableFrom(type))
                return null;

            if (type.IsGenericType)
                return type.GetGenericArguments()?.FirstOrDefault();

            return type.GetElementType();
        }
        /// <summary>
        /// Get a typed custom attribute from a type
        /// </summary>
        /// <param name="type">The current type</param>
        public static T GetCustomAttribute<T>(this Type type) where T : Attribute
        {
            return (T)Attribute.GetCustomAttribute(type, typeof(T));
        }
        /// <summary>
        /// Get a list of typed custom attributes from a type
        /// </summary>
        /// <param name="type">The current type</param>
        public static T[] GetCustomAttributes<T>(this Type type) where T : Attribute
        {
            return (T[])Attribute.GetCustomAttributes(type, typeof(T));
        }
        /// <summary>
        /// Get the DbType of a given Type
        /// </summary>
        /// <param name="type">The current type</param>
        public static DbType GetDbType(this Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;

            if (type == typeof(int)) return DbType.Int32;
            if (type == typeof(byte)) return DbType.Byte;
            if (type == typeof(short)) return DbType.Int16;
            if (type == typeof(long)) return DbType.Int64;
            if (type == typeof(float)) return DbType.Single;
            if (type == typeof(double)) return DbType.Double;
            if (type == typeof(decimal)) return DbType.Decimal;
            if (type == typeof(bool)) return DbType.Boolean;
            if (type == typeof(string)) return DbType.String;
            if (type == typeof(char)) return DbType.StringFixedLength;
            if (type == typeof(DateTime)) return DbType.DateTime;
            if (type == typeof(TimeSpan)) return DbType.Time;
            if (type == typeof(byte[])) return DbType.Binary;
            if (type == typeof(Guid)) return DbType.Guid;

            throw new NotSupportedException($"Type '{type}' is not supported.");
        }
        internal static string CSharpTypeToSql(string cSharpType, DbFlavor flavor)
        {
            switch (cSharpType?.ToLower().TrimEnd('?'))
            {
                case "bool":
                case "boolean": return "BIT";
                case "byte": return "TINYINT";
                case "char": return "CHAR(1)";
                case "decimal": return "DECIMAL(18,29)";
                case "double": return "FLOAT";
                case "float": return "REAL";
                case "int":
                case "uint":
                case "int32":
                case "uint32":
                    return "INT";
                case "long":
                case "ulong":
                case "int64":
                case "uint64":
                    return "BIGINT";
                case "short":
                case "ushort":
                case "int16":
                case "uint16":
                    return "SMALLINT";
                case "sbyte": return "TINYINT";
                case "datetimeoffset": return "DATETIMEOFFSET";
                case "datetime": return "DATETIME";
                case "byte[]": return "VARBINARY(MAX)";
                case "string": return "NVARCHAR(MAX)";
                case "timespan": return "TIME";
                case "guid": return "UNIQUEIDENTIFIER";
                case "object": return "NVARCHAR(MAX)";
                default:
                    throw new NotSupportedException("This query does not support the type: " + cSharpType);
            }
        }
        /// <summary>
        /// Convert a code Type to a the SQL string type given the flavor being used
        /// </summary>
        /// <param name="cSharpType">The current type</param>
        /// <param name="flavor">The type of SQL database</param>
        public static string CSharpTypeToSql(this Type cSharpType, DbFlavor flavor)
        {
            cSharpType = Nullable.GetUnderlyingType(cSharpType) ?? cSharpType;
            return CSharpTypeToSql(cSharpType.Name, flavor);
        }
        /// <summary>
        /// Returns TRUE if the type is Anonymous
        /// </summary>
        /// <param name="type">The current type</param>
        public static bool IsAnonymousType(this Type type)
        {
            return
                // hasCompilerGeneratedAttribute
                type.GetCustomAttributes(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false).Count() > 0

                // nameContainsAnonymousType
                && type.FullName.Contains("AnonymousType")
            ;
        }

        /// <summary>
        /// Assert that type A and B are the same
        /// </summary>
        /// <param name="a">The current type</param>
        /// <param name="b">The type to check</param>
        public static void AssertTypeMatch(this Type a, Type b)
        {
            if (a != b)
                throw new FormatException($"Type '{b.Name}' does not match type '{a.Name}'.");
        }
        /// <summary>
        /// Assert that the object is of type A
        /// </summary>
        /// <param name="a">The current type</param>
        /// <param name="obj">The object to check</param>
        public static void AssertTypeMatch<T>(this Type a, T obj)
        {
            AssertTypeMatch(a, typeof(T));
        }
        /// <summary>
        /// Assert that the generic type T is of type A
        /// </summary>
        /// <param name="a">The current type</param>
        public static void AssertTypeMatch<T>(this Type a)
        {
            AssertTypeMatch(a, typeof(T));
        }
        /// <summary>
        /// Assert that the object is of type A
        /// </summary>
        /// <param name="a">The current type</param>
        /// <param name="obj">The object to check</param>
        public static void AssertTypeMatch(this Type a, object obj)
        {
            AssertTypeMatch(a, obj.GetType());
        }
    }
}
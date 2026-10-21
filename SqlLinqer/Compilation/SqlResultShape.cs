using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Select;
using SqlLinqer.Queries.Core;

namespace SqlLinqer.Compilation
{
    /// <summary>The logical kind of a projected member, independent of JSON transport.</summary>
    public enum SqlResultMemberKind
    {
        /// <summary>A single SQL value.</summary>
        Scalar,
        /// <summary>A nested object, or null when no related row matches.</summary>
        Object,
        /// <summary>A nested collection, empty when no related rows match.</summary>
        Collection
    }

    /// <summary>
    /// Describes one scalar, optional object, or collection in a logical SELECT result.
    /// Missing related objects use null; empty related collections use empty arrays.
    /// </summary>
    public sealed class SqlResultMember
    {
        /// <summary>The mapped output name.</summary>
        public string Name { get; }
        /// <summary>The logical result kind. Collections use empty arrays when there are no related rows.</summary>
        public SqlResultMemberKind Kind { get; }
        /// <summary>
        /// The mapped CLR member type, or object when a SQL expression has no declared CLR type. It does not describe the transport encoding.
        /// </summary>
        public Type ClrType { get; }
        /// <summary>The selected members of a nested result.</summary>
        public IReadOnlyList<SqlResultMember> Members { get; }
        internal SqlResultMember(string name, SqlResultMemberKind kind, Type clrType, SqlResultMember[] members)
        { Name = name; Kind = kind; ClrType = clrType; Members = Array.AsReadOnly(members); }
    }

    /// <summary>
    /// Immutable metadata describing selected members independently of the database JSON transport format.
    /// Typed loading still uses the connector's configured JSON model contract.
    /// </summary>
    public sealed class SqlResultShape
    {
        /// <summary>The selected members.</summary>
        public IReadOnlyList<SqlResultMember> Members { get; }
        private SqlResultShape(SqlResultMember[] members) { Members = new ReadOnlyCollection<SqlResultMember>(members); }

        internal static SqlResultShape Capture(SelectQuery query) => new SqlResultShape(CaptureMembers(query));
        private static SqlResultMember[] CaptureMembers(SelectQuery query) => query.Selects.GetAll().Select(statement =>
        {
            if (statement is SelectRelationship relationship)
                return new SqlResultMember(statement.Alias,
                    relationship.Relationship.Type == RelationshipType.OneToOne ? SqlResultMemberKind.Object : SqlResultMemberKind.Collection,
                    (relationship.Relationship.DataTarget as ReflectedColumn)?.ValueType
                        ?? relationship.Relationship.ChildTable.SourceType ?? typeof(object), CaptureMembers(relationship.SubQuery));
            var type = statement is SelectColumn column && column.Column is ReflectedColumn reflected
                ? reflected.ValueType : typeof(object);
            return new SqlResultMember(statement.Alias, SqlResultMemberKind.Scalar, type, Array.Empty<SqlResultMember>());
        }).ToArray();
    }
}

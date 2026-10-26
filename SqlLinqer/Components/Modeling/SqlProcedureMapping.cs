using System;
using System.Collections.Concurrent;
using SqlLinqer.Modeling;

namespace SqlLinqer.Components.Modeling
{
    internal static class SqlProcedureMapping
    {
        private static readonly ConcurrentDictionary<Type, SqlProcedure> Procedures = new ConcurrentDictionary<Type, SqlProcedure>();

        internal static SqlProcedure Get(Type type) => Procedures.GetOrAdd(type, Read);

        private static SqlProcedure Read(Type type)
        {
            var procedure = (SqlProcedure)Attribute.GetCustomAttribute(type, typeof(SqlProcedure), false)
                ?? throw new InvalidOperationException($"Procedure class '{type}' needs a SqlProcedure attribute.");
            if (string.IsNullOrWhiteSpace(procedure.Name) || procedure.Schema != null && string.IsNullOrWhiteSpace(procedure.Schema))
                throw new InvalidOperationException("Procedure name and schema must not be empty.");
            return procedure;
        }
    }
}

using SqlLinqer.Connections;
using SqlLinqer.Components.Render;

namespace SqlLinqer.Queries.Interfaces
{
    /// <summary>
    /// Renders query builders as parameterized SQL without executing a database command.
    /// </summary>
    public interface IQueryCore
    {
        /// <summary>
        /// Renders SQL and fresh parameter bindings for the selected database dialect without opening a connection.
        /// Execution-time insert batching is not applied. Legacy procedure TVP values retain their references; Capture detaches supported mutable values.
        /// </summary>
        /// <param name="connector">The connector whose dialect is used. This does not replace the query's retained connector or rebuild its model schema.</param>
        /// <returns>The command text, parameter bindings, and available SELECT result-shape metadata.</returns>
        RenderedQuery Render(BaseConnector connector);
        /// <summary>
        /// Renders SQL and fresh parameter bindings for the selected database dialect without opening a connection.
        /// Execution-time insert batching is not applied. Legacy procedure TVP values retain their references; Capture detaches supported mutable values.
        /// </summary>
        /// <param name="flavor">The SQL dialect to render.</param>
        /// <returns>The command text, parameter bindings, and available SELECT result-shape metadata.</returns>
        RenderedQuery Render(DbFlavor flavor);
        /// <summary>
        /// Renders SQL into a supplied parameter collection. The returned text references the bindings added to that collection.
        /// Choose DuckDB placeholder syntax before adding values; a populated parameter collection cannot switch syntax families.
        /// </summary>
        /// <param name="existingCollection">The bindings to reuse and extend. Do not mutate or render into a shared collection concurrently.</param>
        /// <param name="flavor">The SQL dialect and parameter placeholder syntax.</param>
        /// <returns>SQL text whose placeholders refer to the supplied collection.</returns>
        string Render(ParameterCollection existingCollection, DbFlavor flavor);
    }
}

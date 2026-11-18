namespace SqlLinqer.Components.Render
{
    // One named value for hand-written SQL. Query snapshots copy its value like other bindings.
    internal sealed class NamedValue
    {
        internal readonly string Name;
        internal readonly object Value;
        internal readonly System.Data.DbType? DbType;
        // A list value holds an object[] and renders one parameter for each element.
        internal readonly bool IsList;

        internal NamedValue(string name, object value, System.Data.DbType? dbType, bool isList = false)
        {
            Name = name;
            Value = value;
            DbType = dbType;
            IsList = isList;
        }
    }
}

namespace SqlLinqer.Components.Outputs
{
    /// <summary>
    /// An output statement
    /// </summary>
    public interface IOutputStatement
    {
        /// <summary>
        /// Renders the object into a string.
        /// </summary>
        /// <param name="flavor">The type of Sql database being used</param>
        string Render(DbFlavor flavor);
    }
}
namespace SqlLinqer.Components.Render
{
    /// <summary>
    /// holds the data related to a TVP parameter for a <see cref="ParameterCollection"/>
    /// </summary>
    public class TvpParameter : Parameter
    {
        /// <summary>
        /// The Sql type name of the value
        /// </summary>
        public string TypeName { get; }

        /// <summary>
        /// holds the data related to a parameter for a <see cref="ParameterCollection"/>
        /// </summary>
        /// <param name="placehodler">The placehodler string</param>
        /// <param name="value">The value</param>
        /// <param name="typeName">The Sql type name of the value</param>
        public TvpParameter(string placehodler, object value, string typeName)
            : base(placehodler, value, null)
        {
            TypeName = typeName;
        }
    }
}
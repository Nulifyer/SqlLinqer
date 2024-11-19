namespace SqlLinqer.Components.Render
{
    /// <summary>
    /// holds the data related to a parameter for a <see cref="ParameterCollection"/>
    /// </summary>
    public class Parameter
    {
        /// <summary>
        /// The placehodler string
        /// </summary>
        public string Placehodler { get; }
        /// <summary>
        /// The value
        /// </summary>
        public object Value { get; }
        /// <summary>
        /// The <see cref="System.Data.DbType"/> type of the value
        /// </summary>
        public System.Data.DbType? DbType { get; }


        /// <summary>
        /// holds the data related to a parameter for a <see cref="ParameterCollection"/>
        /// </summary>
        /// <param name="placehodler">The placehodler string</param>
        /// <param name="value">The value</param>
        /// <param name="dbType">The <see cref="System.Data.DbType"/> type of the value</param>
        public Parameter(string placehodler, object value, System.Data.DbType? dbType)
        {
            Placehodler = placehodler;
            Value = value;
            DbType = dbType;
        }
    }
}
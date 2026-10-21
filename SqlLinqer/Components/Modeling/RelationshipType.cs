namespace SqlLinqer.Components.Modeling
{
    /// <summary>
    /// The cardinality of a relationship between mapped tables.
    /// </summary>
    public enum RelationshipType
    {
        /// <summary>
        /// One to one relationship
        /// </summary>
        OneToOne,
        /// <summary>
        /// One to many relationship
        /// </summary>
        OneToMany,
        /// <summary>
        /// Many to many relationship
        /// </summary>
        ManyToMany,
    }
}

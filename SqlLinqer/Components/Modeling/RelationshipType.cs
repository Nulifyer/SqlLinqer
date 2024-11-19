namespace SqlLinqer.Components.Modeling
{
    /// <summary>
    /// Indicated the type of the relationship between <see cref="ModelConfig"/>
    /// </summary>
    public enum RelationshipType
    {
        /// <summary>
        /// One to one realationship
        /// </summary>
        OneToOne,
        /// <summary>
        /// One to many realationship
        /// </summary>
        OneToMany,
        /// <summary>
        /// Many to many realationship
        /// </summary>
        ManyToMany,
    }
}
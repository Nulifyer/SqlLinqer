namespace SqlLinqer.Relationships
{
    /// <summary>
    /// Defines a relationship between two classes
    /// </summary>
    public class SQLRelationship
    {
        /// <summary>
        /// The root config
        /// </summary>
        public SQLConfig Root { get; internal set; }
        /// <summary>
        /// The member that contains the foreign object data
        /// </summary>
        public SQLMemberInfo ForeignKey { get; internal set; }
        /// <summary>
        /// The member of the join to class that has the foreign key
        /// </summary>
        public SQLMemberInfo Left { get; internal set; }
        /// <summary>
        /// The member of the joined class as a result of the foreign
        /// </summary>
        public SQLMemberInfo Right { get; internal set; }
    }
}

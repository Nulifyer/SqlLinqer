using System;
using SqlLinqer.Components.Joins;
using SqlLinqer.Components.Modeling;

namespace SqlLinqer.Modeling
{
    /// <summary>
    /// Maps a related collection through an existing linking model or linking table. Empty selected relationships load as empty collections.
    /// Select the relationship explicitly or include it with SqlAutoSelect. Persist relationship changes through foreign keys or linking rows.
    /// </summary>
    public sealed class SqlManyToMany : RelationshipAttribute
    {
        internal bool directTargets;

        /// <summary>
        /// The type that models the relational table
        /// </summary>
        public readonly Type JoiningType;
        /// <summary>
        /// The foreign key on the joining type the is the primary key of the left model
        /// </summary>
        public readonly string LeftJoiningKey;
        /// <summary>
        /// The foreign key on the joining type that is the primary key of the right model
        /// </summary>
        public readonly string RightJoiningKey;

        /// <summary>
        /// Maps a related collection through an existing linking model or linking table. Empty selected relationships load as empty collections.
        /// </summary>
        /// <param name="joiningType">The mapped linking model type. Its table and column attributes supply SQL names.</param>
        /// <param name="leftJoiningKey">The linking model's CLR member referencing the root key.</param>
        /// <param name="rightJoiningKey">The linking model's CLR member referencing the related key.</param>
        public SqlManyToMany(Type joiningType, string leftJoiningKey, string rightJoiningKey) : base(RelationshipType.ManyToMany)
        {
            directTargets = false;

            JoiningType = joiningType;
            LeftJoiningKey = leftJoiningKey;
            RightJoiningKey = rightJoiningKey;
        }

        /// <summary>
        /// The schema of the relational table
        /// </summary>
        public readonly string JoiningSchema;
        /// <summary>
        /// The name of the relational table
        /// </summary>
        public readonly string JoiningTable;
        /// <summary>
        /// The foreign key on the relational table the is the primary key of the left model
        /// </summary>
        public readonly string LeftForeignKey;
        /// <summary>
        /// The foreign key on the relational table the is the primary key of the right model
        /// </summary>
        public readonly string RightForeignKey;

        /// <summary>
        /// Maps a related collection through an existing linking model or linking table. Empty selected relationships load as empty collections.
        /// </summary>
        /// <param name="joiningTable">The existing database linking table name.</param>
        /// <param name="leftForeignerKey">The database column in the linking table referencing the root key.</param>
        /// <param name="rightForeignerKey">The database column in the linking table referencing the related key.</param>
        public SqlManyToMany(string joiningTable, string leftForeignerKey, string rightForeignerKey) : base(RelationshipType.ManyToMany)
        {
            directTargets = true;

            JoiningTable = joiningTable;
            LeftForeignKey = leftForeignerKey;
            RightForeignKey = rightForeignerKey;
        }
        /// <summary>
        /// Maps a related collection through an existing linking model or linking table. Empty selected relationships load as empty collections.
        /// </summary>
        /// <param name="joiningTableSchema">The explicit database schema of the linking table.</param>
        /// <param name="joiningTable">The existing database linking table name.</param>
        /// <param name="leftForeignerKey">The database column in the linking table referencing the root key.</param>
        /// <param name="rightForeignerKey">The database column in the linking table referencing the related key.</param>
        public SqlManyToMany(string joiningTableSchema, string joiningTable, string leftForeignerKey, string rightForeignerKey)
            : this(joiningTable, leftForeignerKey, rightForeignerKey)
        {
            JoiningSchema = joiningTableSchema;
        }

        /// <inheritdoc/>
        public override Relationship ProcessRelationship(Table currentTable, UnprocessedRelationshipMember unprocessed)
            => ManyToManyRelationship.Create(currentTable, unprocessed, JoiningType, JoiningSchema, JoiningTable,
                directTargets ? LeftForeignKey : LeftJoiningKey, directTargets ? RightForeignKey : RightJoiningKey,
                directTargets, bidirectional: false);
    }
}

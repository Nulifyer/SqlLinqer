using System;
using SqlLinqer.Components.Joins;

namespace SqlLinqer.Modeling
{
    /// <summary>
    /// Defines additional conditions against to add to the join of the decorated relationship
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true)]
    public abstract class SqlCondition : Attribute
    {
        /// <summary>
        /// If the condition should replace the join condition from the relationship
        /// </summary>
        public virtual bool Override { get; }
        
        /// <summary>
        /// The index of the join in the relationship to apply to
        /// </summary>
        public readonly int ApplyToIdx;
        
        /// <summary>
        /// Defines additional conditions against to add to the join of the decorated relationship
        /// </summary>
        public SqlCondition(int apply_to_index)
        {
            Override = false;
            ApplyToIdx = apply_to_index;
        }

        /// <summary>
        /// Add the additional conditions to the join
        /// </summary>
        /// <param name="relationship">The current model's current relationship</param>
        /// <param name="join">The current join</param>
        protected internal abstract void AddToJoin(Relationship relationship, Join join);
    }
}

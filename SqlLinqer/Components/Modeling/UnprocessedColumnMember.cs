using System.Reflection;
using SqlLinqer.Modeling;

namespace SqlLinqer.Components.Modeling
{
    /// <summary>Column attributes collected before constructing a table mapping.</summary>
    public class UnprocessedColumnMember
    {
        /// <summary>The CLR field or property being mapped.</summary>
        public readonly MemberInfo Member;
        /// <summary>The primary-key attribute, if present.</summary>
        public readonly SqlPrimaryKey PrimaryKeyInfo;
        /// <summary>The column mapping attribute, if present.</summary>
        public readonly SqlColumn ColumnInfo;
        /// <summary>Automatic-operation settings derived from the member's attributes.</summary>
        public readonly AutoOptions AutoOptions;

        internal UnprocessedColumnMember(MemberInfo member, bool data_target_member)
        {
            Member = member;
            if (!data_target_member)
            {
                PrimaryKeyInfo = Member.GetCustomAttribute<SqlPrimaryKey>();
                ColumnInfo = Member.GetCustomAttribute<SqlColumn>();
            }            
            AutoOptions = new AutoOptions(Member.ReflectedType, this, false);
        }
    }
}
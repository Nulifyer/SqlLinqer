using System.Reflection;
using SqlLinqer.Modeling;

namespace SqlLinqer.Components.Modeling
{
    public class UnprocessedColumnMember
    {
        public readonly MemberInfo Member;
        public readonly SqlPrimaryKey PrimaryKeyInfo;
        public readonly SqlColumn ColumnInfo;
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
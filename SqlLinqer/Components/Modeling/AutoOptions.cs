using System;
using System.Reflection;
using SqlLinqer.Modeling;

namespace SqlLinqer.Components.Modeling
{
    public class AutoOptions
    {
        public readonly bool Select;
        public readonly bool Update;
        public readonly bool UpdateOnDefaultAlways;
        public readonly bool Insert;
        public readonly bool OrderBy;
        public readonly SqlAutoOrderBy OrderByInfo;

        public AutoOptions(Type type, MemberInfo member, bool data_target_member)
        {
            Select = AutoDoThing<SqlAutoSelect, SqlAutoSelectExclude>(member, type, data_target_member) ?? true;
            Update = AutoDoThing<SqlAutoUpdate, SqlAutoUpdateExclude>(member, type, data_target_member) ?? true;
            Insert = AutoDoThing<SqlAutoInsert, SqlAutoInsertExclude>(member, type, data_target_member) ?? true;
            
            OrderByInfo = member.GetCustomAttribute<SqlAutoOrderBy>();
            OrderBy = OrderByInfo != null;

            UpdateOnDefaultAlways = member.GetCustomAttribute<SqlUpdateDefaultAlways>() != null;
        }
        public AutoOptions(Type type, UnprocessedColumnMember unprocessed_member, bool data_target_member)
            : this(type, unprocessed_member.Member, data_target_member)
        {
            if (unprocessed_member.PrimaryKeyInfo != null)
            {
                Update = false;
                UpdateOnDefaultAlways = false;
                
                if (unprocessed_member.PrimaryKeyInfo.DbGenerated)
                {
                    Insert = false;
                }
            }
        }

        private static bool? AutoDoThing<TYes, TNo>(Type tableType)
            where TYes : Attribute
            where TNo : Attribute
        {
            bool? autoDo = null;

            var autoYes = tableType.GetCustomAttribute<TYes>();
            if (autoYes != null)
                autoDo = true;

            var autoNo = tableType.GetCustomAttribute<TNo>();
            if (autoNo != null)
                autoDo = false;

            return autoDo;
        }
        private static bool? AutoDoThing<TYes, TNo>(MemberInfo member, Type tableType, bool data_target_member)
            where TYes : Attribute
            where TNo : Attribute
        {
            var tableAutoDo = AutoDoThing<TYes, TNo>(tableType);
            if (data_target_member && tableAutoDo != true)
                tableAutoDo = false;

            bool? autoDo = tableAutoDo;

            var autoYes = member.GetCustomAttribute<TYes>();
            if (autoYes != null)
                autoDo = true;

            var autoNo = member.GetCustomAttribute<TNo>();
            if (autoNo != null)
                autoDo = false;

            return autoDo;
        }
    }
}
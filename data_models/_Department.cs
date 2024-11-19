using System;
using SqlLinqer.Modeling;

namespace SqlTest.Models
{
    [SqlTable("departments")]
    public class _Department : SqlLinqerPrimaryKeyObject<_Department, string>, IDisplayable
    {
        [SqlPrimaryKey]
        [SqlColumn("dept_no")]
        public string Id { get; set; }

        [SqlColumn("dept_name")]
        public string Name { get; set; }

        public string ToDisplayableString()
        {
            return Id != null ? $"[{Id}] {Name}" : null;
        }
    }
}
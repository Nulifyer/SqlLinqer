using System;
using System.Reflection;
using SqlLinqer.Modeling;
using SqlLinqer.Extensions.MemberInfoExtensions;

namespace SqlLinqer.Components.Modeling
{
    /// <summary>
    /// Modeled data of a <see cref="MemberInfo"/> that contains related configuration and helper methods.
    /// </summary>
    public class ReflectedColumn : Column
    {
        /// <summary>Whether the mapped member is the table's primary key.</summary>
        public readonly bool PrimaryKey;
        /// <summary>The CLR field or property mapped to this column.</summary>
        public readonly MemberInfo CodeMember;
        /// <summary>Automatic-operation settings for the mapped member.</summary>
        public readonly AutoOptions AutoOptions;

        /// <summary>Creates a column mapping from reflected member metadata.</summary>
        public ReflectedColumn(Table table, UnprocessedColumnMember unprocessed_column)
            : base(table, unprocessed_column.Member.Name, unprocessed_column.Member.Name)
        {
            CodeMember = unprocessed_column.Member;
            PrimaryKey = unprocessed_column.PrimaryKeyInfo != null;
            ColumnName = unprocessed_column.ColumnInfo?.Name ?? CodeMember.Name;
            DbType = unprocessed_column.ColumnInfo?.DbType;
            AutoOptions = unprocessed_column.AutoOptions;
        }

        /// <summary>
        /// The code type of the <see cref="CodeMember"/> value
        /// </summary>
        public Type DeclaredValueType => CodeMember?.GetFieldOrPropValueType();

        /// <summary>
        /// The code type of the <see cref="CodeMember"/> value, non-nullable
        /// </summary>
        public Type ValueType
        {
            get
            {
                var raw_type = CodeMember?.GetFieldOrPropValueType();
                return Nullable.GetUnderlyingType(raw_type) ?? raw_type;
            }
        }

        /// <summary>
        /// Gets the value of the <see cref="CodeMember"/> from the object
        /// </summary>
        /// <param name="obj">The object to get the data from</param>
        public object GetValue(object obj) => CodeMember?.GetFieldOrPropValue(obj);

        /// <summary>
        /// Set the value of the <see cref="CodeMember"/> on the object
        /// </summary>
        /// <param name="obj">The object to set the data on</param>
        /// <param name="value">The value to set</param>
        public void SetValue(object obj, object value) => CodeMember?.SetFieldOrPropValue(obj, value);

        /// <summary>Returns a diagnostic description of this mapping.</summary>
        public override string ToString()
        {
            return $"{Table}.{CodeMember.Name}";
        }
    }
}

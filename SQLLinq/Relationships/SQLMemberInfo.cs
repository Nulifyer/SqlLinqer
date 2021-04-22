using System;
using System.Reflection;

namespace SqlLinqer.Relationships
{
    /// <summary>
    /// Contains all the info about a member and allows for easy interaction with the member
    /// </summary>
    public class SQLMemberInfo
    {
        /// <summary>
        /// The name of the member in the database
        /// </summary>
        public string SQLName { get; internal set; }
        /// <summary>
        /// the config that contains the member
        /// </summary>
        public SQLConfig Config { get; internal set; }
        /// <summary>
        /// the member's info from the class
        /// </summary>
        public MemberInfo Info { get; internal set; }
        /// <summary>
        /// The value type of the member
        /// </summary>
        public Type MemberUnderlyingType
        {
            get
            {
                switch (Info.MemberType)
                {
                    case MemberTypes.Field:
                        return ((FieldInfo)Info).FieldType;
                    case MemberTypes.Property:
                        return ((PropertyInfo)Info).PropertyType;
                    default:
                        throw new ArgumentException("Input MemberInfo must be of type FieldInfo or PropertyInfo");
                }
            }
        }
        /// <summary>
        /// The column's alias name
        /// Used to prevent collision when joining to the same source
        /// </summary>
        public string ColumnAlias { get => $"{Config.TableAlias}_{SQLName}"; }

        internal SQLMemberInfo(SQLConfig config, MemberInfo info, string name = null)
        {
            Config = config;
            Info = info;
            SQLName = name ?? Info.Name;
        }

        /// <summary>
        /// Returns the value the member from the object
        /// </summary>
        /// <param name="obj">The object</param>
        /// <returns>The member's value from the object</returns>
        public object GetValue(object obj)
        {
            switch (Info.MemberType)
            {
                case MemberTypes.Field:
                    return ((FieldInfo)Info).GetValue(obj);
                case MemberTypes.Property:
                    return ((PropertyInfo)Info).GetValue(obj);
                default:
                    throw new ArgumentException("MemberInfo must be of type FieldInfo or PropertyInfo");
            }
        }
        /// <summary>
        /// Sets the member's value in the object
        /// </summary>
        /// <param name="obj">The object</param>
        /// <param name="value">The value to set</param>
        public void SetValue(object obj, object value)
        {
            switch (Info.MemberType)
            {
                case MemberTypes.Field:
                    ((FieldInfo)Info).SetValue(obj, value);
                    break;
                case MemberTypes.Property:
                    ((PropertyInfo)Info).SetValue(obj, value);
                    break;
                default:
                    throw new ArgumentException("MemberInfo must be of type FieldInfo or PropertyInfo");
            }
        }
    }
}

using System;
using System.Reflection;

namespace SqlLinqer.Extensions.MemberInfoExtensions
{
    /// <summary>
    /// A set of member info extensions
    /// </summary>
    public static class MemberInfoExtensions
    {
        /// <summary>
        /// Get the set type of the member's value
        /// </summary>
        /// <param name="member">The current member</param>
        public static Type GetFieldOrPropValueType(this MemberInfo member)
        {
            if (member == null)
                throw new ArgumentNullException(nameof(member));

            switch (member.MemberType)
            {
                case MemberTypes.Field:
                    return (member as FieldInfo).FieldType;
                case MemberTypes.Property:
                    return (member as PropertyInfo).PropertyType;
                default:
                    throw new ArgumentException("Input MemberInfo must be of type FieldInfo or PropertyInfo");
            }
        }
        /// <summary>
        /// Get the backing field of a property
        /// </summary>
        /// <param name="propertyInfo">The current property</param>
        public static FieldInfo GetBackingField(this PropertyInfo propertyInfo)
        {
            string fieldName = $"<{propertyInfo.Name}>k__BackingField";
            return propertyInfo.DeclaringType.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        }
        /// <summary>
        /// Get the value of a member as long as it is a field or property
        /// </summary>
        /// <param name="member">The current member</param>
        /// <param name="obj">The object to get the value from</param>
        public static object GetFieldOrPropValue(this MemberInfo member, object obj)
        {
            if (member == null)
                throw new ArgumentNullException(nameof(member));

            switch (member.MemberType)
            {
                case MemberTypes.Field:
                    return (member as FieldInfo).GetValue(obj);
                case MemberTypes.Property:
                    var prop = (member as PropertyInfo);
                    if (prop.GetMethod == null)
                    {
                        var field = prop.GetBackingField();
                        return field.GetValue(obj);
                    }
                    else
                    {
                        return prop.GetValue(obj);
                    }
                default:
                    throw new ArgumentException("Input MemberInfo must be of type FieldInfo or PropertyInfo");
            }
        }
        /// <summary>
        /// Set the value of a member as long as it is a field or property
        /// </summary>
        /// <param name="member">The current member</param>
        /// <param name="obj">The object to set the value of</param>
        /// <param name="value">The value to set</param>
        public static void SetFieldOrPropValue(this MemberInfo member, object obj, object value)
        {
            if (member == null)
                throw new ArgumentNullException(nameof(member));

            switch (member.MemberType)
            {
                case MemberTypes.Field:
                    (member as FieldInfo).SetValue(obj, value);
                    break;
                case MemberTypes.Property:
                    var prop = (member as PropertyInfo);
                    if (prop.SetMethod == null)
                    {
                        var field = prop.GetBackingField();
                        field.SetValue(obj, value);
                    }
                    else
                    {
                        prop.SetValue(obj, value);
                    }
                    break;
                default:
                    throw new ArgumentException("Input MemberInfo must be of type FieldInfo or PropertyInfo");
            }
        }
        /// <summary>
        /// Get the name of a member as long as it is a field or property
        /// </summary>
        /// <param name="member">The current member</param>
        public static string GetFieldOrPropName(this MemberInfo member)
        {
            if (member == null)
                return null;
            else if (member is PropertyInfo prop)
                return prop.Name;
            else if (member is FieldInfo field)
                return field.Name;
            else
                return null;
        }
    }
}
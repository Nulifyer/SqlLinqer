using System;
using System.ComponentModel;
using System.Reflection;

namespace SqlLinqer.Extensions.EnumExtensions
{
    /// <summary>
    /// A set of enum extensions
    /// </summary>
    public static class EnumExtensions
    {
        /// <summary>
        /// Returns the string value of the <see cref="DescriptionAttribute"/> on that enum value
        /// </summary>
        /// <param name="value">The current enum value</param>
        public static string GetDescription(this Enum value)
        {
            FieldInfo member = value.GetType().GetField(value.ToString());
            var attr = member.GetCustomAttribute<DescriptionAttribute>();
            return attr?.Description ?? value.ToString();
        }
    }
}
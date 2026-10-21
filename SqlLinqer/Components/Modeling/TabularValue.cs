using System;
using SqlLinqer.Connections;

namespace SqlLinqer.Components.Modeling
{
    internal static class TabularValue
    {
        internal static object Convert(object value, Type type) => DbValueConversion.ConvertTo(value, type);
    }
}

using System.Reflection;
using Newtonsoft.Json.Serialization;
using SqlLinqer.Extensions.MemberInfoExtensions;

namespace SqlLinqer.Components.Json
{
    internal class MemberValueProvider : IValueProvider
    {
        private MemberInfo _member;

        public MemberValueProvider(MemberInfo member)
        {
            _member = member;
        }

        public object GetValue(object target)
        {
            return _member.GetFieldOrPropValue(target);
        }

        public void SetValue(object target, object value)
        {
            _member.SetFieldOrPropValue(target, value);
        }
    }
}
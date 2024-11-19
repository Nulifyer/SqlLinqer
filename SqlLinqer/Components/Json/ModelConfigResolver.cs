using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using SqlLinqer.Extensions.MemberInfoExtensions;

namespace SqlLinqer.Components.Json
{
    internal class ModelConfigResolver : DefaultContractResolver
    {
        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            var prop = base.CreateProperty(member, memberSerialization);
            
            if (
                !(prop.Readable && prop.Writable) 
                && member is PropertyInfo propInfo
                && propInfo.GetBackingField() != null
            )
            {
                prop.Writable = true;
                prop.Readable = true;
            }

            return prop;
        }
        protected override IValueProvider CreateMemberValueProvider(MemberInfo member)
        {
            return new MemberValueProvider(member);
        }
    }
}
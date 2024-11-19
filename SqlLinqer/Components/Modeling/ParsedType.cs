using System;
using System.Collections.Generic;
using System.Reflection;
using SqlLinqer.Extensions.MemberInfoExtensions;
using SqlLinqer.Extensions.TypeExtensions;
using SqlLinqer.Modeling;
using SqlLinqer.Components.Caching;
using System.Linq;

namespace SqlLinqer.Components.Modeling
{
    internal class ParsedType
    {
        public SqlTable TableInfo;
        public List<UnprocessedColumnMember> ColumnMembers;
        public List<UnprocessedRelationshipMember> RelationshipMembers;

        private ParsedType()
        {
            ColumnMembers = new List<UnprocessedColumnMember>();
            RelationshipMembers = new List<UnprocessedRelationshipMember>();
        }
        public ParsedType(Type type) : this()
        {
            TableInfo = type.GetCustomAttribute<SqlTable>();

            // find members and process relationships
            var members = type.GetInstanceFieldsAndPropertyMembers()
                // Sort by inheritance depth
                .OrderByDescending(member => GetInheritanceDepth(type, member.DeclaringType))
                // Sort by the order of declaration
                .ThenBy(member => member.MetadataToken)
            ;
            foreach (var member in members)
            {
                // check for ignore
                if (member.GetCustomAttribute<SqlIgnore>() != null) continue;

                // can not find backing field of property
                if (
                    !type.IsAnonymousType()
                    && member is PropertyInfo propInfo
                    && (propInfo.GetMethod == null || propInfo.SetMethod == null)
                    && propInfo.GetBackingField() == null
                )
                {
                    continue;
                }

                // handle relationship processing later
                var rel_attr = member.GetCustomAttribute<RelationshipAttribute>();
                if (rel_attr != null)
                {
                    RelationshipMembers.Add(new UnprocessedRelationshipMember(
                        type,
                        member,
                        rel_attr
                    ));
                    continue;
                }

                ColumnMembers.Add(new UnprocessedColumnMember(member, false));
            }
        }

        private static int GetInheritanceDepth(Type derivedType, Type baseType)
        {
            int depth = 0;
            while (derivedType != null && derivedType != baseType)
            {
                derivedType = derivedType.BaseType;
                depth++;
            }
            return depth;
        }

        private static readonly MemoryCache<string, ParsedType> _cache
            = new MemoryCache<string, ParsedType>(TimeSpan.FromSeconds(10), true);
        public static ParsedType GetCached<T>() => GetCached(typeof(T));
        public static ParsedType GetCached(Type type)
        {
            ParsedType parsedType;
            if (!_cache.TryGetValue(type.FullName, out parsedType))
            {
                parsedType = new ParsedType(type);
                _cache.Add(type.FullName, parsedType, TimeSpan.FromSeconds(60));
            }
            return parsedType;
        }
    }
}
using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using SqlLinqer.Modeling;
using SqlLinqer.Components.Joins;
using SqlLinqer.Components.Where;
using SqlLinqer.Extensions.TypeExtensions;
using SqlLinqer.Extensions.StringExtensions;
using SqlLinqer.Extensions.MemberInfoExtensions;
using SqlLinqer.Components.Caching;

namespace SqlLinqer.Components.Modeling
{
    /// <summary>
    /// A model of a sql table
    /// </summary>    
    public class Table
    {
        /// <summary>
        /// A unique ID of this instance
        /// </summary>
        public string UUID { get; private set; }
        /// <summary>
        /// The source type this table was generated from
        /// </summary>
        public readonly Type SourceType;
        /// <summary>
        /// The schema of this table
        /// </summary>
        public readonly string Schema;
        /// <summary>
        /// The sql name of this table
        /// </summary>
        public readonly string Name;
        /// <summary>
        /// The primary key of the table
        /// </summary>
        public readonly Column PrimaryKey;
        /// <summary>
        /// The columns of the table
        /// </summary>
        public readonly ColumnCollection Columns;
        /// <summary>
        /// The order of the table based on the relationships
        /// </summary>
        public int Order => (ParentRelationship?.ParentTable.Order ?? 0) + 1;

        private Relationship _parentRelationship;
        /// <summary>
        /// The parent relationship of the table
        /// </summary>
        public Relationship ParentRelationship 
        {
            get => _parentRelationship;
            internal set { _parentRelationship = value; GenerateUUID(); } 
        }

        private List<UnprocessedRelationshipMember> _unprocessed;
        private RelationshipCollection _relationships;
        /// <summary>
        /// The child relationships of the table
        /// </summary>
        public RelationshipCollection Relationships
            => _relationships ?? (_relationships = ProcessRelationships());

        private string _alias;
        /// <summary>
        /// The alias of the table
        /// </summary>
        public string Alias
        {
            get => _alias ?? UUID;
            set => _alias = value;
        }

        private Table()
        {
            GenerateUUID();
            Columns = new ColumnCollection();
        }
        /// <summary>
        /// Create a new pseudo table. One not made from a type.
        /// </summary>
        /// <param name="schema">The schema of the table</param>
        /// <param name="name">The sql name of the table</param>
        public Table(string schema, string name) : this()
        {
            Schema = schema;
            Name = name;
        }
        /// <summary>
        /// Create a new pseudo table. One not made from a type.
        /// </summary>
        /// <param name="schema">The schema of the table</param>
        /// <param name="name">The sql name of the table</param>
        /// <param name="alias">The alias of the table</param>
        public Table(string schema, string name, string alias) : this(schema, name)
        {
            _alias = alias;
        }
        internal Table(Type type, string default_schema = null) : this()
        {
            var parsedType = ParsedType.GetCached(type);

            SourceType = type;
            Schema = parsedType.TableInfo?.Schema ?? default_schema;
            Name = parsedType.TableInfo?.Name ?? type.Name;

            GenerateUUID();

            _unprocessed = parsedType.RelationshipMembers;

            // process columns
            foreach (var unprocessed_column in parsedType.ColumnMembers)
            {
                var column = new ReflectedColumn(this, unprocessed_column);
                if (column.PrimaryKey)
                    PrimaryKey = column;
                Columns.AddColumn(column);
            }
        }

        private void GenerateUUID()
        {
            if (ParentRelationship == null || SourceType == null) 
                UUID = StringExtensions.GetRandomHashString();
            else
                UUID = $"{ParentRelationship?.ParentTable.UUID}.{SourceType?.Name}.{Name}".GetHashString();
        }

        /// <summary>
        /// Returns the {Schema}.{Name} of the table
        /// </summary>
        public string RenderFullName(DbFlavor flavor)
        {
            return
                (Schema != null ? $"{Schema?.DbWrap(flavor)}." : null)
                + Name?.DbWrap(flavor)
            ;
        }
        /// <summary>
        /// If found returns the column given the path, otherwise returns null
        /// </summary>
        public Column FindColumnFromPath(IEnumerable<string> path)
        {
            if (path.Count() < 1) return null;
            if (path.Count() == 1)
                return Columns.TryGet(path.First());

            var reverse_path = path.Reverse();
            var join = FindRelationshipFromPath(reverse_path.Skip(1).Reverse());
            return join?.ChildTable.FindColumnFromPath(reverse_path.Take(1));
        }
        /// <summary>
        /// If found returns the relationship given the path, otherwise returns null
        /// </summary>
        public Relationship FindRelationshipFromPath(IEnumerable<string> path)
        {
            if (path.Count() < 1) return null;

            var rel = Relationships.TryGet(path.First());
            if (path.Count() == 1) return rel;

            return rel?.ChildTable.FindRelationshipFromPath(path.Skip(1));
        }
        /// <summary>
        /// If found returns the relationship given the path, otherwise returns null
        /// </summary>
        public Relationship FindFirstManyRelationshipFromPath(IEnumerable<string> path)
        {
            if (path.Count() < 1) return null;

            var rel = Relationships.TryGet(path.First());
            if (rel == null) return null;

            switch (rel.Type)
            {
                case RelationshipType.OneToMany:
                case RelationshipType.ManyToMany:
                    return rel;
                default:
                    return rel.ChildTable.FindFirstManyRelationshipFromPath(path.Skip(1));
            }
        }

        /// <summary>
        /// Returns the path of this model
        /// </summary>
        /// <param name="root">The root table to stop at</param>
        public List<string> GetPath(Table root)
        {
            return GetPath(new List<string>(), root);
        }
        private List<string> GetPath(List<string> path, Table root)
        {
            if (this.ParentRelationship == null ||  this.UUID == root?.UUID)
                return path;

            ParentRelationship.ParentTable.GetPath(path, root);
            path.Add(ParentRelationship.DataTarget.Alias);

            return path;
        }

        internal bool IsSameOrChildOf(Table parent_table)
        {
            return IsSameOrChildOf(this, parent_table);
        }
        internal static bool IsSameOrChildOf(Table child_table, Table parent_table)
        {
            if (
                // child table is not null
                child_table != null && 
                (
                    // child table is same as parent table
                    child_table == parent_table || 
                    ( 
                        // check to see if parent table has the same relationship parent as child
                        // and parent is also not the relationship's child table
                        // this mean the parent table is a linking table in the joins of the relatinship

                        // child and parent have same parent relationship
                        child_table.ParentRelationship?.ChildTable == parent_table?.ParentRelationship?.ChildTable &&
                        // child table's parent of the relationship is the same as the parent table's relationship parent
                        child_table.ParentRelationship?.ParentTable == parent_table?.ParentRelationship?.ParentTable &&
                        // parent table is not the child table of the relationship
                        parent_table.ParentRelationship?.ChildTable != parent_table
                    )
                )
            ) return true;
            if (child_table?.ParentRelationship == null || parent_table == null) return false;
            return IsSameOrChildOf(child_table.ParentRelationship.ParentTable, parent_table);
        }

        internal bool IsSameOrAlsoUnderSameParent(Table other_child)
        {
            return this.UUID == other_child.UUID 
                || (
                    IsChildOf(other_child, this.ParentRelationship?.ParentTable)
                    && IsChildOf(this, other_child.ParentRelationship?.ParentTable)
                );
        }

        internal bool IsChildOf(Table parent_table)
        {
            return IsChildOf(this, parent_table);
        }
        internal static bool IsChildOf(Table child_table, Table parent_table)
        {
            if (child_table == null || parent_table == null) return false;
            
            var current = child_table;
            if (current.UUID == parent_table.UUID)
                return false;
            
            while (current != null)
            {
                if (current.ParentRelationship != null && current.ParentRelationship.ParentTable.UUID == parent_table.UUID)
                    return true;
                current = current.ParentRelationship?.ParentTable;
            }
            
            return false;
        }

        private RelationshipCollection ProcessRelationships()
        {
            var collection = new RelationshipCollection();

            if (_unprocessed != null)
            {
                foreach (var unprocessed in _unprocessed)
                {
                    Relationship relationship = unprocessed.Info.ProcessRelationship(this, unprocessed);
                    collection.AddRelationship(relationship);
                }
            }

            return collection;
        }
        
        private static readonly MemoryCache<string, Table> _cache
            = new MemoryCache<string, Table>(TimeSpan.FromSeconds(10), true);
        
        /// <summary>
        /// Get the table from a given type
        /// </summary>
        /// <param name="default_schema">The default schema to use if not defined</param>
        public static Table GetCached<T>(string default_schema = null) => GetCached(typeof(T), default_schema);
        /// <summary>
        /// Get the table from a given type
        /// </summary>
        /// <param name="type">The type to get the table model of</param>
        /// <param name="default_schema">The default schema to use if not defined</param>
        public static Table GetCached(Type type, string default_schema = null)
        {
            string key = $"({default_schema}){type.FullName}";
            Table table;
            if (!_cache.TryGetValue(key, out table))
            {
                table = new Table(type, default_schema);
                _cache.Add(key, table, TimeSpan.FromSeconds(60));
            }
            return table;
        }

        public override string ToString()
        {
            return $"{SourceType?.Name ?? Name}";
        }
    }
}
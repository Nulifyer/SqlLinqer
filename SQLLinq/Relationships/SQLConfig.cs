using System;
using System.Collections.Generic;
using System.Reflection;

namespace SqlLinqer.Relationships
{
    /// <summary>
    /// The parsed config of a class and its relationship to other classes. 
    /// Modeled after the databse as defined in the class's definition.
    /// </summary>
    public sealed class SQLConfig
    {
        /// <summary>
        /// Improves performance by only computing the config of a certain type at a certain level once
        /// </summary>
        private static readonly Dictionary<(Type type, int recursionLevel), SQLConfig> _cache = new Dictionary<(Type type, int recursionLevel), SQLConfig>();

        /// <summary>
        /// The class type the config was made for 
        /// </summary>
        public Type Type { get; private set; }
        /// <summary>
        /// The level of recursion the config was made for
        /// </summary>
        public int RecursionLevel { get; private set; }
        /// <summary>
        /// The name of the table defined on the config's class
        /// </summary>
        public string TableName { get; private set; }
        /// <summary>
        /// The alias name of the table. This allows for the same table to be properly join without collision.
        /// </summary>
        public string TableAlias { get; private set; }

        /// <summary>
        /// The primary key of the class
        /// </summary>
        public SQLPrimaryKeyMemberInfo PrimaryKey { get; private set; }
        /// <summary>
        /// The columns of the class not including the primary key
        /// </summary>
        public List<SQLMemberInfo> Columns { get; private set; }
        /// <summary>
        /// The parent relationship of the config if any
        /// </summary>
        public SQLRelationship ParentRelationship { get; private set; }
        /// <summary>
        /// The one to one relationships of the class
        /// </summary>
        public List<SQLOneToOneRelationship> OneToOne { get; private set; }
        /// <summary>
        /// The one to many relationships of the class 
        /// </summary>
        public List<SQLOneToManyRelationship> OneToMany { get; private set; }

        private SQLConfig()
        {
            Columns = new List<SQLMemberInfo>();
            OneToOne = new List<SQLOneToOneRelationship>();
            OneToMany = new List<SQLOneToManyRelationship>();
        }
        /// <summary>
        /// Creates the config of a class
        /// </summary>
        /// <param name="type">The class to create the config for</param>
        /// <param name="recursionLevel">The level of recursion to step through, this prevents infinite loops</param>
        public SQLConfig(Type type, int recursionLevel = 1)
            : this()
        {
            Type = type;
            RecursionLevel = recursionLevel;

            if (_cache.ContainsKey((Type, RecursionLevel)))
            {
                SQLConfig cached = _cache[(Type, RecursionLevel)];
                TableName = cached.TableName;
                PrimaryKey = cached.PrimaryKey;
                Columns = cached.Columns;
                ParentRelationship = cached.ParentRelationship;
                OneToOne = cached.OneToOne;
                OneToMany = cached.OneToMany;
            }
            else
            {
                Compute(recursionLevel, this);
                _cache.Add((Type, RecursionLevel), this);
            }

            ComputeAlias();
        }
        /// <summary>
        /// Creates the config of a class where the config has some root config
        /// </summary>
        /// <param name="type">The class to create the config for</param>
        /// <param name="rootConfig">The root config of this config</param>
        /// <param name="recursionLevel">The level of recursion to step through, this prevents infinite loops</param>
        private SQLConfig(Type type, SQLConfig rootConfig, int recursionLevel = 1)
            : this()
        {
            Type = type;
            RecursionLevel = recursionLevel;

            Compute(recursionLevel, rootConfig);
        }

        /// <summary>
        /// Computes the config for the <see cref="Type"/>
        /// </summary>
        /// <param name="recursionLevel">The level of recursion to step through, this prevents infinite loops</param>
        /// <param name="rootConfig">The root config if any of the current config</param>
        private void Compute(int recursionLevel, SQLConfig rootConfig)
        {
            // table info
            SQLTable tableprop = (SQLTable)Attribute.GetCustomAttribute(Type, typeof(SQLTable));
            TableName = tableprop?.Name ?? Type.Name;

            // column info
            MemberInfo[] members = Type.GetMembers(BindingFlags.Public | BindingFlags.Instance);

            var OTOmembers = new List<(MemberInfo mem, SQLOneToOne oto)>();
            var OTMmembers = new List<(MemberInfo mem, SQLOneToMany otm)>();
            foreach (var col in members)
            {
                if (col.MemberType != MemberTypes.Property && col.MemberType != MemberTypes.Field)
                    continue;

                if (col.MemberType == MemberTypes.Property && ((PropertyInfo)col).GetSetMethod() == null)
                    continue;

                SQLIgnore sqlIgnr = (SQLIgnore)Attribute.GetCustomAttribute(col, typeof(SQLIgnore));
                if (sqlIgnr != null)
                    continue;

                SQLOneToOne sqlOTO = (SQLOneToOne)Attribute.GetCustomAttribute(col, typeof(SQLOneToOne));
                if (sqlOTO != null)
                {
                    OTOmembers.Add((col, sqlOTO));
                    continue;
                }

                SQLOneToMany sqlOTM = (SQLOneToMany)Attribute.GetCustomAttribute(col, typeof(SQLOneToMany));
                if (sqlOTM != null)
                {
                    OTMmembers.Add((col, sqlOTM));
                    continue;
                }

                SQLPrimaryKey sqlprimarykey = (SQLPrimaryKey)Attribute.GetCustomAttribute(col, typeof(SQLPrimaryKey));
                SQLColumn sqlcolumn = (SQLColumn)Attribute.GetCustomAttribute(col, typeof(SQLColumn));

                string sqlName = sqlcolumn?.Name ?? col.Name;
                if (sqlprimarykey != null && PrimaryKey == null)
                    PrimaryKey = new SQLPrimaryKeyMemberInfo(this, col, sqlName, sqlprimarykey.DBGenerated);
                else
                    Columns.Add(new SQLMemberInfo(this, col, sqlName));
            }

            if (Columns.Count == 0 && PrimaryKey == null)
                throw new Exception($"No columns found for type {Type.FullName}. Make sure some members are public and not ignored.");

            if (recursionLevel > 0)
            {
                --recursionLevel;
                foreach (var (mem, oto) in OTOmembers)
                {
                    Type type = null;
                    switch (mem.MemberType)
                    {
                        case MemberTypes.Field:
                            type = ((FieldInfo)mem).FieldType;
                            break;
                        case MemberTypes.Property:
                            type = ((PropertyInfo)mem).PropertyType;
                            break;
                    }

                    var fkCol = Columns.Find(x => x.Info.Name == oto.TargetProp);
                    if (fkCol == null)
                        throw new Exception("One to One relationship failed to find foreign key column");

                    var otoConfig = new SQLConfig(type, rootConfig, recursionLevel);
                    if (otoConfig.PrimaryKey == null)
                        throw new Exception("One to One relationships require right table to have a primary key");

                    var rel = new SQLOneToOneRelationship()
                    {
                        Root = rootConfig,
                        ForeignKey = new SQLMemberInfo(this, mem),
                        Left = fkCol,
                        Right = otoConfig.PrimaryKey
                    };

                    otoConfig.ParentRelationship = rel;

                    OneToOne.Add(rel);
                }
                foreach (var (mem, otm) in OTMmembers)
                {
                    if (PrimaryKey == null)
                        throw new Exception("One to Many relationships require left table to have a primary key");

                    Type type = null;
                    switch (mem.MemberType)
                    {
                        case MemberTypes.Field:
                            type = ((FieldInfo)mem).FieldType;
                            break;
                        case MemberTypes.Property:
                            type = ((PropertyInfo)mem).PropertyType;
                            break;
                    }

                    var otmConfig = new SQLConfig(type, rootConfig, recursionLevel);

                    var fkCol = otmConfig.Columns.Find(x => x.Info.Name == otm.TargetProp);
                    if (fkCol == null)
                        throw new Exception("One to Many relationship failed to find foreign key column");

                    var rel = new SQLOneToManyRelationship()
                    {
                        Root = rootConfig,
                        ForeignKey = new SQLMemberInfo(this, mem),
                        Left = PrimaryKey,
                        Right = fkCol
                    };

                    otmConfig.ParentRelationship = rel;
                    OneToMany.Add(rel);
                }
            }
        }
        /// <summary>
        /// Computes the table alias for each layer of the config.
        /// These aliases prevent collision of the table when they are the same.
        /// </summary>
        private void ComputeAlias()
        {
            int num = 0;
            ComputeAlias(ref num);
        }
        /// <summary>
        /// Recursively computes the table alias for each layer of the config.
        /// These aliases prevent collision of the table when they are the same.
        /// </summary>
        private void ComputeAlias(ref int num)
        {
            if (TableAlias != null)
                return;

            TableAlias = $"{TableName}_{++num}";

            foreach (var oto in OneToOne)
            {
                oto.Left.Config.ComputeAlias(ref num);
                oto.Right.Config.ComputeAlias(ref num);
            }

            foreach (var otm in OneToMany)
            {
                otm.Left.Config.ComputeAlias(ref num);
                otm.Right.Config.ComputeAlias(ref num);
            }
        }
    }
}

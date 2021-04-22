using SqlLinqer.Relationships;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;

namespace SqlLinqer.Queries
{
    /// <summary>
    /// Base object of sql queries
    /// </summary>
    /// <typeparam name="TObj">The class that represents the database table and its relationships</typeparam>
    public abstract class SQLBaseQuery<TObj> where TObj : SqlLinqerObject<TObj>
    {
        private SqlLinqerConnector _connector;

        /// <summary>
        /// The queries config of <typeparamref name="TObj"/>
        /// </summary>
        protected SQLConfig Config { get; private set; }
        /// <summary>
        /// The <see cref="SqlLinqerConnector.DBType"/>
        /// </summary>
        protected DBType DBType { get => _connector.DBType; }
        /// <summary>
        /// The <see cref="SqlLinqerConnector.ParameterLimit"/>
        /// </summary>
        protected int ParameterLimit { get => _connector.ParameterLimit; }

        /// <summary>
        /// Begins a query context
        /// </summary>
        /// <param name="recursionLevel">The recursion level to initialize the config with.</param>
        protected SQLBaseQuery(int recursionLevel = 1)
        {
            _connector = Default.Connector;
            Config = new SQLConfig(typeof(TObj), recursionLevel);
        }


        private MemberExpression GetMember(Expression<Func<TObj, object>> expr)
        {
            if (expr?.Body == null)
                throw new ArgumentException("Expression cannot be null");

            if (expr.Body is MemberExpression expression)
            {
                return expression;
            }
            else if (expr.Body is UnaryExpression unExpr)
            {
                return (MemberExpression)unExpr.Operand;
            }

            throw new ArgumentException("Invalid expression. Expression must be a member expression.");
        }
        private SQLMemberInfo GetMemberFromExpression(MemberExpression memExpr)
        {
            SQLMemberInfo column = null;

            if (!(memExpr.Expression is MemberExpression) && memExpr.Member.ReflectedType == Config.Type)
            {
                if (Config.PrimaryKey != null && Config.PrimaryKey.Info == memExpr.Member)
                    column = Config.PrimaryKey;
                else
                    column = Config.Columns.Find(c => c.Info == memExpr.Member);
            }
            else
            {
                var trace = new List<MemberExpression>();

                var tracker = memExpr;
                while (tracker.Expression is MemberExpression expression)
                {
                    tracker = expression;
                    trace.Insert(0, tracker);
                }

                UpgradeConfig(trace.Count);
                SQLConfig traceConfig = Config;

                while (trace.Count > 0 && traceConfig != null)
                {
                    var mem = trace.First();
                    trace.RemoveAt(0);

                    traceConfig =
                        traceConfig.OneToOne.Find(f => f.ForeignKey.Info == mem.Member)?.Right.Config
                        ?? traceConfig.OneToMany.Find(f => f.ForeignKey.Info == mem.Member)?.Right.Config;
                }

                if (traceConfig != null && memExpr.Member.ReflectedType == traceConfig.Type && trace.Count == 0)
                {
                    if (traceConfig.PrimaryKey != null && traceConfig.PrimaryKey.Info == memExpr.Member)
                        column = traceConfig.PrimaryKey;
                    else
                        column = traceConfig.Columns.Find(c => c.Info == memExpr.Member);
                }
            }

            if (column == null)
                throw new ArgumentException("Failed to find column from member. Make sure the pointed member is defined as a column.", "memExpr");

            return column;
        }
        private SQLRelationship GetRelationshipFromExpression(MemberExpression memExpr)
        {
            SQLRelationship relationship = null;

            if (!(memExpr.Expression is MemberExpression) && memExpr.Member.ReflectedType == Config.Type)
            {
                relationship = (SQLRelationship)
                    Config.OneToOne.Find(r => r.ForeignKey.Info == memExpr.Member)
                    ?? Config.OneToMany.Find(r => r.ForeignKey.Info == memExpr.Member);
            }
            else
            {
                var trace = new List<MemberExpression>();

                var tracker = memExpr;
                while (tracker.Expression is MemberExpression expression)
                {
                    tracker = expression;
                    trace.Insert(0, tracker);
                }

                if (Config.RecursionLevel < trace.Count)
                    UpgradeConfig(trace.Count);
                SQLConfig traceConfig = Config;

                while (trace.Count > 0 && traceConfig != null)
                {
                    var mem = trace.First();
                    trace.RemoveAt(0);

                    relationship = (SQLRelationship)
                        Config.OneToOne.Find(r => r.Left.Info == memExpr.Member)
                        ?? Config.OneToMany.Find(r => r.Left.Info == memExpr.Member);

                    traceConfig = relationship?.Right.Config;
                }

                if (traceConfig != null && memExpr.Member.ReflectedType == traceConfig.Type && trace.Count == 0)
                    return relationship;
                else
                    relationship = null;
            }

            if (relationship == null)
                throw new ArgumentException("Failed to find relationship from member. Make sure the pointed member is defined as a foreign key.", "memExpr");

            return relationship;
        }

        /// <summary>
        /// Sets the queries connector object.
        /// If the new connector is ignored if it is null.
        /// </summary>
        /// <param name="connector">The new connector object</param>
        protected void SetConnector(SqlLinqerConnector connector)
        {
            if (connector != null)
                _connector = connector;
        }

        /// <summary>
        /// Asserts that the current config for <typeparamref name="TObj"/> has a primary key of type <typeparamref name="TKey"/>.
        /// </summary>
        /// <typeparam name="TKey">The type of the primary key</typeparam>
        protected void AssertConfigHasPrimaryKey<TKey>()
        {
            if (Config.PrimaryKey == null)
                throw new FormatException($"The config for {typeof(TObj).FullName} does not have a primary key.");

            if (Config.PrimaryKey.MemberUnderlyingType != typeof(TKey))
                throw new FormatException($"Primary key type of {typeof(TKey).FullName} does not match the config's primary key type of {Config.PrimaryKey.MemberUnderlyingType.FullName}.");
        }
        /// <summary>
        /// Finds and returns the <see cref="SQLRelationship"/> of the pointed member from the <paramref name="expr"/>
        /// This also automatically upgrades the recursion level of the queries <see cref="SQLConfig"/>
        /// </summary>
        /// <param name="expr">The expression that points to the member</param>
        /// <returns>The <see cref="SQLRelationship"/> for the pointed member</returns>
        protected SQLRelationship GetRelationshipFromExpression(Expression<Func<TObj, object>> expr)
        {
            return GetRelationshipFromExpression(GetMember(expr));
        }
        /// <summary>
        /// Finds and returns the <see cref="SQLMemberInfo"/> of the pointed member from the <paramref name="expr"/>
        /// This also automatically upgrades the recursion level of the queries <see cref="SQLConfig"/>
        /// </summary>
        /// <param name="expr">The expression that points to the member</param>
        /// <returns>The <see cref="SQLMemberInfo"/> for the pointed member</returns>
        protected SQLMemberInfo GetMemberFromExpression(Expression<Func<TObj, object>> expr)
        {
            return GetMemberFromExpression(GetMember(expr));
        }
        /// <summary>
        /// Returns the passed string wrapped with the proper characters based on the <see cref="SqlLinqerConnector"/>
        /// </summary>
        /// <param name="name">The string to wrap</param>
        /// <returns>The passed string wrapped with the proper characters based on the <see cref="SqlLinqerConnector"/></returns>
        protected string Wrap(string name)
        {
            switch (_connector.DBType)
            {
                case DBType.OracleSQL:
                case DBType.PostgreSQL:
                    return $"\"{name}\"";
                case DBType.MYSQL:
                    return $"`{name}`";
                case DBType.SQLServer:
                default:
                    return $"[{name}]";
            }
        }
        /// <summary>
        /// Creates a list of <typeparamref name="TObj"/> with their information populated base on the query's <see cref="SQLConfig"/>
        /// </summary>
        /// <param name="table"><see cref="DataTable"/> with source data</param>
        /// <param name="useColumnAlias">if to use aliases in the parsing</param>
        /// <returns>A <see cref="List{T}"/> of constructed objects</returns>
        protected List<TObj> PopulateObject(DataTable table, bool useColumnAlias)
        {
            var type = typeof(TObj);
            var objs = new List<TObj>();
            foreach (DataRow row in table.Rows)
            {
                var rowref = row;
                objs.Add((TObj)SQLBaseQuery.PopulateObject(type, Config, rowref, useColumnAlias));
            }
            return objs;
        }
        /// <summary>
        /// Increases the recursion level of the queries config
        /// </summary>
        /// <param name="recursionLevel"></param>
        protected void UpgradeConfig(int recursionLevel)
        {
            if (recursionLevel > 0 && Config.RecursionLevel < recursionLevel)
                Config = new SQLConfig(typeof(TObj), recursionLevel);
        }

        /// <summary>
        /// Create new database connection object using the connector
        /// </summary>
        /// <returns>A new connection object</returns>
        protected DbConnection CreateConnection()
        {
            if (_connector == null)
                throw new ArgumentNullException("_connector", "Must set the SqlLinqer default connector or provide one to the method");

            return _connector.CreateConnection();
        }
        /// <summary>
        /// Executes a database command that returns rows and columns of data using the connector
        /// </summary>
        /// <param name="command">The command to be exectuted. The command must have its connection set.</param>
        /// <returns>A <see cref="SQLResponse{T}"/> object with a <see cref="DataTable"/> that contains the results of the query</returns>
        protected SQLResponse<DataTable> ExecuteReader(DbCommand command)
        {
            return _connector.ExecuteReader(command);
        }
        /// <summary>
        /// Executes a database command the returns a <typeparamref name="T"/> which is the first column of the first row
        /// This happens trough a transaction and the transaction is rolled back if there is an error thrown.
        /// </summary>
        /// <param name="command">The command to be executed. The command must have its connection set.</param>
        /// <returns>A <see cref="SQLResponse{T}"/> that contains the first column of the first row</returns>
        protected SQLResponse<T> ExecuteNonQuery<T>(DbCommand command)
        {
            return _connector.ExecuteNonQuery<T>(command);
        }
        /// <summary>
        /// Executes a database command the returns a <see cref="long"/> which is the number of affected rows.
        /// This happens trough a transaction and the transaction is rolled back if there is an error thrown.
        /// </summary>
        /// <param name="command">The command to be executed. The command must have its connection set.</param>
        /// <returns>A <see cref="SQLResponse{T}"/> object with a <see cref="long"/></returns>
        protected SQLResponse<long> ExecuteNonQuery(DbCommand command)
        {
            return _connector.ExecuteNonQuery(command);
        }
        /// <summary>
        /// Executes multiple database commands with the same connection and returns the number of affected rows across all commands. 
        /// This happens trough a transaction and the transaction is rolled back if there is an error thrown.
        /// Intended use is for the <see cref="SQLInsertQuery{TObj}"/> to insert in batches.
        /// </summary>
        /// <param name="commands">The commands to be executed. The command must have its connection set and they must all be the same connection.</param>
        /// <returns>A <see cref="SQLResponse{T}"/> object with a <see cref="long"/></returns>
        protected SQLResponse<long> ExecuteNonQuery(IEnumerable<DbCommand> commands)
        {
            return _connector.ExecuteNonQuery(commands);
        }
    }

    internal static class SQLBaseQuery
    {
        public static object PopulateObject(Type type, SQLConfig config, DataRow row, bool useColumnAlias)
        {
            object obj = Activator.CreateInstance(type);

            bool foundData = false;

            // populate primary key
            if (config.PrimaryKey != null)
            {
                string col;
                switch (useColumnAlias)
                {
                    case false:
                        col = config.PrimaryKey.SQLName;
                        break;
                    default:
                        col = config.PrimaryKey.ColumnAlias;
                        break;
                }
                if (row.Table.Columns.Contains(col))
                {
                    foundData = true;
                    try
                    {
                        if (row[col].GetType() == config.PrimaryKey.MemberUnderlyingType)
                            config.PrimaryKey.SetValue(obj, row[col]);
                        else
                        {
                            TypeConverter typeConverter = TypeDescriptor.GetConverter(config.PrimaryKey.MemberUnderlyingType);
                            var value = row[col] == DBNull.Value
                                ? default
                                : typeConverter.ConvertFrom(row[col]);
                            config.PrimaryKey.SetValue(obj, value);
                        }
                    }
                    catch (ArgumentException err)
                    {
                        throw new FormatException($"Failed to set the value of {config.Type.FullName}.{config.PrimaryKey.Info.Name}", err);
                    }
                }
            }

            // populate column data
            foreach (SQLMemberInfo column in config.Columns)
            {
                string col;
                switch (useColumnAlias)
                {
                    case false:
                        col = column.SQLName;
                        break;
                    default:
                        col = column.ColumnAlias;
                        break;
                }
                if (row.Table.Columns.Contains(col))
                {
                    if (!foundData) foundData = true;
                    try
                    {
                        if (row[col].GetType() == column.MemberUnderlyingType)
                            column.SetValue(obj, row[col]);
                        else
                        {
                            TypeConverter typeConverter = TypeDescriptor.GetConverter(column.MemberUnderlyingType);
                            var value = row[col] == DBNull.Value
                                ? default
                                : typeConverter.ConvertFrom(row[col]);
                            column.SetValue(obj, value);
                        }
                    }
                    catch (ArgumentException err)
                    {
                        throw new FormatException($"Failed to set the value of {config.Type.FullName}.{column.Info.Name}", err);
                    }
                }
            }

            // populate joined one to one relationships
            foreach (SQLOneToOneRelationship relationship in config.OneToOne)
            {
                Type sub_type = relationship.ForeignKey.MemberUnderlyingType;
                SQLConfig right_config = relationship.Right.Config;
                object sub_obj = PopulateObject(sub_type, right_config, row, useColumnAlias);

                if (sub_obj != null && !foundData) foundData = true;

                relationship.ForeignKey.SetValue(obj, sub_obj);
            }

            if (foundData)
                return obj;
            else
                return null;
        }
    }
}

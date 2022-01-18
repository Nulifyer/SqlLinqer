using SqlLinqer.Relationships;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;

namespace SqlLinqer.Queries
{
    /// <summary>
    /// Select query for a specific object with some primary key value
    /// </summary>
    /// <typeparam name="TObj">The class that represents the database table and its relationships</typeparam>
    /// <typeparam name="TKey">The value type of the primary key</typeparam>
    public sealed class SQLSelectQuery<TObj, TKey> : SQLWhereQuery<TObj> where TObj : new()
    {
        private readonly Dictionary<string, SQLMemberInfo> _selectedColumns;
        private readonly TKey _primaryKeyValue;

        internal SQLSelectQuery(TKey primaryKeyValue, int recursionLevel = 1)
            : base(recursionLevel)
        {
            _selectedColumns = new Dictionary<string, SQLMemberInfo>();

            UpgradeConfig(recursionLevel);

            _primaryKeyValue = primaryKeyValue;

            SelectOneToOne(Config);
        }

        private void SelectOneToOne(SQLConfig config)
        {
            config.Columns
                .ForEach(column =>
                {
                    string key = column.ColumnAlias;
                    if (_selectedColumns.ContainsKey(key)) return;
                    _selectedColumns.Add(key, column);
                });

            if (config.PrimaryKey != null)
            {
                string key = config.PrimaryKey.ColumnAlias;
                if (!_selectedColumns.ContainsKey(key))
                    _selectedColumns.Add(key, config.PrimaryKey);
            }

            config.OneToOne.ForEach(oto => SelectOneToOne(oto.Right.Config));
        }
        private string GetColumnStr()
        {
            var columnList = _selectedColumns.Select(x => x.Value);

            var columns = new List<string>();

            foreach (var column in columnList)
            {
                columns.Add($"{Wrap(column.Config.TableAlias)}.{Wrap(column.SQLName)} AS {Wrap(column.ColumnAlias)}");
            }

            return string.Join(",", columns);
        }
        private new void GetJoins(Dictionary<string, SQLConfig> joins)
        {
            var columnList = _selectedColumns.Select(x => x.Value);

            foreach (var column in columnList)
            {
                if (joins.ContainsKey(column.Config.TableAlias)) continue;
                joins.Add(column.Config.TableAlias, column.Config);
            }

            base.GetJoins(joins);
        }

        /// <summary>
        /// Execute the query and return results
        /// </summary>
        /// <param name="connector">The <see cref="SqlLinqerConnector"/> to use. If null the <see cref="Default.Connector"/> is used instead.</param>
        /// <returns>A <see cref="SQLResponse{T}"/> containing the results or the error</returns>
        public SQLSelectResponse<TObj> Run(SqlLinqerConnector connector = null)
        {
            SetConnector(connector);

            var result = new SQLSelectResponse<TObj>();

            DbConnection conn;
            try
            {
                conn = CreateConnection();
            }
            catch (Exception err)
            {
                return result.SetError(err);
            }

            base.Where(Config.PrimaryKey, _primaryKeyValue, SQLOp.EQ);

            // build command string
            DbCommand cmd = conn.CreateCommand();

            var joins = new Dictionary<string, SQLConfig>();
            GetJoins(joins);

            cmd.CommandText = $"SELECT {GetColumnStr()} FROM {RenderJoins(joins)} ";

            RenderWhere(cmd);

            bool useColumnAlias = true;

            SQLResponse<DataTable> response = ExecuteReader(cmd);
            if (response.State == ResponseState.Error)
                return result.SetError(response.Error);

            DataTable data = response.Result;

            try
            {
                result.Result = PopulateObject(data, useColumnAlias).FirstOrDefault();
                result.TotalResults = result.Result != null ? 1 : 0;
            }
            catch (Exception err)
            {
                result.Error = new SQLResponseException(cmd, innerException: err);
            }

            return result;
        }
    }
}

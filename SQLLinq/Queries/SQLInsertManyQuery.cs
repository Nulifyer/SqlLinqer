using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;

namespace SqlLinqer.Queries
{
    /// <summary>
    /// Insert in batches query.
    /// Batch size is automatically determined based on the connector's parameter limit.
    /// </summary>
    /// <typeparam name="TObj">The class that represents the database table and its relationships</typeparam>
    public class SQLInsertManyQuery<TObj> : SQLBaseQuery<TObj> where TObj : SqlLinqerObject<TObj>
    {
        private readonly IEnumerable<TObj> _objs;

        internal SQLInsertManyQuery(IEnumerable<TObj> objs)
            : base(recursionLevel: 0)
        {
            _objs = objs;
        }

        /// <summary>
        /// Execute the query and return results
        /// </summary>
        /// <param name="connector">The <see cref="SqlLinqerConnector"/> to use. If null the <see cref="Default.Connector"/> is used instead.</param>
        /// <returns>A <see cref="SQLResponse{T}"/> containing the results or the error</returns>
        public SQLResponse<long> Run(SqlLinqerConnector connector = null)
        {
            SetConnector(connector);

            DbConnection conn;
            try
            {
                conn = CreateConnection();
            }
            catch (Exception err)
            {
                return new SQLResponse<long>(err);
            }

            if (_objs == null || _objs.Count() == 0)
                return new SQLResponse<long>(0);

            // create batches
            bool includePK = Config.PrimaryKey?.DBGenerated ?? false;
            double numCols = (includePK ? 1 : 0) + Config.Columns.Count();
            int numBatches = (int)Math.Ceiling(numCols * _objs.Count() / ParameterLimit);
            int batchSize = _objs.Count() / numBatches;

            var batches = Batch(_objs, batchSize);
            var cmds = new List<DbCommand>();

            // create batch commands
            foreach (var batch in batches)
            {
                var cols = new List<string>();
                var vals = new List<List<string>>();

                DbCommand cmd = conn.CreateCommand();

                // parameterize data
                for (int i = 0; i < batch.Count(); ++i)
                {
                    var obj = batch.ElementAt(i);
                    var itemVals = new List<string>();
                    if (includePK)
                    {
                        string col = Config.PrimaryKey.SQLName;
                        if (i == 0) cols.Add(Wrap(col));
                        itemVals.Add($"@{i}_{col}");

                        DbParameter param = cmd.CreateParameter();
                        param.ParameterName = $"@{i}_{col}";
                        param.Value = Config.PrimaryKey.GetValue(obj);

                        cmd.Parameters.Add(param);
                    }
                    foreach (var item in Config.Columns)
                    {
                        string col = item.SQLName;
                        if (i == 0) cols.Add(Wrap(col));
                        itemVals.Add($"@{i}_{col}");

                        DbParameter param = cmd.CreateParameter();
                        param.ParameterName = $"@{i}_{col}";
                        param.Value = item.GetValue(obj);

                        cmd.Parameters.Add(param);
                    }
                    vals.Add(itemVals);
                }

                // build command string
                StringBuilder builder = new StringBuilder();
                builder.Append($"INSERT INTO {Wrap(Config.TableName)} ({string.Join(",", cols)}) VALUES ");
                bool firstInsert = true;
                foreach (var item in vals)
                {
                    if (!firstInsert)
                        builder.Append(", ");
                    else
                        firstInsert = false;
                    builder.Append($"({string.Join(",", item)})");
                }
                cmd.CommandText = builder.ToString();

                cmds.Add(cmd);
            }

            // execute batch commands
            return ExecuteNonQuery(cmds);
        }

        private static IEnumerable<IEnumerable<T>> Batch<T>(IEnumerable<T> source, int size)
        {
            if (size < 1) size = 1;
            var list = source.ToList();
            for (int i = 0; i < list.Count(); i += size)
            {
                yield return list.GetRange(i, Math.Min(size, list.Count() - i));
            }
        }
    }
}

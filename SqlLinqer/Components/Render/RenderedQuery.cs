using System;
using System.Linq;

namespace SqlLinqer.Components.Render
{
    /// <summary>
    /// Holds parameterized SQL command text, its bindings, and optional logical SELECT result-shape metadata.
    /// </summary>
    public class RenderedQuery
    {
        internal bool Transactional;
        // Set only by the SELECT compiler. Raw scalar commands retain their
        // existing transaction behavior because they can perform writes.
        internal bool IsReadOnlySelect;
        /// <summary>An optional diagnostic name. It does not modify the SQL text or parameter bindings.</summary>
        public string QueryName { get; set; }
        /// <summary>
        /// The logical selected member structure for a regular SELECT. Count and other commands can leave it null.
        /// SQL expressions without a declared member type use object metadata.
        /// </summary>
        public SqlLinqer.Compilation.SqlResultShape ResultShape { get; internal set; }

        /// <summary>
        /// The parameterized SQL command text. Execute it together with Parameters so application values remain bound.
        /// </summary>
        public string Text;
        /// <summary>
        /// The bindings referenced by Text. Each ordinary query render returns fresh bindings.
        /// Do not share mutable bindings between concurrent renders or executions.
        /// </summary>
        public ParameterCollection Parameters;

        /// <summary>
        /// A rendered query with the query text and the parameters
        /// </summary>
        public RenderedQuery()
        {
            Parameters = new ParameterCollection();
        }
        /// <summary>
        /// A rendered query with the query text and the parameters
        /// </summary>
        /// <param name="text">The text of the query</param>
        public RenderedQuery(string text) : this()
        {
            Text = text;
        }

        /// <summary>
        /// Returns diagnostic SQL with input values substituted. Output and input/output parameter bindings remain placeholders.
        /// </summary>
        /// <returns>Diagnostic command text, or the original empty/null text.</returns>
        public string GetTextWithParameterValues()
        {
            if (string.IsNullOrEmpty(this.Text))
                return this.Text;

            string newText = this.Text;
            var @params = Parameters.GetAll()
                .OrderByDescending(x => x.Placehodler);
            foreach (var param in @params)
            {
                if (param is CallableParameter callable && callable.Direction != System.Data.ParameterDirection.Input)
                    continue;

                if (param.Value == null || param.Value == DBNull.Value || param.Value is SqlNull)
                {
                    newText = newText.Replace(param.Placehodler, "NULL");
                    continue;
                }

                string valueStr;
                switch (param.Value)
                {
                    case System.Data.DataTable val:
                        valueStr = $"<{typeof(System.Data.DataTable).FullName}>";
                        break;
                    case bool val:
                        valueStr = (val ? 1 : 0).ToString();
                        break;
                    case DateTime val:
                        valueStr = val.ToString("yyyy-MM-dd hh:mm:ss");
                        break;
                    case DateTimeOffset val:
                        valueStr = val.ToString("yyyy-MM-ddTHH:mm:ss.fffK");
                        break;
                    default:
                        valueStr = param.Value.ToString();
                        break;
                }

                switch (param.Value)
                {
                    case bool val: break;
                    case int val: break;
                    case decimal val: break;
                    case long val: break;
                    case double val: break;
                    case float val: break;
                    default:
                        valueStr = $"'{valueStr.ToString().Replace("'", "''")}'";
                        break;
                }
                newText = newText.Replace(param.Placehodler, valueStr);
            }
            return newText;
        }

        /// <summary>Returns diagnostic SQL with parameter values substituted. Do not use it to execute commands.</summary>
        public override string ToString()
        {
            return GetTextWithParameterValues();
        }
    }
}

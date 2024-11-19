using System;
using System.Linq;

namespace SqlLinqer.Components.Render
{
    /// <summary>
    /// A rendered query with the query text and the parameters
    /// </summary>
    public class RenderedQuery
    {
        /// <summary>
        /// The text of the query
        /// </summary>
        public string Text;
        /// <summary>
        /// The parameters of the query
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
        /// Get the text of the query with the placeholders replaced with strings of the values
        /// </summary>
        public string GetTextWithParameterValues()
        {
            if (string.IsNullOrEmpty(this.Text))
                return this.Text;

            string newText = this.Text;
            var @params = Parameters.GetAll()
                .OrderByDescending(x => x.Placehodler);
            foreach (var param in @params)
            {
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
                    case SqlNull.NULL:
                        valueStr = null;
                        break;
                    default:
                        valueStr = param.Value?.ToString();
                        break;
                }
                
                if (param.Value == DBNull.Value)
                    valueStr = null;

                switch (param.Value)
                {
                    case bool val: break;
                    case int val: break;
                    case decimal val: break;
                    case long val: break;
                    case double val: break;
                    case float val: break;
                    case null:
                        valueStr = "NULL";
                        break;
                    default:
                        valueStr = $"'{valueStr.ToString().Replace("'", "''")}'";
                        break;
                }
                newText = newText.Replace(param.Placehodler, valueStr);
            }
            return newText;
        }

        public override string ToString()
        {
            return GetTextWithParameterValues();
        }
    }
}
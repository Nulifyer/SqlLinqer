using SqlLinqer.Components.Render;
using SqlLinqer.Queries.Core.Abstract;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Extensions.StringExtensions;
using SqlLinqer.Components.Generic;
using SqlLinqer.Components.Outputs;

namespace SqlLinqer.Queries.Core
{
    public class DeleteQuery : WhereQuery
    {
        protected internal OutputsCollection Outputs;
        
        public DeleteQuery(Table table) : base(table)
        {
            Outputs = new OutputsCollection();
        }

        /// <inheritdoc/>
        public override string Render(ParameterCollection parameters, DbFlavor flavor)
        {
            var stashables = new IStashable[]
            {
                this.RootWhere,
                this.Withs,
                this.Joins,
                this.Outputs,
            };

            foreach (var item in stashables)
                item?.Stash();

            string where = this.RootWhere.Render(this, parameters, flavor);
            string with = this.Withs.Render(this, parameters, flavor);
            string joins = this.Joins.Render(this, parameters, flavor);

            foreach (var item in stashables)
                item?.Unstash();

            return string.Join(" ", new[]
            {
                with,
                $"DELETE {Table.Alias.DbWrap(flavor)}",
                joins,
                where,
            });
        }
    }
}
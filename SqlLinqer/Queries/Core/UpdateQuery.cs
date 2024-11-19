using SqlLinqer.Components.Render;
using SqlLinqer.Components.Update;
using SqlLinqer.Queries.Core.Abstract;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Outputs;
using SqlLinqer.Components.Generic;

namespace SqlLinqer.Queries.Core
{
    public class UpdateQuery : WhereQuery
    {
        protected internal UpdatesCollection Updates;
        protected internal OutputsCollection Outputs;

        public UpdateQuery(Table table) : base(table)
        {
            Updates = new UpdatesCollection();
            Outputs = new OutputsCollection();
        }

        public override string Render(ParameterCollection parameters, DbFlavor flavor)
        {
            var stashables = new IStashable[]
            {
                this.RootWhere,
                this.Withs,
                this.Joins,
                this.Updates,
                this.Outputs,
            };

            foreach (var item in stashables)
                item?.Stash();

            string where = this.RootWhere.Render(this, parameters, flavor);
            string with = this.Withs.Render(this, parameters, flavor);
            string updates = this.Updates.Render(this, parameters, flavor);
            string outputs = this.Outputs.Render(flavor);
            string joins = this.Joins.Render(this, parameters, flavor);

            foreach (var item in stashables)
                item?.Unstash();

            return string.Join(" ", new[]
            {
                with,
                updates,
                outputs,
                joins,
                where,
            });
        }
    }
}
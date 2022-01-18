namespace SqlLinqer.Relationships
{
    internal class SQLWith<TObj>
    {
        public string Alias { get; set; }
        public string OutputCol { get; set; }
        public string CommandText { get; set; }
        public int MinHavingCount { get; set; }

        public uint Order { get; set; }
        public bool TopLevel { get; set; }

        public SQLConfig Config { get; set; }
        public SQLWhereGroup<TObj> WhereGroup { get; set; }
        public SQLWith<TObj> NestedWith { get; set; }

        public SQLWith(SQLGroupOp op = SQLGroupOp.AND)
        {
            TopLevel = false;
            Order = 0;
            MinHavingCount = -1;
            WhereGroup = new SQLWhereGroup<TObj>(op);
        }
    }
}

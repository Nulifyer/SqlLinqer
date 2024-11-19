using System.ComponentModel;
using SqlLinqer.Extensions.EnumExtensions;

namespace SqlLinqer
{
    /// <summary>
    /// Sql where clause operators
    /// </summary>
    public enum SqlOp
    {
        /// <summary>
        /// Equals
        /// </summary>
        [Description("=")]
        EQ,
        /// <summary>
        /// Greater than
        /// </summary>
        [Description(">")]
        GT,
        /// <summary>
        /// Less than
        /// </summary>
        [Description("<")]
        LT,
        /// <summary>
        /// Greater or equal than
        /// </summary>
        [Description(">=")]
        GTE,
        /// <summary>
        /// Less or equal than
        /// </summary>
        [Description("<=")]
        LTE,
        /// <summary>
        /// Not
        /// </summary>
        [Description("!=")]
        NOT,
        /// <summary>
        /// Like
        /// </summary>
        [Description("LIKE")]
        LIKE,
        /// <summary>
        /// Not Like
        /// </summary>
        [Description("NOT LIKE")]
        NOTLIKE,
        /// <summary>
        /// Not Like
        /// </summary>
        [Description("NOT LIKE")]
        NOTLIKEORNULL,
        /// <summary>
        /// Like word performs multiple conditions as an OR:
        /// column == value
        /// column LIKE '{value}[^0-9A-z]%'
        /// column LIKE '%[^0-9A-z]{value}%'
        /// column LIKE '%[^0-9A-z]{value}[^0-9A-z]%'
        /// </summary>
        [Description("LIKE")]
        LIKEWORD,
        /// <summary>
        /// Not Like
        /// </summary>
        [Description("NOT LIKE")]
        NOTLIKEWORD,
        /// <summary>
        /// Not Like
        /// </summary>
        [Description("NOT LIKE")]
        NOTLIKEWORDORNULL,
    }

    internal static class SqlOpExtensions
    {
        public static string GetOpString(this SqlOp op, DbFlavor flavor)
        {
            switch (flavor)
            {
                case DbFlavor.PostgreSql:
                    switch (op)
                    {
                        case SqlOp.LIKE:
                            return "ILIKE";
                        case SqlOp.NOTLIKE:
                            return "NOT ILIKE";
                    }
                    break;
            }
            return op.GetDescription();
        }
        public static SqlOp GetInvert(this SqlOp op)
        {
            switch (op)
            {
                case SqlOp.EQ:
                    return SqlOp.NOT;
                case SqlOp.GT:
                    return SqlOp.LTE;
                case SqlOp.LT:
                    return SqlOp.GTE;
                case  SqlOp.GTE:
                    return SqlOp.LT;
                case  SqlOp.LTE:
                    return SqlOp.GT;
                case  SqlOp.NOT:
                    return SqlOp.EQ;
                case SqlOp.LIKE:
                    return SqlOp.NOTLIKE;
                case SqlOp.NOTLIKE:
                case SqlOp.NOTLIKEORNULL:
                    return SqlOp.LIKE;
                case SqlOp.LIKEWORD:
                    return SqlOp.NOTLIKEWORD;
                case SqlOp.NOTLIKEWORD:
                case SqlOp.NOTLIKEWORDORNULL:
                    return SqlOp.LIKEWORD;
                default:
                    throw new System.NotSupportedException($"Invert not supported for {op}");
            }
        }
    }
}

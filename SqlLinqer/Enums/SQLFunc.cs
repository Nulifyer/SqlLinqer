using System.ComponentModel;

namespace SqlLinqer
{
    /// <summary>
    /// Sql Function identifiers
    /// </summary>
    public enum SqlFunc
    {
        /* *******************************************************
         * Single Column Functions
         * *******************************************************
         */
        /// <summary>
        /// Absolute value
        /// </summary>
        [Description("ABS")]
        ABS,
        /// <summary>
        /// Date of date type value
        /// </summary>
        [Description("DAY")]
        DAY,
        /// <summary>
        /// Lowercase
        /// </summary>
        [Description("LOWER")]
        LOWER,
        /// <summary>
        /// Trim left
        /// </summary>
        [Description("LTRIM")]
        LTRIM,
        /// <summary>
        /// Month of date type value
        /// </summary>
        [Description("MONTH")]
        MONTH,
        /// <summary>
        /// Reverse value
        /// </summary>
        [Description("REVERSE")]
        REVERSE,
        /// <summary>
        /// Extracts a number of characters from a string, starting from right side
        /// </summary>
        [Description("RIGHT")]
        RIGHT,
        /// <summary>
        /// Trim right
        /// </summary>
        [Description("RTRIM")]
        RTRIM,
        /// <summary>
        /// SoundDex of value
        /// </summary>
        [Description("SOUNDDEX")]
        SOUNDDEX,
        /// <summary>
        /// Substring of a value
        /// </summary>
        [Description("SUBSTRING")]
        SUBSTRING,
        /// <summary>
        /// Trim value
        /// </summary>
        [Description("TRIM")]
        TRIM,
        /// <summary>
        /// Uppercase
        /// </summary>
        [Description("UPPER")]
        UPPER,
        /// <summary>
        /// Year of date type value
        /// </summary>
        [Description("YEAR")]
        YEAR,

        /* *******************************************************
         * Multi Column Functions
         * *******************************************************
         */

        /// <summary>
        /// Coalesce
        /// </summary>
        [Description("COALESCE")]
        COALESCE,
        /// <summary>
        /// Concat
        /// </summary>
        [Description("CONCAT")]
        CONCAT,
        /// <summary>
        /// CONCAT_WS
        /// </summary>
        [Description("CONCAT_WS")]
        CONCAT_WS,

        /* *******************************************************
         * Aggregate Functions
         * *******************************************************
         */

        /// <summary>
        /// Average value
        /// </summary>
        [Description("AVG")]
        AVG,
        /// <summary>
        /// This function returns the checksum of the values in a group
        /// </summary>
        [Description("CHECKSUM_AGG")]
        CHECKSUM_AGG,
        /// <summary>
        /// Count based on value
        /// </summary>
        [Description("COUNT")]
        COUNT,
        /// <summary>
        /// Count based on value, return long
        /// </summary>
        [Description("COUNT_BIG")]
        COUNT_BIG,
        /// <summary>
        /// Max value
        /// </summary>
        [Description("MAX")]
        MAX,
        /// <summary>
        /// Minimum value
        /// </summary>
        [Description("MIN")]
        MIN,
        /// <summary>
        /// Statistical standard deviation 
        /// </summary>
        [Description("STDEV")]
        STDEV,
        /// <summary>
        /// Statistical standard deviation for the population
        /// </summary>
        [Description("STDEVP")]
        STDEVP,
        /// <summary>
        /// Concatenates the values of string expressions and places separator values between them
        /// </summary>
        [Description("STRING_AGG")]
        STRING_AGG,
        /// <summary>
        /// Sum of values
        /// </summary>
        [Description("SUM")]
        SUM,
        /// <summary>
        /// Returns the statistical variance of all values in the specified expression
        /// </summary>
        [Description("VAR")]
        VAR,
        /// <summary>
        /// Returns the statistical variance for the population for all values in the specified expression
        /// </summary>
        [Description("VARP")]
        VARP,
    }

    public static class SqlFuncExtensions
    {
        public static bool DoGroupBy(this SqlFunc func)
        {
            switch (func)
            {
                case SqlFunc.AVG:
                case SqlFunc.CHECKSUM_AGG:
                case SqlFunc.COUNT:
                case SqlFunc.COUNT_BIG:
                case SqlFunc.MAX:
                case SqlFunc.MIN:
                case SqlFunc.STDEV:
                case SqlFunc.STDEVP:
                case SqlFunc.STRING_AGG:
                case SqlFunc.SUM:
                case SqlFunc.VAR:
                case SqlFunc.VARP:
                    return false;
                default:
                    return true;
            }
        }
        public static bool GroupByOutput(this SqlFunc func)
        {
            switch (func)
            {
                case SqlFunc.ABS:
                case SqlFunc.DAY:
                case SqlFunc.LOWER:
                case SqlFunc.LTRIM:
                case SqlFunc.MONTH:
                case SqlFunc.REVERSE:
                case SqlFunc.RIGHT:
                case SqlFunc.RTRIM:
                case SqlFunc.SOUNDDEX:
                case SqlFunc.SUBSTRING:
                case SqlFunc.TRIM:
                case SqlFunc.UPPER:
                case SqlFunc.YEAR:
                case SqlFunc.COALESCE:
                case SqlFunc.CONCAT:
                case SqlFunc.CONCAT_WS:
                    return true;
                default:
                    return false;
            }
        }
    }
}

using System.ComponentModel;

namespace SqlLinqer
{
    /// <summary>
    /// Sql Math Operations
    /// </summary>
    public enum SqlMathOperation
    {
        /// <summary>
        /// Addition
        /// </summary>
        [Description("+")]
        ADD,

        /// <summary>
        /// Subtraction
        /// </summary>
        [Description("-")]
        SUB,
        
        /// <summary>
        /// Multiplication
        /// </summary>
        [Description("*")]
        MULT,
        
        /// <summary>
        /// Division
        /// </summary>
        [Description("/")]
        DIV,
        
        /// <summary>
        /// Modulous
        /// </summary>
        [Description("%")]
        MOD,
    }
}

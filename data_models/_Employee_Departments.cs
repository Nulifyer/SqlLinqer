using System;
using SqlLinqer.Modeling;

namespace SqlTest.Models
{
    [SqlTable("dept_emp")]
    public class _Employee_Departments : SqlLinqerObject<_Employee_Departments>, IDisplayable
    {
        [SqlColumn("emp_no")]
        public int? EmpID;
        
        [SqlColumn("dept_no")]
        public string DeptID;

        [SqlColumn("from_date")]
        public DateTime? FromDate;

        [SqlColumn("to_date")]
        public DateTime? ToDate;

        
        [SqlOneToOne(nameof(EmpID), nameof(Models._Employee.Id))]
        public _Employee _Employee { get; set; }

        [SqlOneToOne(nameof(DeptID))]
        public _Department _Department { get; set; }


        public string ToDisplayableString()
        {
            return $"({EmpID}) {_Employee?.ToDisplayableString()} <> ({DeptID}) {_Department?.ToDisplayableString()}";
        }
    }
}
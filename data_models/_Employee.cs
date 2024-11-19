using System;
using System.Collections.Generic;
using SqlLinqer.Modeling;

namespace SqlTest.Models
{
    [SqlTable("employees")]
    public class _Employee : SqlLinqerPrimaryKeyObject<_Employee, int>, IDisplayable
    {
        [SqlPrimaryKey(true)]
        [SqlColumn("emp_no")]
        public int? Id { get; set; }

        [SqlColumn("first_name")]
        public string FirstName { get; set; }

        [SqlColumn("last_name")]
        public string LastName { get; set; }

        [SqlColumn("gender")]
        public EmployeeGener? Gender { get; set; }

        [SqlColumn("hire_date")]
        public DateTime? HireDate { get; set; }


        [SqlOneToOne(nameof(Id))]
        public _Employee SameEmployee { get; set; }

        [SqlOneToOne(nameof(Id), nameof(_Employee.Id))]
        public _Employee SameEmployee2 { get; set; }

        [SqlManyToMany(typeof(_Employee_Departments), nameof(_Employee_Departments.EmpID), nameof(_Employee_Departments.DeptID))]
        public IEnumerable<_Department> Departments { get; set; }


        [SqlIgnore]
        public string DisplayName { get => $"{LastName}, {FirstName}"; }

        public string ToDisplayableString()
        {
            return Id != null
                ? $"[{Id}] {DisplayName} {Gender}"
                : null;
        }
    }
}
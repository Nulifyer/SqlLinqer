using System;
using SqlLinqer;
using SqlTest;
using MySql.Data.MySqlClient;
using SqlTest.Models;
using System.Linq;

namespace SqlTestFramework
{
    class Program
    {
        static void Main(string[] args)
        {
            // SqlLinqer.Default.Connector =
            //     new SqlLinqer.Connections.ReplicatorConnector(new MySql.Data.MySqlClient.MySqlConnection(
            //         $"Server=localhost;Database=employees;Uid=dbuser;Pwd=dbpass;"
            //     ), DbFlavor.MySql);

            SqlLinqer.Default.Connector =
                new SqlLinqer.Connections.ReplicatorConnector(new Npgsql.NpgsqlConnection(
                    $"Host=localhost;Database=employees;Username=dbuser;Password=dbpass;"),
                    DbFlavor.PostgreSql);

            var testconn = SqlLinqer.Default.Connector.ExecuteScalar<int>(new SqlLinqer.Components.Render.RenderedQuery("SELECT 1"));

            var res1 = _Employee
                .BeginSelect()
                .SelectRootColumns()
                .Select(x => x.Departments)
                .Top(10)
                .ExecuteWithTotalCount();

            if (res1.State == ResponseState.Valid)
                foreach (IDisplayable item in res1.Result)
                    Console.WriteLine(item.ToDisplayableString());
            else
            {
                Console.WriteLine(res1.Error.Message);
                Console.WriteLine((res1.Error as SqlLinqer.Exceptions.SqlResponseException)?.Query.GetTextWithParameterValues());
            }

            Console.WriteLine("-----------------------------------------------------");

            var q = _Employee_Departments
                .BeginSelect();

            var group = q
                .NewWhere(SqlWhereOp.AND)
                .Where(x => x._Employee.Id, SqlNull.NULL, SqlOp.NOT);

            var res = q
                .SelectRootColumns()
                .Where(group)
                .Where(q.NewWhere(SqlWhereOp.OR)
                    .Where(x => x._Department.Name, "%dev%", SqlOp.LIKE)
                    .Where(q.NewWhere(SqlWhereOp.AND)
                        .Where(x => x._Department.Name, "Re%", SqlOp.LIKE)
                        .Where(x => x._Department.Name, "%search%", SqlOp.NOTLIKE)
                    )
                )
                .Where(x => x._Department.Name, "%dev%", SqlOp.LIKE)
                .Where(x => x._Department.Name, "%ment%", SqlOp.LIKE)
                .Where(q.NewWhere(SqlWhereOp.OR)
                    .Where(x => x._Employee.Departments.First().Name, "%dev%", SqlOp.LIKE)
                    .Where(q.NewWhere(SqlWhereOp.AND)
                        .Where(x => x._Employee.Departments.First().Name, "%lop%", SqlOp.LIKE)
                        .Where(x => x._Employee.Departments.First().Name, "%search%", SqlOp.NOTLIKE)
                    )
                    .Where(x => x._Employee.FirstName, "%deep%", SqlOp.LIKE)
                    .Where(x => x._Employee.Departments.First().Name, "%dev%", SqlOp.LIKE)
                )
                .Page(2, 10)
                .ExecuteWithTotalCount();

            if (res.State == ResponseState.Valid)
                foreach (IDisplayable item in res.Result)
                    Console.WriteLine(item.ToDisplayableString());
            else
            {
                Console.WriteLine(res.Error.Message);
                Console.WriteLine((res.Error as SqlLinqer.Exceptions.SqlResponseException)?.Query.GetTextWithParameterValues());
            }

            Console.WriteLine("-----------------------------------------------------");

            var res_agg = _Employee_Departments
                .BeginSelectBuilder()
                .BuildQuery(x => new
                {
                    Id = x.Select(a => a._Employee.Id),
                    FirstName = x.Select(a => a._Employee.FirstName),
                    LastName = x.Select(a => a._Employee.LastName),
                    HireDay = x.DAY(a => a._Employee.HireDate),
                    HireDayName = x.YEAR(a => a._Employee.HireDate)
                })
                .Top(10)
                .OrderBy(x => x.FirstName, SqlDir.DESC)
                .Execute();
            if (res_agg.State == ResponseState.Valid)
            {
                foreach (var item in res_agg.Result)
                    Console.WriteLine($"({item.Id}) {item.LastName}, {item.FirstName}");
            }
            else
            {
                Console.WriteLine(res_agg.Error.Message);
                Console.WriteLine((res_agg.Error as SqlLinqer.Exceptions.SqlResponseException)?.Query.GetTextWithParameterValues());
            }

            Console.WriteLine("-----------------------------------------------------");

            var valid_emps = _Employee
                .BeginSelect()
                .Select(x => x.Id)
                .Where(x => x.LastName, "Syrzycki", SqlOp.EQ)
                .Where(x => x.Departments.First().Name, "%dev%", SqlOp.LIKE);
            var res_3 = _Employee_Departments
                .BeginSelect()
                .SelectRootColumns()
                .Where(x => x._Employee.Id, valid_emps, SqlArrayOp.ANY)
                .Execute();

            if (res_3.State == ResponseState.Valid)
            {
                foreach (IDisplayable item in res_3.Result)
                    Console.WriteLine(item.ToDisplayableString());
            }
            else
            {
                Console.WriteLine(res_3.Error.Message);
                Console.WriteLine((res_3.Error as SqlLinqer.Exceptions.SqlResponseException)?.Query.GetTextWithParameterValues());
            }

            Console.WriteLine("-----------------------------------------------------");

            ;
        }
    }
}
# What is SqlLinqer?
SqlLinqer makes it much easier to use your sql database within your .net code. Compiled in .net standard 2.0 it can be used with the most common platforms without any extra dependencies. SqlLinqer generates your SQL queries from the class definitions of your already existing models and instead of returning an abstract data table SqlLinqer will return back your object with its properties and fields populated, this includes the properties and fields that are other classes that represent other tables in your database.

All parameters are parameterized to avoid any data conversion issues or SQL injection.

https://www.nuget.org/packages/SqlLinqer/

Support SQL Implementations:
 - Microsoft SQL Server 2012+
 - My SQL
 - PostgreSQL
 - Oracle SQL

# Creating Class Definitions
These class definitions represent your tables and how they are related to each other. These are used by SqlLinqer to create your queries, by you to specify column targets and are returned by select queries populated with the data from the database.

    [SQLTable("users")]
    public class _User : SqlLinqerObjectWithPrimaryKey<_User, string>
    {
        [SQLPrimaryKey]
        [SQLColumn("uuid")]
        public string id;

        public string email;
        public string manager;
        public ulong? status;

        // one to one relationship
        [SQLOneToOne(nameof(manager))]
        public _User _manager;

        [SQLOneToOne(nameof(status))]
        public _Status _status;

        // one to many relationship
        [SQLOneToMany(nameof(_Permission.userId))]
        public _Permission _permission;
    }

    [SQLTable("permissions")]
    public class _Permission : SqlLinqerObject<_Permission>
    {
        [SQLColumn("uuid")]
        public string userId;

        public string name;

        [SQLOneToOne(nameof(userId))]
        public _User _User;
    }

    [SQLTable("status")]
    public class _Status : SqlLinqerObjectWithPrimaryKey<_Status, ulong>
    {
        [SQLPrimaryKey(dbGenerated: true)]
        public ulong id;
        public string name;
    }

# Using SqlLinqer

## Setup the default connector
This isn't required but this way you do not have to define the connector each time.

    var conn = new MySqlConnection(
        "server=127.0.0.1;" +
        "port=3306;" +
        "user id=user;" +
        "password=password;" +
        "database=db;" +
        "SslMode=none;"
    );

    SqlLinqer.Default.Connector = 
        new SqlLinqerConnector(conn, SQLDBType.MYSQL);

## Insert new records

### Code Examples
    _Status status = new()
    {
        name = "active"
    };
    _User user = new()
    {
        id = "user1",
        email = "user1@mail.com",
        manager = "user1"
    };
    _User user2 = new()
    {
        id = "user2",
        email = "user2@mail.com",
        manager = "user1"
    };
    var perms = new[]
    {
        new _Permission() { userId = user.id, name = "admin" },
        new _Permission() { userId = user2.id, name = "general" }
    };

	/*
	 * insert one item. 
	 * Status has a primary key. It's automatically populated after insert, if its successful.
	 */ 
    status.Insert().Run();
    
    user.status = status.id; // the id is now populated
    user.Insert().Run();
    
    _Permission.Insert(perms).Run(); // arrays of objects are inserted in batches to improve performace

## Update records

### Code Examples
    // Update a user's email
    user.email = "user_1@mail.com";
    user.Update().Run();

    // Update non-admin users status to id 0
    _User
        .Update(x => x.status, 0)
        .Where(x => x._permission.name, "%admin%", SQLOp.NOTLIKE)
        .Run();

## Deleting records

### Code Examples
    // Delete the user
    user2.Delete().Run();

    // Delete user id
    _User
        .Delete("user2")
        .Run();

    // Delete non-admin users
    _User
        .DeleteWhere()
        .Where(x => x._permission.name, "%admin%", SQLOp.NOTLIKE)
        .Run();

## Selecting records

### Recursion level
The "recursion level" means "the number of relationship layers to include". Each class can be related to another and each of those classes can be related to other classes or back to the original class. The "recursion level" refers to how many layers of relationships should be included in the selected columns. 
This only applies to selecting columns not using them in where or order by statements.

### Code Examples
    // Select user where id is "user1"
    SQLSelectResponse<_User> response = _User
        .Select("user1")
        .Run();

    /*
     * Select all columns from all users and all columns from the one to one relationships
     * This is the default "recursion level" of 1
     * 
     * A "recursion level of 0" would have only returned the columns from "_User" not its relationships
     */
    SQLSelectResponse<List<_User>> response2 = _User
        .Select()
        .Run();

    /*
     * Select "_User" with a recursion level of 1
     * and select the manager's manager's userId
     * with where & sorting 
     * page 2 , page size 20
     */
    SQLSelectResponse<List<_User>> response4 = _User
        .Select()
        .Select(x => x._Manager._Manager.Id)
        .Where(x => x.email, null, SQLOp.NOT)
        .OrderBy(x => x.Id, SQLDir.ASC)
        .ThenBy(x => x._Manager.Id, SQLDir.ASC)
        .Page(2, 20)
        .Run();
    response4.TotalResults; // this is the total possible results without paging

    // Select top 20 distinct manager IDs
    SQLSelectResponse<List<_User>> response5 = _User
        .Select(x => x._Manager.Id)
        .Distinct()
        .Top(20)
        .Run();

## Query Responses

    public class SQLResponse<long>
    {
	    // the state of the response (Valid or Error)
	    ResponseState State;

		// The number of effected rows
	    long Content;
		
		// The exception
	    Exception Error;
    }
    public class SQLSelectResponse<List<T>> : SQLResponse<List<T>>
    {
	    // The number of total possible results from the query without paging
	    long TotalResults;
    }

## Connect with impersonation

To connect with impersonation you can define your own `SqlLinqerConnector` such as the below example. Your class will override the default execution methods and will be run first allowing your class to create the impersonation context before the connection is opened and the commands executed.

This was important because impersonation is platform specific and this way allows the library to be compatible with .net standard 2.0.

    public class MySqlLinqerConnector : SqlLinqerConnector
    {
        public MySqlLinqerConnector(DbConnection connection, DBType dbType, int parameterLimit = 2100)
            : base(connection, dbType, parameterLimit)
        {

        }

        // all commands execute through this function
        protected sealed override SQLResponse<T> ExecuteCommand<T>(Func<T> action)
        {
            using(<impersonation context>) {
            	return base.ExecuteCommand(action);
		}
        }
    }

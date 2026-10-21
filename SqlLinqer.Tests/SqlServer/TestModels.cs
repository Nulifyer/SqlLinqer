using SqlLinqer;
using SqlLinqer.Modeling;

namespace SqlLinqer.Tests.SqlServer;

[SqlTable("#SqlLinqerAzureParents"), SqlAutoInsert]
public class AzureParent : SqlLinqerPrimaryKeyObject<AzureParent, int>
{
    [SqlPrimaryKey] public int Id { get; set; }
    public string Name { get; set; }
    public bool Enabled { get; set; }
    public int? ProfileId { get; set; }
    [SqlOneToOne(nameof(ProfileId))] public AzureProfile Profile { get; set; }
    [SqlOneToMany(nameof(AzureChild.ParentId))] public List<AzureChild> Children { get; set; }
}
[SqlTable("#SqlLinqerAzureProfiles"), SqlAutoInsert]
public class AzureProfile
{
    [SqlPrimaryKey] public int Id { get; set; }
    public string Name { get; set; }
}
[SqlTable("#SqlLinqerAzureChildren"), SqlAutoInsert]
public class AzureChild
{
    [SqlPrimaryKey, SqlAutoOrderBy(SqlDir.DESC)] public int Id { get; set; }
    public int ParentId { get; set; }
    public string Name { get; set; }
    [SqlOneToMany(nameof(AzureLeaf.ChildId))] public List<AzureLeaf> Leaves { get; set; }
}
[SqlTable("#SqlLinqerAzureLeaves"), SqlAutoInsert]
public class AzureLeaf
{
    [SqlPrimaryKey, SqlAutoOrderBy] public int Id { get; set; }
    public int ChildId { get; set; }
    public string Name { get; set; }
}
[SqlTable("#SqlLinqerAzureTypes"), SqlAutoInsert]
public class AzureTypes
{
    [SqlPrimaryKey] public int Id { get; set; }
    public byte Tiny { get; set; }
    public short Small { get; set; }
    public long Big { get; set; }
    public decimal Amount { get; set; }
    public float Real { get; set; }
    public double Double { get; set; }
    public Guid Token { get; set; }
    public bool Flag { get; set; }
    public int? Optional { get; set; }
    [SqlColumn(nameof(Stamp), System.Data.DbType.DateTime2)] public DateTime Stamp { get; set; }
    public DateTimeOffset OffsetStamp { get; set; }
    public TimeSpan Clock { get; set; }
    public byte[] Bytes { get; set; }
    public string Text { get; set; }
    public char Letter { get; set; }
}
[SqlTable("Rows"), SqlAutoInsert]
public class ScopedRow : SqlLinqerPrimaryKeyObject<ScopedRow, int>
{
    [SqlPrimaryKey] public int Id { get; set; }
    public string Name { get; set; }
    public int? Optional { get; set; }
    public decimal Amount { get; set; }
}
public class ProcedureRow
{
    public int Id { get; set; }
    public string Name { get; set; }
}

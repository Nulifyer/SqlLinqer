using SqlLinqer.Modeling;

namespace SqlLinqer.Tests.Regression;

[SqlTable("stress_rel_roots")]
internal sealed class StressRoot
{
    [SqlPrimaryKey, SqlAutoOrderBy] public int Id { get; set; }
    public string Name { get; set; }
    public int? OwnerId { get; set; }
    public int? BackupId { get; set; }
    [SqlOneToOne(nameof(OwnerId))] public StressPerson Owner { get; set; }
    [SqlOneToOne(nameof(BackupId))] public StressPerson Backup { get; set; }
    [SqlOneToMany(nameof(StressProject.RootId))] public List<StressProject> Projects { get; set; }
    [SqlManyToMany("stress_rel_links", "RootId", "PersonId")] public List<StressPerson> Members { get; set; }
}

[SqlTable("stress_rel_people")]
internal sealed class StressPerson
{
    [SqlPrimaryKey, SqlAutoOrderBy(SqlDir.DESC)] public int Id { get; set; }
    public string Name { get; set; }
    public int? ManagerId { get; set; }
    public int? RegionId { get; set; }
    [SqlOneToOne(nameof(ManagerId))] public StressPerson Manager { get; set; }
    [SqlOneToMany(nameof(ManagerId))] public List<StressPerson> Reports { get; set; }
    [SqlOneToOne(nameof(RegionId))] public StressRegion Region { get; set; }
    [SqlOneToMany(nameof(StressTicket.AssigneeId))] public List<StressTicket> Tickets { get; set; }
}

[SqlTable("stress_rel_projects")]
internal sealed class StressProject
{
    [SqlPrimaryKey, SqlAutoOrderBy(SqlDir.ASC, 1)] public int Id { get; set; }
    public int RootId { get; set; }
    public int? OwnerId { get; set; }
    [SqlAutoOrderBy(SqlDir.DESC, 0)] public int Rank { get; set; }
    [SqlOneToOne(nameof(OwnerId))] public StressPerson Owner { get; set; }
    [SqlOneToOne(nameof(RootId))] public StressRoot Root { get; set; }
    [SqlOneToMany(nameof(StressTicket.ProjectId))] public List<StressTicket> Tickets { get; set; }
}

[SqlTable("stress_rel_tickets")]
internal sealed class StressTicket
{
    [SqlPrimaryKey, SqlAutoOrderBy(SqlDir.DESC, 1)] public int Id { get; set; }
    public int ProjectId { get; set; }
    public int? AssigneeId { get; set; }
    public int? CategoryId { get; set; }
    [SqlAutoOrderBy(SqlDir.ASC, 0)] public int Rank { get; set; }
    [SqlOneToOne(nameof(AssigneeId))] public StressPerson Assignee { get; set; }
    [SqlOneToOne(nameof(CategoryId))] public StressCategory Category { get; set; }
    [SqlOneToOne(nameof(ProjectId))] public StressProject Project { get; set; }
}

[SqlTable("stress_rel_regions")]
internal sealed class StressRegion
{
    [SqlPrimaryKey] public int Id { get; set; }
    public string Name { get; set; }
    public int? ParentId { get; set; }
    [SqlOneToOne(nameof(ParentId))] public StressRegion Parent { get; set; }
}

[SqlTable("stress_rel_categories")]
internal sealed class StressCategory
{
    [SqlPrimaryKey] public int Id { get; set; }
    public string Name { get; set; }
    public int? ParentId { get; set; }
    [SqlOneToOne(nameof(ParentId))] public StressCategory Parent { get; set; }
}

namespace SqlLinqer.Queries.Interfaces
{
    /// <summary>
    /// A typed query. Typed queries contain the main facing and usable functions of the data models.
    /// The functions should be strongly typed at the query level with the root targeted type. 
    /// This strong typing allows the query to be built up relative to the root type and perform
    /// most of the logic automatically for joins and proper handling of the different relationships
    /// to ensure data returned is not duplicated while remaining performant.
    /// </summary>
    public interface IQueryTyped
    {
        
    }
}
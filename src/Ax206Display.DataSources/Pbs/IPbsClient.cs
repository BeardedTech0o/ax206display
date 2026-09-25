namespace Ax206Display.DataSources.Pbs;

public interface IPbsClient
{
    Task LoginAsync(string username, string password, string realm = "pam", CancellationToken cancellationToken = default);

    /// <summary>Every datastore's capacity - basic support only reports capacity, not backup job/snapshot status.</summary>
    Task<IReadOnlyList<PbsDatastoreStatus>> GetDatastoreUsageAsync(CancellationToken cancellationToken = default);
}

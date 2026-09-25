namespace Ax206Display.DataSources.Pbs;

/// <summary>Maps each datastore's usage onto its own render-data key (see <see cref="PbsStatKeys"/>), namespaced by which configured PBS host it came from.</summary>
public static class PbsStatsPublisher
{
    public static void Publish(string hostId, IReadOnlyList<PbsDatastoreStatus> datastores, Action<string, object> publish)
    {
        ArgumentNullException.ThrowIfNull(hostId);
        ArgumentNullException.ThrowIfNull(datastores);
        ArgumentNullException.ThrowIfNull(publish);

        foreach (var datastore in datastores)
        {
            var usedPercent = datastore.TotalBytes > 0
                ? datastore.UsedBytes / (double)datastore.TotalBytes * 100.0
                : 0.0;
            publish(PbsStatKeys.UsedPercent(hostId, datastore.Store), usedPercent);
        }
    }
}

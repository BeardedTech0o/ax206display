namespace Ax206Display.DataSources.Pbs;

/// <summary>
/// Builds the render-data key a datastore's usage is published under,
/// namespaced by host (IntegrationConfig.Id) and store name - same reasoning
/// as ProxmoxGuestKeys/ProxmoxNodeKeys: a store name is only unique within
/// one PBS host, not across several configured hosts.
/// </summary>
public static class PbsStatKeys
{
    public static string UsedPercent(string hostId, string store) => $"pbs.{hostId}.datastore.{store}.used";
}

namespace Ax206Display.DataSources.PiHole;

public interface IPiHoleClient
{
    Task<PiHoleSummary> GetSummaryAsync(CancellationToken cancellationToken = default);
}

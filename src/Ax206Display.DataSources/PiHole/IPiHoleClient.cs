namespace Ax206Display.DataSources.PiHole;

public interface IPiHoleClient
{
    Task LoginAsync(string appPassword, CancellationToken cancellationToken = default);

    Task<PiHoleSummary> GetSummaryAsync(CancellationToken cancellationToken = default);
}

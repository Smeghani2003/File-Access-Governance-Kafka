using FileAccessGovernance.Shared.Models;

namespace FileAccessGovernance.ScanAgent.Kafka;

public interface IObjectRecordProducer : IDisposable
{
    Task PublishAsync(ObjectRecord record, CancellationToken ct);
}

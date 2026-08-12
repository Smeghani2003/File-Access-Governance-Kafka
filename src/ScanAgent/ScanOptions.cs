namespace FileAccessGovernance.ScanAgent;

public sealed class ScanOptions
{
    public string RootPath { get; set; } = default!;   // e.g. \\srv1\share01
    public string ShareName { get; set; } = default!;
    public int DegreeOfParallelism { get; set; } = 4;
}

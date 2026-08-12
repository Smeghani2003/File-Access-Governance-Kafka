namespace FileAccessGovernance.ScanAgent.WorkQueue;

public sealed record DirectoryTask(string FullPath, string? ParentPath, string ShareName);

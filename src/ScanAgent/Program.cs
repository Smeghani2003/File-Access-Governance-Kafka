using FileAccessGovernance.ScanAgent;
using FileAccessGovernance.ScanAgent.Kafka;
using FileAccessGovernance.ScanAgent.Security;
using FileAccessGovernance.ScanAgent.WorkQueue;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(); // no-op off Windows; lets the same binary run as a Windows Service there

builder.Services.Configure<ScanOptions>(builder.Configuration.GetSection("Scan"));
builder.Services.Configure<ScanAgentKafkaOptions>(builder.Configuration.GetSection("Kafka"));

builder.Services.AddSingleton<IDirectoryTaskQueue, InMemoryDirectoryTaskQueue>();
builder.Services.AddSingleton<IObjectRecordProducer, KafkaObjectRecordProducer>();

// Real Win32 reader only works on Windows; local dev on Mac/Linux uses the fake —
// see design doc §7 and ISecurityDescriptorReader's doc comment.
if (OperatingSystem.IsWindows())
{
    builder.Services.AddSingleton<ISecurityDescriptorReader, Win32SecurityDescriptorReader>();
}
else
{
    builder.Services.AddSingleton<ISecurityDescriptorReader, FakeSecurityDescriptorReader>();
}

builder.Services.AddHostedService<ScanWorker>();

var host = builder.Build();
host.Run();

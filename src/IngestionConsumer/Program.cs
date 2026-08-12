using FileAccessGovernance.IngestionConsumer;
using FileAccessGovernance.IngestionConsumer.Kafka;
using FileAccessGovernance.IngestionConsumer.Sql;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<KafkaConsumerOptions>(builder.Configuration.GetSection("Kafka"));

builder.Services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();
builder.Services.AddSingleton<StagingWriter>();
builder.Services.AddSingleton<MergeRunner>();
builder.Services.AddSingleton<IDeadLetterPublisher, KafkaDeadLetterPublisher>();
builder.Services.AddSingleton<IObjectRecordConsumer, KafkaObjectRecordConsumer>();
builder.Services.AddHostedService<ConsumerWorker>();

var host = builder.Build();
host.Run();

using CommunityToolkit.Aspire.Hosting.Dapr;

var builder = DistributedApplication.CreateBuilder(args);
builder.AddDapr();

var postgres = builder.AddPostgres("postgres").WithDataVolume();
var billingDatabase = postgres.AddDatabase("billing", "usage_billing");
var redis = builder.AddRedis("redis", 6379).WithDataVolume();
var kafka = builder.AddKafka("kafka", 9092).WithDataVolume().WithKafkaUI();

var stateStore = builder.AddDaprStateStore("statestore", new DaprComponentOptions
{
    LocalPath = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "dapr", "components", "statestore.yaml"))
});
var pubSub = builder.AddDaprPubSub("pubsub", new DaprComponentOptions
{
    LocalPath = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "dapr", "components", "pubsub.yaml"))
});

var api = builder.AddProject<Projects.UsageBilling_Api>("api")
    .WithReference(billingDatabase)
    .WithReference(redis)
    .WithReference(kafka)
    .WaitFor(billingDatabase)
    .WaitFor(redis)
    .WaitFor(kafka)
    .WithEnvironment("Storage__Provider", "Postgres")
    .WithEnvironment("Storage__ConnectionString", billingDatabase.Resource.ConnectionStringExpression)
    .WithEnvironment("Redis__Enabled", "true")
    .WithEnvironment("Redis__ConnectionString", redis.Resource.ConnectionStringExpression)
    .WithEnvironment("Messaging__Provider", "Kafka")
    .WithEnvironment("Messaging__BootstrapServers", kafka.Resource.ConnectionStringExpression)
    .WithDaprSidecar(sidecar => sidecar.WithReference(stateStore).WithReference(pubSub));

builder.AddProject<Projects.UsageBilling_BillingWorker>("billing-worker")
    .WithReference(billingDatabase)
    .WaitFor(billingDatabase)
    .WithEnvironment("Storage__Provider", "Postgres")
    .WithEnvironment("Storage__ConnectionString", billingDatabase.Resource.ConnectionStringExpression)
    .WithDaprSidecar(sidecar => sidecar.WithReference(stateStore).WithReference(pubSub));

builder.AddJavaScriptApp("web", "../../frontend", "start")
    .WithPnpm()
    .WithReference(api)
    .WaitFor(api)
    .WithHttpEndpoint(4200, 4200, "http")
    .WithExternalHttpEndpoints();

builder.Build().Run();
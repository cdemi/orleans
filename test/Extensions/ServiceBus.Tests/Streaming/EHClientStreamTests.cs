using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Orleans.Configuration;
using Orleans.Streaming.EventHubs;
using Orleans.Streams;
using Orleans.TestingHost;
using ServiceBus.Tests.TestStreamProviders.EventHub;
using Tester;
using Tester.StreamingTests;
using TestExtensions;
using Xunit;
using Xunit.Abstractions;

namespace ServiceBus.Tests.StreamingTests
{
    /// <summary>
    /// Tests for EventHub streaming functionality with client producer/consumer scenarios and dropped client handling.
    /// </summary>
    [TestCategory("EventHub"), TestCategory("Streaming"), TestCategory("Functional")]
    public class EHClientStreamTests : TestClusterPerTest
    {
        private const string StreamProviderName = "EventHubStreamProvider";
        private const string StreamNamespace = "StreamNamespace";
        private const string EHPath = "ehorleanstest";
        private const string EHConsumerGroup = "orleansnightly";

        private readonly ITestOutputHelper output;
        private ClientStreamTestRunner runner;
        public EHClientStreamTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            runner = new ClientStreamTestRunner(this.HostedCluster);
        }

        protected override void ConfigureTestCluster(TestClusterBuilder builder)
        {
            TestUtils.CheckForEventHub();
            builder.AddSiloBuilderConfigurator<MySiloBuilderConfigurator>();
            builder.AddClientBuilderConfigurator<MyClientBuilderConfigurator>();
        }

        private class MySiloBuilderConfigurator : ISiloConfigurator
        {
            public void Configure(ISiloBuilder hostBuilder)
            {
                hostBuilder
                    .AddPersistentStreams(StreamProviderName, TestEventHubStreamAdapterFactory.Create, b=>
                    {
                        b.Configure<SiloMessagingOptions>(ob => ob.Configure(options => options.ClientDropTimeout = TimeSpan.FromSeconds(5)));
                        b.Configure<EventHubOptions>(ob => ob.Configure(options =>
                        {
                            options.ConfigureTestDefaults(EHPath, EHConsumerGroup);
                        }));
                        b.ConfigureComponent<AzureTableStreamCheckpointerOptions, IStreamQueueCheckpointerFactory>(
                            EventHubCheckpointerFactory.CreateFactory,
                            ob => ob.Configure(options =>
                            {
                                options.ConfigureTestDefaults();
                                options.PersistInterval = TimeSpan.FromSeconds(10);
                            }));
                    })
                    .AddMemoryGrainStorage("PubSubStore")
                    .ConfigureServices(services => services.TryAddSingleton<IEventHubDataAdapter, EventHubDataAdapter>());
            }
        }

        private class MyClientBuilderConfigurator : IClientBuilderConfigurator
        {
            public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
            {
                clientBuilder
                    .AddPersistentStreams(StreamProviderName, TestEventHubStreamAdapterFactory.Create, b=>b
                        .Configure<EventHubOptions>(ob=>ob.Configure(options =>
                        {
                            options.ConfigureTestDefaults(EHPath, EHConsumerGroup);
                        })))
                    .ConfigureServices(services => services.TryAddSingleton<IEventHubDataAdapter, EventHubDataAdapter>());
            }
        }

        [SkippableFact(Skip = "https://github.com/dotnet/orleans/issues/5657")]
        public async Task EHStreamProducerOnDroppedClientTest()
        {
            logger.LogInformation("************************ EHStreamProducerOnDroppedClientTest *********************************");
            await runner.StreamProducerOnDroppedClientTest(StreamProviderName, StreamNamespace);
        }

        [SkippableFact(Skip = "https://github.com/dotnet/orleans/issues/5634")]
        public async Task EHStreamConsumerOnDroppedClientTest()
        {
            logger.LogInformation("************************ EHStreamConsumerOnDroppedClientTest *********************************");
            await runner.StreamConsumerOnDroppedClientTest(StreamProviderName, StreamNamespace, output,
                    () => TestAzureTableStorageStreamFailureHandler.GetDeliveryFailureCount(StreamProviderName), true);
        }
    }
}

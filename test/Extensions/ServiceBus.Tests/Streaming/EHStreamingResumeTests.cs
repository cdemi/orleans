using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Streaming.EventHubs;
using Orleans.TestingHost;
using Tester;
using Tester.StreamingTests;
using TestExtensions;

namespace ServiceBus.Tests.Streaming
{
    /// <summary>
    /// Tests for EventHub streaming resume functionality with configurable cache eviction and stream inactivity settings.
    /// </summary>
    [TestCategory("Functional"), TestCategory("Streaming"), TestCategory("StreamingResume")]
    public class EHStreamingResumeTests : StreamingResumeTests
    {
        private const string EHPath = "ehorleanstest";
        private const string EHConsumerGroup = "orleansnightly";

        private class MySiloBuilderConfigurator : ISiloConfigurator
        {
            public void Configure(ISiloBuilder hostBuilder)
            {
                hostBuilder
                    .AddMemoryGrainStorage("PubSubStore")
                    .AddMemoryGrainStorageAsDefault()
                    .AddEventHubStreams(StreamProviderName, b =>
                    {
                        b.ConfigurePullingAgent(ob => ob.Configure(options =>
                        {
                            options.StreamInactivityPeriod = StreamInactivityPeriod;
                        }));
                        b.ConfigureCacheEviction(ob => ob.Configure(options =>
                        {
                            options.MetadataMinTimeInCache = MetadataMinTimeInCache;
                            options.DataMaxAgeInCache = DataMaxAgeInCache;
                            options.DataMinTimeInCache = DataMinTimeInCache;
                        }));
                        b.ConfigureEventHub(ob => ob.Configure(options =>
                        {
                            options.ConfigureTestDefaults(EHPath, EHConsumerGroup);
                        }));
                        b.UseAzureTableCheckpointer(ob => ob.Configure(options =>
                        {
                            options.ConfigureTestDefaults();
                            options.PersistInterval = TimeSpan.FromSeconds(10);
                        }));
                        b.UseDataAdapter((sp, n) => ActivatorUtilities.CreateInstance<EventHubDataAdapter>(sp));
                    });
            }
        }

        private class MyClientBuilderConfigurator : IClientBuilderConfigurator
        {
            public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
            {
                clientBuilder
                    .AddEventHubStreams(StreamProviderName, b =>
                    {
                        b.ConfigureEventHub(ob => ob.Configure(options =>
                        {
                            options.ConfigureTestDefaults(EHPath, EHConsumerGroup);
                        }));
                    });
            }
        }

        protected override void CheckPreconditionsOrThrow() => TestUtils.CheckForEventHub();

        protected override void ConfigureTestCluster(TestClusterBuilder builder)
        {
            TestUtils.CheckForEventHub();
            builder.AddSiloBuilderConfigurator<MySiloBuilderConfigurator>();
            builder.AddClientBuilderConfigurator<MyClientBuilderConfigurator>();
        }
    }
}

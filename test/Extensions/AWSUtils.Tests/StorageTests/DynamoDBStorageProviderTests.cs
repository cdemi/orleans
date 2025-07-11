using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Providers;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Storage;
using TestExtensions;
using UnitTests.Persistence;
using Xunit;
using Xunit.Abstractions;
using static Orleans.Storage.DynamoDBGrainStorage;

namespace AWSUtils.Tests.StorageTests
{
    /// <summary>
    /// Tests DynamoDB storage provider functionality including read/write operations and format conversions.
    /// </summary>
    [TestCategory("Persistence"), TestCategory("AWS"), TestCategory("DynamoDb")]
    [Collection(TestEnvironmentFixture.DefaultCollection)]
    public class DynamoDBStorageProviderTests
    {
        private readonly IProviderRuntime providerRuntime;
        private readonly ITestOutputHelper output;
        private readonly Dictionary<string, string> providerCfgProps = new Dictionary<string, string>();
        private readonly TestEnvironmentFixture fixture;

        public DynamoDBStorageProviderTests(ITestOutputHelper output, TestEnvironmentFixture fixture)
        {
            this.output = output;
            this.fixture = fixture;
            providerCfgProps["DataConnectionString"] = $"Service={AWSTestConstants.DynamoDbService}";
            this.providerRuntime = new ClientProviderRuntime(
                fixture.InternalGrainFactory,
                fixture.Services,
                fixture.Services.GetRequiredService<ClientGrainContext>());
        }

        [SkippableTheory, TestCategory("Functional")]
        [InlineData(null, false)]
        [InlineData(null, true)]
        [InlineData(400_000, false)]
        [InlineData(400_000, true)]
        public async Task PersistenceProvider_DynamoDB_WriteRead(int? stringLength, bool useJson)
        {
            var testName = string.Format("{0}({1} = {2}, {3} = {4})",
                nameof(PersistenceProvider_DynamoDB_WriteRead),
                nameof(stringLength), stringLength == null ? "default" : stringLength.ToString(),
                nameof(useJson), useJson);

            var grainState = TestStoreGrainState.NewRandomState(stringLength);
            EnsureEnvironmentSupportsState(grainState);

            var store = await InitDynamoDBGrainStorage(useJson);

            await Test_PersistenceProvider_WriteRead(testName, store, grainState);
        }

        [SkippableTheory, TestCategory("Functional")]
        [InlineData(null, false, false)]
        [InlineData(null, true, false)]
        [InlineData(400_000, false, false)]
        [InlineData(400_000, true, false)]
        public async Task PersistenceProvider_DynamoDB_WriteClearRead(int? stringLength, bool useJson, bool useFallback)
        {
            var testName = string.Format("{0}({1} = {2}, {3} = {4}, {5} = {6})",
                nameof(PersistenceProvider_DynamoDB_WriteClearRead),
                nameof(stringLength), stringLength == null ? "default" : stringLength.ToString(),
                nameof(useJson), useJson,
                nameof(useFallback), useFallback);

            var grainState = TestStoreGrainState.NewRandomState(stringLength);
            EnsureEnvironmentSupportsState(grainState);

            var store = await InitDynamoDBGrainStorage(useJson, useFallback);

            await Test_PersistenceProvider_WriteClearRead(testName, store, grainState);
        }

        [SkippableTheory, TestCategory("Functional")]
        [InlineData(null, true, false)]
        [InlineData(null, false, true)]
        [InlineData(400_000, true, false)]
        [InlineData(400_000, false, true)]
        public async Task PersistenceProvider_DynamoDB_ChangeReadFormat(int? stringLength, bool useJsonForWrite, bool useJsonForRead)
        {
            var testName = string.Format("{0}({1} = {2}, {3} = {4}, {5} = {6})",
                nameof(PersistenceProvider_DynamoDB_ChangeReadFormat),
                nameof(stringLength), stringLength == null ? "default" : stringLength.ToString(),
                nameof(useJsonForWrite), useJsonForWrite,
                nameof(useJsonForRead), useJsonForRead);

            var grainState = TestStoreGrainState.NewRandomState(stringLength);
            EnsureEnvironmentSupportsState(grainState);
            var grainId = GrainId.Create("test", Guid.NewGuid().ToString("N"));

            var store = await InitDynamoDBGrainStorage(useJsonForWrite);

            grainState = await Test_PersistenceProvider_WriteRead(testName, store,
                grainState, grainId);

            store = await InitDynamoDBGrainStorage(useJsonForRead);

            await Test_PersistenceProvider_Read(testName, store, grainState, grainId);
        }

        [SkippableTheory, TestCategory("Functional")]
        [InlineData(null, true, false)]
        [InlineData(null, false, true)]
        [InlineData(100_000, true, false)]
        [InlineData(100_000, false, true)]
        public async Task PersistenceProvider_DynamoDB_ChangeWriteFormat(int? stringLength, bool useJsonForFirstWrite, bool useJsonForSecondWrite)
        {
            var testName = string.Format("{0}({1}={2},{3}={4},{5}={6})",
                nameof(PersistenceProvider_DynamoDB_ChangeWriteFormat),
                nameof(stringLength), stringLength == null ? "default" : stringLength.ToString(),
                "json1stW", useJsonForFirstWrite,
                "json2ndW", useJsonForSecondWrite);

            var grainState = TestStoreGrainState.NewRandomState(stringLength);
            EnsureEnvironmentSupportsState(grainState);

            var grainId = GrainId.Create("test", Guid.NewGuid().ToString("N"));

            var store = await InitDynamoDBGrainStorage(useJsonForFirstWrite);

            await Test_PersistenceProvider_WriteRead(testName, store, grainState, grainId);

            grainState = TestStoreGrainState.NewRandomState(stringLength);
            grainState.ETag = "*";

            store = await InitDynamoDBGrainStorage(useJsonForSecondWrite);

            await Test_PersistenceProvider_WriteRead(testName, store, grainState, grainId);
        }

        [SkippableTheory, TestCategory("Functional")]
        [InlineData(null, false)]
        [InlineData(null, true)]
        [InlineData(400_000, false)]
        [InlineData(400_000, true)]
        public async Task DynamoDBStorage_ConvertToFromStorageFormat(int? stringLength, bool useJson)
        {
            var testName = string.Format("{0}({1} = {2}, {3} = {4})",
               nameof(DynamoDBStorage_ConvertToFromStorageFormat),
               nameof(stringLength), stringLength == null ? "default" : stringLength.ToString(),
               nameof(useJson), useJson);

            var state = TestStoreGrainState.NewRandomState(stringLength);
            EnsureEnvironmentSupportsState(state);

            var storage = await InitDynamoDBGrainStorage(useJson);
            var initialState = state.State;

            var entity = new GrainStateRecord();

            storage.ConvertToStorageFormat(initialState, entity);

            var convertedState = storage.ConvertFromStorageFormat<TestStoreGrainState>(entity);
            Assert.NotNull(convertedState);
            Assert.Equal(initialState.A, convertedState.A);
            Assert.Equal(initialState.B, convertedState.B);
            Assert.Equal(initialState.C, convertedState.C);
        }

        private async Task<DynamoDBGrainStorage> InitDynamoDBGrainStorage(DynamoDBStorageOptions options)
        {
            DynamoDBGrainStorage store = ActivatorUtilities.CreateInstance<DynamoDBGrainStorage>(this.providerRuntime.ServiceProvider, "StorageProviderTests", options);
            ISiloLifecycleSubject lifecycle = ActivatorUtilities.CreateInstance<SiloLifecycleSubject>(this.providerRuntime.ServiceProvider, NullLogger<SiloLifecycleSubject>.Instance);
            store.Participate(lifecycle);
            await lifecycle.OnStart();
            return store;
        }

        private Task<DynamoDBGrainStorage> InitDynamoDBGrainStorage(bool useJson = false, bool useFallback = true)
        {
            var options = new DynamoDBStorageOptions
            {
                Service = AWSTestConstants.DynamoDbService,
            };

            var jsonOptions = this.providerRuntime.ServiceProvider.GetService<IOptions<OrleansJsonSerializerOptions>>();
            var binarySerializer = new OrleansGrainStorageSerializer(this.providerRuntime.ServiceProvider.GetRequiredService<Serializer>());
            var jsonSerializer = new JsonGrainStorageSerializer(new OrleansJsonSerializer(jsonOptions));

            if (useFallback)
                options.GrainStorageSerializer = useJson
                    ? new GrainStorageSerializer(jsonSerializer, binarySerializer)
                    : new GrainStorageSerializer(binarySerializer, jsonSerializer);
            else
                options.GrainStorageSerializer = useJson ? jsonSerializer : binarySerializer;

            return InitDynamoDBGrainStorage(options);
        }

        private async Task Test_PersistenceProvider_Read(string grainTypeName, IGrainStorage store,
            GrainState<TestStoreGrainState> grainState = null, GrainId grainId = default)
        {
            var reference = grainId.IsDefault ? GrainId.Create("test", Guid.NewGuid().ToString("N")) : grainId;

            if (grainState == null)
            {
                grainState = new GrainState<TestStoreGrainState>(new TestStoreGrainState());
            }
            var storedGrainState = new GrainState<TestStoreGrainState>(new TestStoreGrainState());

            Stopwatch sw = new Stopwatch();
            sw.Start();

            await store.ReadStateAsync(grainTypeName, reference, storedGrainState);

            TimeSpan readTime = sw.Elapsed;
            this.output.WriteLine("{0} - Read time = {1}", store.GetType().FullName, readTime);

            var storedState = storedGrainState.State;
            Assert.Equal(grainState.State.A, storedState.A);
            Assert.Equal(grainState.State.B, storedState.B);
            Assert.Equal(grainState.State.C, storedState.C);
        }

        private async Task<GrainState<TestStoreGrainState>> Test_PersistenceProvider_WriteRead(string grainTypeName,
            IGrainStorage store, GrainState<TestStoreGrainState> grainState = null, GrainId grainId = default)
        {
            var reference = grainId.IsDefault ? GrainId.Create("test", Guid.NewGuid().ToString("N")) : grainId;

            if (grainState == null)
            {
                grainState = TestStoreGrainState.NewRandomState();
            }

            Stopwatch sw = new Stopwatch();
            sw.Start();

            await store.WriteStateAsync(grainTypeName, reference, grainState);

            TimeSpan writeTime = sw.Elapsed;
            sw.Restart();

            var storedGrainState = new GrainState<TestStoreGrainState>
            {
                State = new TestStoreGrainState()
            };
            await store.ReadStateAsync(grainTypeName, reference, storedGrainState);
            TimeSpan readTime = sw.Elapsed;
            this.output.WriteLine("{0} - Write time = {1} Read time = {2}", store.GetType().FullName, writeTime, readTime);
            Assert.Equal(grainState.State.A, storedGrainState.State.A);
            Assert.Equal(grainState.State.B, storedGrainState.State.B);
            Assert.Equal(grainState.State.C, storedGrainState.State.C);

            return storedGrainState;
        }

        private async Task<GrainState<TestStoreGrainState>> Test_PersistenceProvider_WriteClearRead(string grainTypeName,
            IGrainStorage store, GrainState<TestStoreGrainState> grainState = null, GrainId grainId = default)
        {
            var reference = grainId.IsDefault ? GrainId.Create("test", Guid.NewGuid().ToString("N")) : grainId;

            if (grainState == null)
            {
                grainState = TestStoreGrainState.NewRandomState();
            }

            Stopwatch sw = new Stopwatch();
            sw.Start();

            await store.WriteStateAsync(grainTypeName, reference, grainState);

            TimeSpan writeTime = sw.Elapsed;
            sw.Restart();

            await store.ClearStateAsync(grainTypeName, reference, grainState);

            var storedGrainState = new GrainState<TestStoreGrainState>
            {
                State = new TestStoreGrainState()
            };
            await store.ReadStateAsync(grainTypeName, reference, storedGrainState);
            TimeSpan readTime = sw.Elapsed;
            this.output.WriteLine("{0} - Write time = {1} Read time = {2}", store.GetType().FullName, writeTime, readTime);
            Assert.NotNull(storedGrainState.State);
            Assert.Equal(default, storedGrainState.State.A);
            Assert.Equal(default, storedGrainState.State.B);
            Assert.Equal(default, storedGrainState.State.C);

            return storedGrainState;
        }

        private static void EnsureEnvironmentSupportsState(GrainState<TestStoreGrainState> grainState)
        {
            if (!AWSTestConstants.IsDynamoDbAvailable)
                throw new SkipException("Unable to connect to DynamoDB simulator");
        }
    }
}

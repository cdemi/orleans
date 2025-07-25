#nullable enable
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Configuration;
using Orleans.Configuration.Validators;
using Orleans.Runtime.Configuration;
using Orleans.Runtime.ConsistentRing;
using Orleans.Runtime.GrainDirectory;
using Orleans.Runtime.MembershipService;
using Orleans.Metadata;
using Orleans.Runtime.Messaging;
using Orleans.Runtime.Placement;
using Orleans.Runtime.Providers;
using Orleans.Runtime.Versions;
using Orleans.Runtime.Versions.Compatibility;
using Orleans.Runtime.Versions.Selector;
using Orleans.Serialization;
using Orleans.Statistics;
using Orleans.Timers;
using Orleans.Versions;
using Orleans.Versions.Compatibility;
using Orleans.Versions.Selector;
using Orleans.Providers;
using Orleans.Runtime;
using Microsoft.Extensions.Logging;
using Orleans.Runtime.Utilities;
using System;
using System.Reflection;
using System.Linq;
using Microsoft.Extensions.Options;
using Orleans.Timers.Internal;
using Microsoft.AspNetCore.Connections;
using Orleans.Networking.Shared;
using Orleans.Configuration.Internal;
using Orleans.Runtime.Metadata;
using Orleans.GrainReferences;
using Orleans.Storage;
using Orleans.Serialization.TypeSystem;
using Orleans.Serialization.Serializers;
using Orleans.Serialization.Cloning;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Orleans.Serialization.Internal;
using Orleans.Core;
using Orleans.Placement.Repartitioning;
using Orleans.Runtime.Placement.Filtering;

namespace Orleans.Hosting
{
    internal static class DefaultSiloServices
    {
        private static readonly ServiceDescriptor ServiceDescriptor = new(typeof(ServicesAdded), new ServicesAdded());

        internal static void AddDefaultServices(ISiloBuilder builder)
        {
            var services = builder.Services;
            if (services.Contains(ServiceDescriptor))
            {
                return;
            }

            services.Add(ServiceDescriptor);

            // Common services
            services.AddLogging();
            services.AddOptions();
            services.TryAddSingleton<TimeProvider>(TimeProvider.System);

            services.TryAddSingleton(typeof(IOptionFormatter<>), typeof(DefaultOptionsFormatter<>));
            services.TryAddSingleton(typeof(IOptionFormatterResolver<>), typeof(DefaultOptionsFormatterResolver<>));

            services.AddSingleton<Silo>();
            services.AddSingleton<Watchdog>();
            services.AddHostedService<SiloHostedService>();
            services.PostConfigure<SiloOptions>(options => options.SiloName ??= $"Silo_{Guid.NewGuid().ToString("N")[..5]}");
            services.TryAddSingleton<ILocalSiloDetails, LocalSiloDetails>();
            services.TryAddSingleton<SiloLifecycleSubject>();
            services.TryAddFromExisting<ISiloLifecycleSubject, SiloLifecycleSubject>();
            services.TryAddFromExisting<ISiloLifecycle, SiloLifecycleSubject>();
            services.AddSingleton<SiloOptionsLogger>();
            services.AddFromExisting<ILifecycleParticipant<ISiloLifecycle>, SiloOptionsLogger>();
            services.AddSingleton<SiloControl>();
            services.AddFromExisting<ILifecycleParticipant<ISiloLifecycle>, SiloControl>();

            // Statistics
            services.AddSingleton<IEnvironmentStatisticsProvider, EnvironmentStatisticsProvider>();
#pragma warning disable 618
            services.AddSingleton<OldEnvironmentStatistics>();
            services.AddFromExisting<IAppEnvironmentStatistics, OldEnvironmentStatistics>();
            services.AddFromExisting<IHostEnvironmentStatistics, OldEnvironmentStatistics>();
#pragma warning restore 618

            services.TryAddSingleton<OverloadDetector>();

            services.AddSingleton<SystemTargetShared>();
            services.AddSingleton<LifecycleSchedulingSystemTarget>();

            services.TryAddSingleton<ITimerRegistry, TimerRegistry>();

            services.TryAddSingleton<GrainRuntime>();
            services.TryAddSingleton<IGrainRuntime, GrainRuntime>();
            services.TryAddSingleton<IGrainCancellationTokenRuntime, GrainCancellationTokenRuntime>();
            services.AddTransient<CancellationSourcesExtension>();
            services.AddKeyedTransient<IGrainExtension>(typeof(ICancellationSourcesExtension), (sp, _) => sp.GetRequiredService<CancellationSourcesExtension>());
            services.TryAddSingleton<GrainFactory>(sp => sp.GetRequiredService<InsideRuntimeClient>().ConcreteGrainFactory);
            services.TryAddSingleton<InterfaceToImplementationMappingCache>();
            services.TryAddSingleton<GrainInterfaceTypeToGrainTypeResolver>();
            services.TryAddFromExisting<IGrainFactory, GrainFactory>();
            services.TryAddFromExisting<IInternalGrainFactory, GrainFactory>();
            services.TryAddSingleton<IGrainReferenceRuntime, GrainReferenceRuntime>();
            services.TryAddSingleton<GrainReferenceActivator>();
            services.AddSingleton<IGrainReferenceActivatorProvider, GrainReferenceActivatorProvider>();
            services.AddSingleton<IGrainReferenceActivatorProvider, UntypedGrainReferenceActivatorProvider>();
            services.AddSingleton<IConfigureGrainContextProvider, MayInterleaveConfiguratorProvider>();
            services.AddSingleton<IConfigureGrainTypeComponents, ReentrantSharedComponentsConfigurator>();
            services.TryAddSingleton<RpcProvider>();
            services.AddSingleton<GrainVersionManifest>();
            services.TryAddSingleton<GrainBindingsResolver>();
            services.TryAddSingleton<GrainTypeSharedContextResolver>();
            services.TryAddSingleton<ActivationDirectory>();
            services.TryAddSingleton<GrainCountStatistics>();
            services.AddSingleton<ActivationCollector>();
            services.AddFromExisting<IActivationWorkingSetObserver, ActivationCollector>();
            services.AddFromExisting<ILifecycleParticipant<ISiloLifecycle>, ActivationCollector>();

            services.AddSingleton<ActivationWorkingSet>();
            services.AddFromExisting<IActivationWorkingSet, ActivationWorkingSet>();
            services.AddFromExisting<ILifecycleParticipant<ISiloLifecycle>, ActivationWorkingSet>();

            // Directory
            services.TryAddSingleton<LocalGrainDirectory>();
            services.TryAddFromExisting<ILocalGrainDirectory, LocalGrainDirectory>();
            services.AddFromExisting<ILifecycleParticipant<ISiloLifecycle>, LocalGrainDirectory>();
            services.AddSingleton<GrainLocator>();
            services.AddSingleton<GrainLocatorResolver>();
            services.AddSingleton<DhtGrainLocator>(sp => DhtGrainLocator.FromLocalGrainDirectory(sp.GetService<LocalGrainDirectory>()));
            services.AddSingleton<GrainDirectoryResolver>();
            services.AddSingleton<IGrainDirectoryResolver, GenericGrainDirectoryResolver>();
            services.AddSingleton<CachedGrainLocator>();
            services.AddFromExisting<ILifecycleParticipant<ISiloLifecycle>, CachedGrainLocator>();
            services.AddSingleton<ClientGrainLocator>();

            services.TryAddSingleton<MessageCenter>();
            services.TryAddFromExisting<IMessageCenter, MessageCenter>();
            services.TryAddSingleton(FactoryUtility.Create<MessageCenter, Gateway>);
            services.TryAddSingleton<IConnectedClientCollection>(sp => sp.GetRequiredService<MessageCenter>().Gateway as IConnectedClientCollection ?? new EmptyConnectedClientCollection());
            services.TryAddSingleton<InternalGrainRuntime>();
            services.TryAddSingleton<InsideRuntimeClient>();
            services.TryAddFromExisting<IRuntimeClient, InsideRuntimeClient>();
            services.AddFromExisting<ILifecycleParticipant<ISiloLifecycle>, InsideRuntimeClient>();
            services.TryAddSingleton<IGrainServiceFactory, GrainServiceFactory>();

            services.TryAddSingleton<IFatalErrorHandler, FatalErrorHandler>();

            services.AddSingleton<DeploymentLoadPublisher>();
            services.AddFromExisting<ILifecycleParticipant<ISiloLifecycle>, DeploymentLoadPublisher>();

            services.AddSingleton<IAsyncTimerFactory, AsyncTimerFactory>();
            services.AddSingleton<MembershipTableManager>();
            services.AddFromExisting<IHealthCheckParticipant, MembershipTableManager>();
            services.AddFromExisting<ILifecycleParticipant<ISiloLifecycle>, MembershipTableManager>();
            services.AddSingleton<MembershipSystemTarget>();
            services.AddFromExisting<IMembershipService, MembershipSystemTarget>();
            services.AddFromExisting<ILifecycleParticipant<ISiloLifecycle>, MembershipSystemTarget>();
            services.AddSingleton<IMembershipGossiper, MembershipGossiper>();
            services.AddSingleton<IRemoteSiloProber, RemoteSiloProber>();
            services.AddSingleton<SiloStatusOracle>();
            services.TryAddFromExisting<ISiloStatusOracle, SiloStatusOracle>();
            services.AddSingleton<ClusterHealthMonitor>();
            services.AddFromExisting<ILifecycleParticipant<ISiloLifecycle>, ClusterHealthMonitor>();
            services.AddFromExisting<IHealthCheckParticipant, ClusterHealthMonitor>();
            services.AddSingleton<ProbeRequestMonitor>();
            services.AddSingleton<LocalSiloHealthMonitor>();
            services.AddFromExisting<ILocalSiloHealthMonitor, LocalSiloHealthMonitor>();
            services.AddFromExisting<ILifecycleParticipant<ISiloLifecycle>, LocalSiloHealthMonitor>();
            services.AddSingleton<MembershipAgent>();
            services.AddFromExisting<ILifecycleParticipant<ISiloLifecycle>, MembershipAgent>();
            services.AddFromExisting<IHealthCheckParticipant, MembershipAgent>();
            services.AddSingleton<MembershipTableCleanupAgent>();
            services.AddFromExisting<ILifecycleParticipant<ISiloLifecycle>, MembershipTableCleanupAgent>();
            services.AddFromExisting<IHealthCheckParticipant, MembershipTableCleanupAgent>();
            services.AddSingleton<SiloStatusListenerManager>();
            services.AddFromExisting<ILifecycleParticipant<ISiloLifecycle>, SiloStatusListenerManager>();
            services.AddSingleton<ClusterMembershipService>();
            services.TryAddFromExisting<IClusterMembershipService, ClusterMembershipService>();
            services.AddFromExisting<ILifecycleParticipant<ISiloLifecycle>, ClusterMembershipService>();

            services.TryAddSingleton<ClientDirectory>();
            services.AddFromExisting<ILocalClientDirectory, ClientDirectory>();
            services.AddFromExisting<ILifecycleParticipant<ISiloLifecycle>, ClientDirectory>();

            services.TryAddSingleton<SiloProviderRuntime>();
            services.TryAddFromExisting<IProviderRuntime, SiloProviderRuntime>();

            services.TryAddSingleton<MessageFactory>();

            services.TryAddSingleton(FactoryUtility.Create<LocalGrainDirectoryPartition>);

            // Placement
            services.AddSingleton<IConfigurationValidator, ActivationCountBasedPlacementOptionsValidator>();
            services.AddSingleton<IConfigurationValidator, ResourceOptimizedPlacementOptionsValidator>();
            services.AddSingleton<PlacementService>();
            services.AddSingleton<PlacementStrategyResolver>();
            services.AddSingleton<PlacementDirectorResolver>();
            services.AddSingleton<IPlacementStrategyResolver, ClientObserverPlacementStrategyResolver>();

            // Configure the default placement strategy.
            services.TryAddSingleton<PlacementStrategy, ResourceOptimizedPlacement>();

            // Placement filters
            services.AddSingleton<PlacementFilterStrategyResolver>();
            services.AddSingleton<PlacementFilterDirectorResolver>();

            // Placement directors
            services.AddPlacementDirector<RandomPlacement, RandomPlacementDirector>();
            services.AddPlacementDirector<PreferLocalPlacement, PreferLocalPlacementDirector>();
            services.AddPlacementDirector<StatelessWorkerPlacement, StatelessWorkerDirector>(ServiceLifetime.Transient);
            services.AddPlacementDirector<ActivationCountBasedPlacement, ActivationCountPlacementDirector>();
            services.AddPlacementDirector<HashBasedPlacement, HashBasedPlacementDirector>();
            services.AddPlacementDirector<ClientObserversPlacement, ClientObserversPlacementDirector>();
            services.AddPlacementDirector<SiloRoleBasedPlacement, SiloRoleBasedPlacementDirector>();
            services.AddPlacementDirector<ResourceOptimizedPlacement, ResourceOptimizedPlacementDirector>();

            // Versioning
            services.TryAddSingleton<VersionSelectorManager>();
            services.TryAddSingleton<CachedVersionSelectorManager>();
            // Version selector strategy
            if (!services.Any(x => x.ServiceType == typeof(IVersionStore)))
            {
                services.AddSingleton<GrainVersionStore>();
                services.AddFromExisting<IVersionStore, GrainVersionStore>();
            }
            services.AddFromExisting<ILifecycleParticipant<ISiloLifecycle>, GrainVersionStore>();
            services.AddKeyedSingleton<VersionSelectorStrategy, AllCompatibleVersions>(nameof(AllCompatibleVersions));
            services.AddKeyedSingleton<VersionSelectorStrategy, LatestVersion>(nameof(LatestVersion));
            services.AddKeyedSingleton<VersionSelectorStrategy, MinimumVersion>(nameof(MinimumVersion));
            // Versions selectors
            services.AddKeyedSingleton<IVersionSelector, MinimumVersionSelector>(typeof(MinimumVersion));
            services.AddKeyedSingleton<IVersionSelector, LatestVersionSelector>(typeof(LatestVersion));
            services.AddKeyedSingleton<IVersionSelector, AllCompatibleVersionsSelector>(typeof(AllCompatibleVersions));

            // Compatibility
            services.AddSingleton<CompatibilityDirectorManager>();
            // Compatability strategy
            services.AddKeyedSingleton<CompatibilityStrategy, AllVersionsCompatible>(nameof(AllVersionsCompatible));
            services.AddKeyedSingleton<CompatibilityStrategy, BackwardCompatible>(nameof(BackwardCompatible));
            services.AddKeyedSingleton<CompatibilityStrategy, StrictVersionCompatible>(nameof(StrictVersionCompatible));
            // Compatability directors
            services.AddKeyedSingleton<ICompatibilityDirector, BackwardCompatilityDirector>(typeof(BackwardCompatible));
            services.AddKeyedSingleton<ICompatibilityDirector, AllVersionsCompatibilityDirector>(typeof(AllVersionsCompatible));
            services.AddKeyedSingleton<ICompatibilityDirector, StrictVersionCompatibilityDirector>(typeof(StrictVersionCompatible));

            services.TryAddSingleton<Factory<IGrainRuntime>>(sp => () => sp.GetRequiredService<IGrainRuntime>());

            // Grain activation
            services.AddSingleton<PlacementService>();
            services.AddSingleton<Catalog>();
            services.AddFromExisting<ILifecycleParticipant<ISiloLifecycle>, Catalog>();
            services.AddSingleton<GrainContextActivator>();
            services.AddSingleton<IConfigureGrainTypeComponents, ConfigureDefaultGrainActivator>();
            services.AddSingleton<GrainReferenceActivator>();
            services.AddSingleton<IGrainContextActivatorProvider, ActivationDataActivatorProvider>();
            services.AddSingleton<IGrainContextAccessor, GrainContextAccessor>();
            services.AddSingleton<IncomingRequestMonitor>();
            services.AddFromExisting<ILifecycleParticipant<ISiloLifecycle>, IncomingRequestMonitor>();
            services.AddFromExisting<IActivationWorkingSetObserver, IncomingRequestMonitor>();

            // Scoped to a grain activation
            services.AddScoped<IGrainContext>(sp => RuntimeContext.Current ?? throw new InvalidOperationException("No current grain context available."));

            services.TryAddSingleton<IConsistentRingProvider>(
                sp =>
                {
                    // TODO: make this not sux - jbragg
                    var consistentRingOptions = sp.GetRequiredService<IOptions<ConsistentRingOptions>>().Value;
                    var siloDetails = sp.GetRequiredService<ILocalSiloDetails>();
                    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
                    var siloStatusOracle = sp.GetRequiredService<ISiloStatusOracle>();
                    if (consistentRingOptions.UseVirtualBucketsConsistentRing)
                    {
                        return new VirtualBucketsRingProvider(siloDetails.SiloAddress, loggerFactory, consistentRingOptions.NumVirtualBucketsConsistentRing, siloStatusOracle);
                    }

                    return new ConsistentRingProvider(siloDetails.SiloAddress, loggerFactory, siloStatusOracle);
                });

            services.AddSingleton<IConfigureOptions<GrainTypeOptions>, DefaultGrainTypeOptionsProvider>();
            services.AddSingleton<IPostConfigureOptions<EndpointOptions>, EndpointOptionsProvider>();

            // Type metadata
            services.AddSingleton<SiloManifestProvider>();
            services.AddSingleton<GrainClassMap>(sp => sp.GetRequiredService<SiloManifestProvider>().GrainTypeMap);
            services.AddSingleton<GrainTypeResolver>();
            services.AddSingleton<IGrainTypeProvider, AttributeGrainTypeProvider>();
            services.AddSingleton<GrainPropertiesResolver>();
            services.AddSingleton<GrainInterfaceTypeResolver>();
            services.AddSingleton<IGrainInterfaceTypeProvider, AttributeGrainInterfaceTypeProvider>();
            services.AddSingleton<IGrainInterfacePropertiesProvider, AttributeGrainInterfacePropertiesProvider>();
            services.AddSingleton<IGrainPropertiesProvider, AttributeGrainPropertiesProvider>();
            services.AddSingleton<IGrainPropertiesProvider, AttributeGrainBindingsProvider>();
            services.AddSingleton<IGrainInterfacePropertiesProvider, TypeNameGrainPropertiesProvider>();
            services.AddSingleton<IGrainPropertiesProvider, TypeNameGrainPropertiesProvider>();
            services.AddSingleton<IGrainPropertiesProvider, ImplementedInterfaceProvider>();
            services.AddSingleton<ClusterManifestProvider>();
            services.AddFromExisting<IClusterManifestProvider, ClusterManifestProvider>();
            services.AddFromExisting<ILifecycleParticipant<ISiloLifecycle>, ClusterManifestProvider>();
            services.AddSingleton<ClusterManifestSystemTarget>();
            services.AddFromExisting<ILifecycleParticipant<ISiloLifecycle>, ClusterManifestSystemTarget>();

            //Add default option formatter if none is configured, for options which are required to be configured
            services.ConfigureFormatter<SiloOptions>();
            services.ConfigureFormatter<SchedulingOptions>();
            services.ConfigureFormatter<ConnectionOptions>();
            services.ConfigureFormatter<SiloMessagingOptions>();
            services.ConfigureFormatter<ClusterMembershipOptions>();
            services.ConfigureFormatter<GrainDirectoryOptions>();
            services.ConfigureFormatter<ActivationCountBasedPlacementOptions>();
            services.ConfigureFormatter<ResourceOptimizedPlacementOptions>();
            services.ConfigureFormatter<GrainCollectionOptions>();
            services.ConfigureFormatter<StatelessWorkerOptions>();
            services.ConfigureFormatter<GrainVersioningOptions>();
            services.ConfigureFormatter<ConsistentRingOptions>();
            services.ConfigureFormatter<LoadSheddingOptions>();
            services.ConfigureFormatter<EndpointOptions>();
            services.ConfigureFormatter<ClusterOptions>();

            // This validator needs to construct the IMembershipOracle and the IMembershipTable
            // so move it in the end so other validator are called first
            services.AddTransient<IConfigurationValidator, ClusterOptionsValidator>();
            services.AddTransient<IConfigurationValidator, SiloClusteringValidator>();
            services.AddTransient<IConfigurationValidator, DevelopmentClusterMembershipOptionsValidator>();
            services.AddTransient<IConfigurationValidator, GrainTypeOptionsValidator>();
            services.AddTransient<IValidateOptions<SiloMessagingOptions>, SiloMessagingOptionsValidator>();
            services.AddTransient<IOptions<MessagingOptions>>(static sp => sp.GetRequiredService<IOptions<SiloMessagingOptions>>());

            // Enable hosted client.
            services.TryAddSingleton<HostedClient>();
            services.AddFromExisting<ILifecycleParticipant<ISiloLifecycle>, HostedClient>();
            services.TryAddSingleton<InternalClusterClient>();
            services.TryAddFromExisting<IInternalClusterClient, InternalClusterClient>();
            services.TryAddFromExisting<IClusterClient, InternalClusterClient>();

            // Enable collection specific Age limits
            services.AddOptions<GrainCollectionOptions>()
                .Configure<IOptions<GrainTypeOptions>>((options, grainTypeOptions) =>
                {
                    foreach (var grainClass in grainTypeOptions.Value.Classes)
                    {
                        var attr = grainClass.GetCustomAttribute<CollectionAgeLimitAttribute>();
                        if (attr != null)
                        {
                            var className = RuntimeTypeNameFormatter.Format(grainClass);
                            options.ClassSpecificCollectionAge[className] = attr.AgeLimit;
                        }
                    }
                });

            // Validate all CollectionAgeLimit values for the right configuration.
            services.AddTransient<IConfigurationValidator, GrainCollectionOptionsValidator>();
            services.AddTransient<IConfigurationValidator, LoadSheddingValidator>();

            services.TryAddSingleton<ITimerManager, TimerManagerImpl>();

            // persistent state facet support
            services.TryAddSingleton<IGrainStorageSerializer, JsonGrainStorageSerializer>();
            services.TryAddSingleton<IPersistentStateFactory, PersistentStateFactory>();
            services.TryAddSingleton(typeof(IAttributeToFactoryMapper<PersistentStateAttribute>), typeof(PersistentStateAttributeMapper));
            services.TryAddSingleton<StateStorageBridgeSharedMap>();

            // IAsyncEnumerable support
            services.AddScoped<IAsyncEnumerableGrainExtension, AsyncEnumerableGrainExtension>();
            services.AddKeyedTransient<IGrainExtension>(
                typeof(IAsyncEnumerableGrainExtension),
                (sp, _) => sp.GetRequiredService<IAsyncEnumerableGrainExtension>());

            // Networking
            services.TryAddSingleton<IMessageStatisticsSink, NoOpMessageStatisticsSink>();
            services.TryAddSingleton<ConnectionCommon>();
            services.TryAddSingleton<ConnectionManager>();
            services.TryAddSingleton<ConnectionPreambleHelper>();
            services.AddSingleton<ILifecycleParticipant<ISiloLifecycle>, ConnectionManagerLifecycleAdapter<ISiloLifecycle>>();
            services.AddSingleton<ILifecycleParticipant<ISiloLifecycle>, SiloConnectionMaintainer>();

            services.AddKeyedSingleton<IConnectionFactory>(
                SiloConnectionFactory.ServicesKey,
                (sp, key) => ActivatorUtilities.CreateInstance<SocketConnectionFactory>(sp));
            services.AddKeyedSingleton<IConnectionListenerFactory>(
                SiloConnectionListener.ServicesKey,
                (sp, key) => ActivatorUtilities.CreateInstance<SocketConnectionListenerFactory>(sp));
            services.AddKeyedSingleton<IConnectionListenerFactory>(
                GatewayConnectionListener.ServicesKey,
                (sp, key) => ActivatorUtilities.CreateInstance<SocketConnectionListenerFactory>(sp));

            services.AddSerializer();
            services.AddSingleton<ITypeNameFilter, AllowOrleansTypes>();
            services.AddSingleton<ISpecializableCodec, GrainReferenceCodecProvider>();
            services.AddSingleton<ISpecializableCopier, GrainReferenceCopierProvider>();
            services.AddSingleton<OnDeserializedCallbacks>();
            services.AddTransient<IConfigurationValidator, SerializerConfigurationValidator>();
            services.AddSingleton<IPostConfigureOptions<OrleansJsonSerializerOptions>, ConfigureOrleansJsonSerializerOptions>();
            services.AddSingleton<OrleansJsonSerializer>();

            services.TryAddTransient(sp => ActivatorUtilities.CreateInstance<MessageSerializer>(
                sp,
                sp.GetRequiredService<IOptions<SiloMessagingOptions>>().Value));
            services.TryAddSingleton<ConnectionFactory, SiloConnectionFactory>();
            services.AddSingleton<NetworkingTrace>();
            services.AddSingleton<RuntimeMessagingTrace>();
            services.AddFromExisting<MessagingTrace, RuntimeMessagingTrace>();

            // Use Orleans server.
            services.AddSingleton<ILifecycleParticipant<ISiloLifecycle>, SiloConnectionListener>();
            services.AddSingleton<ILifecycleParticipant<ISiloLifecycle>, GatewayConnectionListener>();
            services.AddSingleton<SocketSchedulers>();
            services.AddSingleton<SharedMemoryPool>();

            // Activation migration
            services.AddSingleton<MigrationContext.SerializationHooks>();
            services.AddSingleton<ActivationMigrationManager>();
            services.AddFromExisting<IActivationMigrationManager, ActivationMigrationManager>();
            services.AddFromExisting<ILifecycleParticipant<ISiloLifecycle>, ActivationMigrationManager>();
            services.AddSingleton<GrainMigratabilityChecker>();

            // Cancellation manager
            services.AddSingleton<GrainCallCancellationManager>();
            services.AddFromExisting<IGrainCallCancellationManager, GrainCallCancellationManager>();
            services.AddFromExisting<ILifecycleParticipant<ISiloLifecycle>, GrainCallCancellationManager>();

            ApplyConfiguration(builder);
        }

        private static void ApplyConfiguration(ISiloBuilder builder)
        {
            var services = builder.Services;
            var cfg = builder.Configuration.GetSection("Orleans");
            var knownProviderTypes = GetRegisteredProviders();

            if (cfg["Name"] is { Length: > 0 } name)
            {
                services.Configure<SiloOptions>(siloOptions => siloOptions.SiloName = name);
            }

            services.Configure<ClusterOptions>(cfg);
            services.Configure<SiloMessagingOptions>(cfg.GetSection("Messaging"));
            if (cfg.GetSection("Endpoints") is { } ep && ep.Exists())
            {
                services.Configure<EndpointOptions>(o => o.Bind(ep));
            }

            if (bool.TryParse(cfg["EnableDistributedTracing"], out var enableDistributedTracing) && enableDistributedTracing)
            {
                builder.AddActivityPropagation();
            }

            ApplySubsection(builder, cfg, knownProviderTypes, "Clustering");
            ApplySubsection(builder, cfg, knownProviderTypes, "Reminders");
            ApplyNamedSubsections(builder, cfg, knownProviderTypes, "BroadcastChannel");
            ApplyNamedSubsections(builder, cfg, knownProviderTypes, "Streaming");
            ApplyNamedSubsections(builder, cfg, knownProviderTypes, "GrainStorage");
            ApplyNamedSubsections(builder, cfg, knownProviderTypes, "GrainDirectory");

            static void ConfigureProvider(ISiloBuilder builder, Dictionary<(string Kind, string Name), Type> knownProviderTypes, string kind, string? name, IConfigurationSection configurationSection)
            {
                var providerType = configurationSection["ProviderType"] ?? "Default";
                var provider = GetRequiredProvider(knownProviderTypes, kind, providerType);
                provider.Configure(builder, name, configurationSection);
            }

            static IProviderBuilder<ISiloBuilder> GetRequiredProvider(Dictionary<(string Kind, string Name), Type> knownProviderTypes, string kind, string name)
            {
                if (knownProviderTypes.TryGetValue((kind, name), out var type))
                {
                    var instance = Activator.CreateInstance(type);
                    return instance as IProviderBuilder<ISiloBuilder>
                        ?? throw new InvalidOperationException($"{kind} provider, '{name}', of type {type}, does not implement {typeof(IProviderBuilder<ISiloBuilder>)}.");
                }

                throw new InvalidOperationException($"Could not find {kind} provider named '{name}'. This can indicate that either the 'Microsoft.Orleans.Sdk' or the provider's package are not referenced by your application.");
            }

            static Dictionary<(string Kind, string Name), Type> GetRegisteredProviders()
            {
                var result = new Dictionary<(string, string), Type>();
                foreach (var asm in ReferencedAssemblyProvider.GetRelevantAssemblies())
                {
                    foreach (var attr in asm.GetCustomAttributes<RegisterProviderAttribute>())
                    {
                        if (string.Equals(attr.Target, "Silo"))
                        {
                            result[(attr.Kind, attr.Name)] = attr.Type;
                        }
                    }
                }

                return result;
            }

            static void ApplySubsection(ISiloBuilder builder, IConfigurationSection cfg, Dictionary<(string Kind, string Name), Type> knownProviderTypes, string sectionName)
            {
                if (cfg.GetSection(sectionName) is { } section && section.Exists())
                {
                    ConfigureProvider(builder, knownProviderTypes, sectionName, name: null, section);
                }
            }

            static void ApplyNamedSubsections(ISiloBuilder builder, IConfigurationSection cfg, Dictionary<(string Kind, string Name), Type> knownProviderTypes, string sectionName)
            {
                if (cfg.GetSection(sectionName) is { } section && section.Exists())
                {
                    foreach (var child in section.GetChildren())
                    {
                        ConfigureProvider(builder, knownProviderTypes, sectionName, name: child.Key, child);
                    }
                }
            }
        }

        private class AllowOrleansTypes : ITypeNameFilter
        {
            public bool? IsTypeNameAllowed(string typeName, string assemblyName)
            {
                if (assemblyName is { Length: > 0} && assemblyName.Contains("Orleans"))
                {
                    return true;
                }

                return null;
            }
        }

        private class ServicesAdded { }
    }
}

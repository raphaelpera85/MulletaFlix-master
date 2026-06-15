using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MulletaFlix.Database.Implementations;
using MulletaFlix.Database.Implementations.Contexts;
using MulletaFlix.Database.Implementations.DbConfiguration;
using MulletaFlix.Database.Implementations.Locking;
using MulletaFlix.Database.Providers.Sqlite;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MulletaFlixDbProviderFactory = System.Func<System.IServiceProvider, MulletaFlix.Database.Implementations.IMulletaFlixDatabaseProvider>;

namespace MulletaFlix.Server.Implementations.Extensions;

/// <summary>
/// Extensions for the <see cref="IServiceCollection"/> interface.
/// </summary>
public static class ServiceCollectionExtensions
{
    private static IEnumerable<Type> DatabaseProviderTypes()
    {
        yield return typeof(SqliteDatabaseProvider);
        yield return typeof(MySqlDatabaseProvider);
    }

    private static IDictionary<string, MulletaFlixDbProviderFactory> GetSupportedDbProviders()
    {
        var items = new Dictionary<string, MulletaFlixDbProviderFactory>(StringComparer.InvariantCultureIgnoreCase);
        foreach (var providerType in DatabaseProviderTypes())
        {
            var keyAttribute = providerType.GetCustomAttribute<MulletaFlixDatabaseProviderKeyAttribute>();
            if (keyAttribute is null || string.IsNullOrWhiteSpace(keyAttribute.DatabaseProviderKey))
            {
                continue;
            }

            var provider = providerType;
            items[keyAttribute.DatabaseProviderKey] = (services) => (IMulletaFlixDatabaseProvider)ActivatorUtilities.CreateInstance(services, providerType);
        }

        return items;
    }

    private static MulletaFlixDbProviderFactory? LoadDatabasePlugin(CustomDatabaseOptions customProviderOptions, IApplicationPaths applicationPaths)
    {
        var plugin = Directory.EnumerateDirectories(applicationPaths.PluginsPath)
            .Where(e => Path.GetFileName(e)!.StartsWith(customProviderOptions.PluginName, StringComparison.OrdinalIgnoreCase))
            .Order()
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"The requested custom database plugin with the name '{customProviderOptions.PluginName}' could not been found in '{applicationPaths.PluginsPath}'");

        var dbProviderAssembly = Path.Combine(plugin, Path.ChangeExtension(customProviderOptions.PluginAssembly, "dll"));
        if (!File.Exists(dbProviderAssembly))
        {
            throw new InvalidOperationException($"Could not find the requested assembly at '{dbProviderAssembly}'");
        }

        // we have to load the assembly without proxy to ensure maximum performance for this.
        var assembly = Assembly.LoadFrom(dbProviderAssembly);
        var dbProviderType = assembly.GetExportedTypes().FirstOrDefault(f => f.IsAssignableTo(typeof(IMulletaFlixDatabaseProvider)))
            ?? throw new InvalidOperationException($"Could not find any type implementing the '{nameof(IMulletaFlixDatabaseProvider)}' interface.");

        return (services) => (IMulletaFlixDatabaseProvider)ActivatorUtilities.CreateInstance(services, dbProviderType);
    }

    /// <summary>
    /// Adds the <see cref="IDbContextFactory{TContext}"/> interface to the service collection with second level caching enabled.
    /// </summary>
    /// <param name="serviceCollection">An instance of the <see cref="IServiceCollection"/> interface.</param>
    /// <param name="configurationManager">The server configuration manager.</param>
    /// <param name="configuration">The startup Configuration.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddMulletaFlixDbContext(
        this IServiceCollection serviceCollection,
        IServerConfigurationManager configurationManager,
        IConfiguration configuration)
    {
        var efCoreConfiguration = configurationManager.GetConfiguration<DatabaseConfigurationOptions>("database");
        MulletaFlixDbProviderFactory? providerFactory = null;

        if (efCoreConfiguration?.DatabaseType is null)
        {
            var cmdMigrationArgument = configuration.GetValue<string>("migration-provider");
            if (!string.IsNullOrWhiteSpace(cmdMigrationArgument))
            {
                efCoreConfiguration = new DatabaseConfigurationOptions()
                {
                    DatabaseType = cmdMigrationArgument,
                };
            }
            else
            {
                // fallback to MariaDB/MySQL with default settings.
                efCoreConfiguration = new DatabaseConfigurationOptions()
                {
                    DatabaseType = "MulletaFlix-MySQL",
                    LockingBehavior = DatabaseLockingBehaviorTypes.NoLock
                };
                configurationManager.SaveConfiguration("database", efCoreConfiguration);
            }
        }

        if (efCoreConfiguration.DatabaseType.Equals("PLUGIN_PROVIDER", StringComparison.OrdinalIgnoreCase))
        {
            if (efCoreConfiguration.CustomProviderOptions is null)
            {
                throw new InvalidOperationException("The custom database provider must declare the custom provider options to work");
            }

            providerFactory = LoadDatabasePlugin(efCoreConfiguration.CustomProviderOptions, configurationManager.ApplicationPaths);
        }
        else
        {
            var providers = GetSupportedDbProviders();
            if (!providers.TryGetValue(efCoreConfiguration.DatabaseType.ToUpperInvariant(), out providerFactory!))
            {
                throw new InvalidOperationException($"MulletaFlix cannot find the database provider of type '{efCoreConfiguration.DatabaseType}'. Supported types are {string.Join(", ", providers.Keys)}");
            }
        }

        serviceCollection.AddSingleton<IMulletaFlixDatabaseProvider>(providerFactory!);

        switch (efCoreConfiguration.LockingBehavior)
        {
            case DatabaseLockingBehaviorTypes.NoLock:
                serviceCollection.AddSingleton<IEntityFrameworkCoreLockingBehavior, NoLockBehavior>();
                break;
            case DatabaseLockingBehaviorTypes.Pessimistic:
                serviceCollection.AddSingleton<IEntityFrameworkCoreLockingBehavior, PessimisticLockBehavior>();
                break;
            case DatabaseLockingBehaviorTypes.Optimistic:
                serviceCollection.AddSingleton<IEntityFrameworkCoreLockingBehavior, OptimisticLockBehavior>();
                break;
        }

        serviceCollection.AddPooledDbContextFactory<MulletaFlixDbContext>((serviceProvider, opt) =>
        {
            var provider = serviceProvider.GetRequiredService<IMulletaFlixDatabaseProvider>();
            provider.Initialise(opt, efCoreConfiguration);
            var lockingBehavior = serviceProvider.GetRequiredService<IEntityFrameworkCoreLockingBehavior>();
            lockingBehavior.Initialise(opt);
        });

        serviceCollection.AddPooledDbContextFactory<UsersDbContext>((serviceProvider, opt) =>
        {
            var provider = serviceProvider.GetRequiredService<IMulletaFlixDatabaseProvider>();
            provider.Initialise(opt, efCoreConfiguration);
        });

        serviceCollection.AddPooledDbContextFactory<MoviesDbContext>((serviceProvider, opt) =>
        {
            var provider = serviceProvider.GetRequiredService<IMulletaFlixDatabaseProvider>();
            provider.Initialise(opt, efCoreConfiguration);
        });

        serviceCollection.AddPooledDbContextFactory<SeriesDbContext>((serviceProvider, opt) =>
        {
            var provider = serviceProvider.GetRequiredService<IMulletaFlixDatabaseProvider>();
            provider.Initialise(opt, efCoreConfiguration);
        });

        serviceCollection.AddPooledDbContextFactory<ChannelsDbContext>((serviceProvider, opt) =>
        {
            var provider = serviceProvider.GetRequiredService<IMulletaFlixDatabaseProvider>();
            provider.Initialise(opt, efCoreConfiguration);
        });

        serviceCollection.AddPooledDbContextFactory<BooksDbContext>((serviceProvider, opt) =>
        {
            var provider = serviceProvider.GetRequiredService<IMulletaFlixDatabaseProvider>();
            provider.Initialise(opt, efCoreConfiguration);
        });

        serviceCollection.AddPooledDbContextFactory<SystemDbContext>((serviceProvider, opt) =>
        {
            var provider = serviceProvider.GetRequiredService<IMulletaFlixDatabaseProvider>();
            provider.Initialise(opt, efCoreConfiguration);
        });

        return serviceCollection;
    }
}


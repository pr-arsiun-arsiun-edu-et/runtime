// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.Hosting
{
    /// <summary>
    /// A program initialization utility.
    /// </summary>
    public partial class HostBuilder : IHostBuilder
    {
        private List<Action<IConfigurationBuilder>> _configureHostConfigActions = new List<Action<IConfigurationBuilder>>();
        private List<Action<HostBuilderContext, IConfigurationBuilder>> _configureAppConfigActions = new List<Action<HostBuilderContext, IConfigurationBuilder>>();
        private List<Action<HostBuilderContext, IServiceCollection>> _configureServicesActions = new List<Action<HostBuilderContext, IServiceCollection>>();
        private List<IConfigureContainerAdapter> _configureContainerActions = new List<IConfigureContainerAdapter>();
        private IServiceFactoryAdapter _serviceProviderFactory = new ServiceFactoryAdapter<IServiceCollection>(new DefaultServiceProviderFactory());
        private bool _hostBuilt;
        private IConfiguration _hostConfiguration;
        private IConfiguration _appConfiguration;
        private HostBuilderContext _hostBuilderContext;
        private HostingEnvironment _hostingEnvironment;
        private IServiceProvider _appServices;
        private PhysicalFileProvider _defaultProvider;

        /// <summary>
        /// A central location for sharing state between components during the host building process.
        /// </summary>
        public IDictionary<object, object> Properties { get; } = new Dictionary<object, object>();

        /// <summary>
        /// Set up the configuration for the builder itself. This will be used to initialize the <see cref="IHostEnvironment"/>
        /// for use later in the build process. This can be called multiple times and the results will be additive.
        /// </summary>
        /// <param name="configureDelegate">The delegate for configuring the <see cref="IConfigurationBuilder"/> that will be used
        /// to construct the <see cref="IConfiguration"/> for the host.</param>
        /// <returns>The same instance of the <see cref="IHostBuilder"/> for chaining.</returns>
        public IHostBuilder ConfigureHostConfiguration(Action<IConfigurationBuilder> configureDelegate!!)
        {
            _configureHostConfigActions.Add(configureDelegate);
            return this;
        }

        /// <summary>
        /// Sets up the configuration for the remainder of the build process and application. This can be called multiple times and
        /// the results will be additive. The results will be available at <see cref="HostBuilderContext.Configuration"/> for
        /// subsequent operations, as well as in <see cref="IHost.Services"/>.
        /// </summary>
        /// <param name="configureDelegate">The delegate for configuring the <see cref="IConfigurationBuilder"/> that will be used
        /// to construct the <see cref="IConfiguration"/> for the host.</param>
        /// <returns>The same instance of the <see cref="IHostBuilder"/> for chaining.</returns>
        public IHostBuilder ConfigureAppConfiguration(Action<HostBuilderContext, IConfigurationBuilder> configureDelegate!!)
        {
            _configureAppConfigActions.Add(configureDelegate);
            return this;
        }

        /// <summary>
        /// Adds services to the container. This can be called multiple times and the results will be additive.
        /// </summary>
        /// <param name="configureDelegate">The delegate for configuring the <see cref="IConfigurationBuilder"/> that will be used
        /// to construct the <see cref="IConfiguration"/> for the host.</param>
        /// <returns>The same instance of the <see cref="IHostBuilder"/> for chaining.</returns>
        public IHostBuilder ConfigureServices(Action<HostBuilderContext, IServiceCollection> configureDelegate!!)
        {
            _configureServicesActions.Add(configureDelegate);
            return this;
        }

        /// <summary>
        /// Overrides the factory used to create the service provider.
        /// </summary>
        /// <typeparam name="TContainerBuilder">The type of the builder to create.</typeparam>
        /// <param name="factory">A factory used for creating service providers.</param>
        /// <returns>The same instance of the <see cref="IHostBuilder"/> for chaining.</returns>
        public IHostBuilder UseServiceProviderFactory<TContainerBuilder>(IServiceProviderFactory<TContainerBuilder> factory!!)
        {
            _serviceProviderFactory = new ServiceFactoryAdapter<TContainerBuilder>(factory);
            return this;
        }

        /// <summary>
        /// Overrides the factory used to create the service provider.
        /// </summary>
        /// <param name="factory">A factory used for creating service providers.</param>
        /// <typeparam name="TContainerBuilder">The type of the builder to create.</typeparam>
        /// <returns>The same instance of the <see cref="IHostBuilder"/> for chaining.</returns>
        public IHostBuilder UseServiceProviderFactory<TContainerBuilder>(Func<HostBuilderContext, IServiceProviderFactory<TContainerBuilder>> factory!!)
        {
            _serviceProviderFactory = new ServiceFactoryAdapter<TContainerBuilder>(() => _hostBuilderContext, factory);
            return this;
        }

        /// <summary>
        /// Enables configuring the instantiated dependency container. This can be called multiple times and
        /// the results will be additive.
        /// </summary>
        /// <typeparam name="TContainerBuilder">The type of the builder to create.</typeparam>
        /// <param name="configureDelegate">The delegate for configuring the <see cref="IConfigurationBuilder"/> that will be used
        /// to construct the <see cref="IConfiguration"/> for the host.</param>
        /// <returns>The same instance of the <see cref="IHostBuilder"/> for chaining.</returns>
        public IHostBuilder ConfigureContainer<TContainerBuilder>(Action<HostBuilderContext, TContainerBuilder> configureDelegate!!)
        {
            _configureContainerActions.Add(new ConfigureContainerAdapter<TContainerBuilder>(configureDelegate));
            return this;
        }

        /// <summary>
        /// Run the given actions to initialize the host. This can only be called once.
        /// </summary>
        /// <returns>An initialized <see cref="IHost"/></returns>
        /// <remarks>Adds basic services to the host such as application lifetime, host environment, and logging.</remarks>
        public IHost Build()
        {
            if (_hostBuilt)
            {
                throw new InvalidOperationException(SR.BuildCalled);
            }
            _hostBuilt = true;

            // REVIEW: If we want to raise more events outside of these calls then we will need to
            // stash this in a field.
            using var diagnosticListener = new DiagnosticListener("Microsoft.Extensions.Hosting");
            const string hostBuildingEventName = "HostBuilding";
            const string hostBuiltEventName = "HostBuilt";

            if (diagnosticListener.IsEnabled() && diagnosticListener.IsEnabled(hostBuildingEventName))
            {
                Write(diagnosticListener, hostBuildingEventName, this);
            }

            BuildHostConfiguration();
            CreateHostingEnvironment();
            CreateHostBuilderContext();
            BuildAppConfiguration();
            CreateServiceProvider();

            var host = _appServices.GetRequiredService<IHost>();
            if (diagnosticListener.IsEnabled() && diagnosticListener.IsEnabled(hostBuiltEventName))
            {
                Write(diagnosticListener, hostBuiltEventName, host);
            }

            return host;
        }

        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:UnrecognizedReflectionPattern",
            Justification = "The values being passed into Write are being consumed by the application already.")]
        private static void Write<T>(
            DiagnosticSource diagnosticSource,
            string name,
            T value)
        {
            diagnosticSource.Write(name, value);
        }

        private void BuildHostConfiguration()
        {
            IConfigurationBuilder configBuilder = new ConfigurationBuilder()
                .AddInMemoryCollection(); // Make sure there's some default storage since there are no default providers

            foreach (Action<IConfigurationBuilder> buildAction in _configureHostConfigActions)
            {
                buildAction(configBuilder);
            }
            _hostConfiguration = configBuilder.Build();
        }

        private void CreateHostingEnvironment()
        {
            _hostingEnvironment = new HostingEnvironment()
            {
                ApplicationName = _hostConfiguration[HostDefaults.ApplicationKey],
                EnvironmentName = _hostConfiguration[HostDefaults.EnvironmentKey] ?? Environments.Production,
                ContentRootPath = ResolveContentRootPath(_hostConfiguration[HostDefaults.ContentRootKey], AppContext.BaseDirectory),
            };

            if (string.IsNullOrEmpty(_hostingEnvironment.ApplicationName))
            {
                // Note GetEntryAssembly returns null for the net4x console test runner.
                _hostingEnvironment.ApplicationName = Assembly.GetEntryAssembly()?.GetName().Name;
            }

            _hostingEnvironment.ContentRootFileProvider = _defaultProvider = new PhysicalFileProvider(_hostingEnvironment.ContentRootPath);
        }

        private string ResolveContentRootPath(string contentRootPath, string basePath)
        {
            if (string.IsNullOrEmpty(contentRootPath))
            {
                return basePath;
            }
            if (Path.IsPathRooted(contentRootPath))
            {
                return contentRootPath;
            }
            return Path.Combine(Path.GetFullPath(basePath), contentRootPath);
        }

        private void CreateHostBuilderContext()
        {
            _hostBuilderContext = new HostBuilderContext(Properties)
            {
                HostingEnvironment = _hostingEnvironment,
                Configuration = _hostConfiguration
            };
        }

        private void BuildAppConfiguration()
        {
            IConfigurationBuilder configBuilder = new ConfigurationBuilder()
                .SetBasePath(_hostingEnvironment.ContentRootPath)
                .AddConfiguration(_hostConfiguration, shouldDisposeConfiguration: true);

            foreach (Action<HostBuilderContext, IConfigurationBuilder> buildAction in _configureAppConfigActions)
            {
                buildAction(_hostBuilderContext, configBuilder);
            }
            _appConfiguration = configBuilder.Build();
            _hostBuilderContext.Configuration = _appConfiguration;
        }

        private void CreateServiceProvider()
        {
            var services = new ServiceCollection();
#pragma warning disable CS0618 // Type or member is obsolete
            services.AddSingleton<IHostingEnvironment>(_hostingEnvironment);
#pragma warning restore CS0618 // Type or member is obsolete
            services.AddSingleton<IHostEnvironment>(_hostingEnvironment);
            services.AddSingleton(_hostBuilderContext);
            // register configuration as factory to make it dispose with the service provider
            services.AddSingleton(_ => _appConfiguration);
#pragma warning disable CS0618 // Type or member is obsolete
            services.AddSingleton<IApplicationLifetime>(s => (IApplicationLifetime)s.GetService<IHostApplicationLifetime>());
#pragma warning restore CS0618 // Type or member is obsolete
            services.AddSingleton<IHostApplicationLifetime, ApplicationLifetime>();

            AddLifetime(services);

            services.AddSingleton<IHost>(_ =>
            {
                return new Internal.Host(_appServices,
                    _hostingEnvironment,
                    _defaultProvider,
                    _appServices.GetRequiredService<IHostApplicationLifetime>(),
                    _appServices.GetRequiredService<ILogger<Internal.Host>>(),
                    _appServices.GetRequiredService<IHostLifetime>(),
                    _appServices.GetRequiredService<IOptions<HostOptions>>());
            });
            services.AddOptions().Configure<HostOptions>(options => { options.Initialize(_hostConfiguration); });
            services.AddLogging();

            foreach (Action<HostBuilderContext, IServiceCollection> configureServicesAction in _configureServicesActions)
            {
                configureServicesAction(_hostBuilderContext, services);
            }

            object containerBuilder = _serviceProviderFactory.CreateBuilder(services);

            foreach (IConfigureContainerAdapter containerAction in _configureContainerActions)
            {
                containerAction.ConfigureContainer(_hostBuilderContext, containerBuilder);
            }

            _appServices = _serviceProviderFactory.CreateServiceProvider(containerBuilder);

            if (_appServices == null)
            {
                throw new InvalidOperationException(SR.NullIServiceProvider);
            }

            // resolve configuration explicitly once to mark it as resolved within the
            // service provider, ensuring it will be properly disposed with the provider
            _ = _appServices.GetService<IConfiguration>();
        }
    }
}

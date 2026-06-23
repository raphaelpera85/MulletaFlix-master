using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text;
using Emby.Server.Implementations.EntryPoints;
using Emby.Server.Implementations.Localization;
using Polly;
using Polly.Extensions.Http;
using Polly.Timeout;
using Polly.Timeout;
using MulletaFlix.Api.Middleware;
using MulletaFlix.Api.Jobs;
using MulletaFlix.Database.Implementations;
using MulletaFlix.LiveTv.Extensions;
using MulletaFlix.LiveTv.Recordings;
using MulletaFlix.MediaEncoding.Hls.Extensions;
using MulletaFlix.Networking;
using MulletaFlix.Networking.HappyEyeballs;
using MulletaFlix.Server.Extensions;
using MulletaFlix.Server.HealthChecks;
using MulletaFlix.Server.Implementations.Extensions;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Extensions;
using MediaBrowser.Providers.Plugins.MidiaStorageOnline;
using MediaBrowser.XbmcMetadata;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Primitives;
using Prometheus;

namespace MulletaFlix.Server
{
    /// <summary>
    /// Startup configuration for the Kestrel webhost.
    /// </summary>
    public class Startup
    {
        private readonly CoreAppHost _serverApplicationHost;
        private readonly IConfiguration _configuration;
        private readonly IServerConfigurationManager _serverConfigurationManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="Startup" /> class.
        /// </summary>
        /// <param name="appHost">The server application host.</param>
        /// <param name="configuration">The used Configuration.</param>
        public Startup(CoreAppHost appHost, IConfiguration configuration)
        {
            _serverApplicationHost = appHost;
            _configuration = configuration;
            _serverConfigurationManager = appHost.ConfigurationManager;
        }

        /// <summary>
        /// Configures the service collection for the webhost.
        /// </summary>
        /// <param name="services">The service collection.</param>
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddResponseCompression();
            services.AddHttpContextAccessor();
            services.AddHttpsRedirection(options =>
            {
                options.HttpsPort = _serverApplicationHost.HttpsPort;
            });

            services.AddMulletaFlixApi(_serverApplicationHost.GetApiPluginAssemblies(), _serverConfigurationManager.GetNetworkConfiguration());
            services.AddMulletaFlixDbContext(_serverApplicationHost.ConfigurationManager, _configuration);
            services.AddMulletaFlixApiSwagger();

            // configure custom legacy authentication
            services.AddCustomAuthentication();

            services.AddMulletaFlixApiAuthorization();

            var productHeader = new ProductInfoHeaderValue(
                _serverApplicationHost.Name.Replace(' ', '-'),
                _serverApplicationHost.ApplicationVersionString);
            var acceptJsonHeader = new MediaTypeWithQualityHeaderValue(MediaTypeNames.Application.Json, 1.0);
            var acceptXmlHeader = new MediaTypeWithQualityHeaderValue(MediaTypeNames.Application.Xml, 0.9);
            var acceptAnyHeader = new MediaTypeWithQualityHeaderValue("*/*", 0.8);
            Func<IServiceProvider, HttpMessageHandler> eyeballsHttpClientHandlerDelegate = (_) => new SocketsHttpHandler()
            {
                AutomaticDecompression = DecompressionMethods.All,
                RequestHeaderEncodingSelector = (_, _) => Encoding.UTF8,
                ConnectCallback = HttpClientExtension.OnConnect
            };

            Func<IServiceProvider, HttpMessageHandler> defaultHttpClientHandlerDelegate = (_) => new SocketsHttpHandler()
            {
                AutomaticDecompression = DecompressionMethods.All,
                RequestHeaderEncodingSelector = (_, _) => Encoding.UTF8
            };

            services.AddHttpClient(NamedClient.Default, c =>
                {
                    c.DefaultRequestHeaders.UserAgent.Add(productHeader);
                    c.DefaultRequestHeaders.Accept.Add(acceptJsonHeader);
                    c.DefaultRequestHeaders.Accept.Add(acceptXmlHeader);
                    c.DefaultRequestHeaders.Accept.Add(acceptAnyHeader);
                })
                .ConfigurePrimaryHttpMessageHandler(eyeballsHttpClientHandlerDelegate)
                .AddPolicyHandler(GetRetryPolicy());

            services.AddHttpClient(NamedClient.MusicBrainz, c =>
                {
                    c.DefaultRequestHeaders.UserAgent.Add(productHeader);
                    c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue($"({_serverApplicationHost.ApplicationUserAgentAddress})"));
                    c.DefaultRequestHeaders.Accept.Add(acceptXmlHeader);
                    c.DefaultRequestHeaders.Accept.Add(acceptAnyHeader);
                })
                .ConfigurePrimaryHttpMessageHandler(eyeballsHttpClientHandlerDelegate)
                .AddPolicyHandler(GetRetryPolicy());

            services.AddHttpClient(NamedClient.DirectIp, c =>
                {
                    c.DefaultRequestHeaders.UserAgent.Add(productHeader);
                    c.DefaultRequestHeaders.Accept.Add(acceptJsonHeader);
                    c.DefaultRequestHeaders.Accept.Add(acceptXmlHeader);
                    c.DefaultRequestHeaders.Accept.Add(acceptAnyHeader);
                })
                .ConfigurePrimaryHttpMessageHandler(defaultHttpClientHandlerDelegate)
                .AddPolicyHandler(GetRetryPolicy());

            services.AddHsts(options =>
            {
                options.MaxAge = TimeSpan.FromDays(365);
                options.IncludeSubDomains = true;
                options.Preload = false;
            });

            services.AddHealthChecks()
                .AddCheck<DbContextFactoryHealthCheck<MulletaFlixDbContext>>(nameof(MulletaFlixDbContext));

            services.AddHlsPlaylistGenerator();
            services.AddLiveTvServices();
            services.AddSingleton<MulletaFlixJobQueue>();
            services.AddSingleton<IJobQueue>(serviceProvider => serviceProvider.GetRequiredService<MulletaFlixJobQueue>());

            services.AddSingleton<MulletaFlix.Api.Caching.ItemsResponseCache>();

            // Book Reader services
            services.AddSingleton<MediaBrowser.Controller.Books.IBookConversionService, MulletaFlix.Server.Implementations.Books.BookConversionService>();
            services.AddSingleton<MediaBrowser.Controller.Books.IBookToEpubConverter, MulletaFlix.Server.Implementations.Books.CalibreBookConverter>();

            var serverUICulture = _serverConfigurationManager.Configuration.UICulture;
            if (string.IsNullOrEmpty(serverUICulture))
            {
                serverUICulture = "en-US";
            }

            CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo(serverUICulture);

            services.Configure<RequestLocalizationOptions>(options =>
            {
                var supportedUICultures = LocalizationManager.GetSupportedUICultures();
                options.SupportedCultures = supportedUICultures;
                options.SupportedUICultures = supportedUICultures;
                options.DefaultRequestCulture = new RequestCulture(serverUICulture);
                options.ApplyCurrentCultureToResponseHeaders = true;
                options.FallBackToParentCultures = true;
                options.FallBackToParentUICultures = true;
            });

            services.AddHostedService<RecordingsHost>();
            services.AddHostedService<AutoDiscoveryHost>();
            services.AddHostedService<PortMappingHost>();
            services.AddHostedService<NfoUserDataSaver>();
            services.AddHostedService<LibraryChangedNotifier>();
            services.AddHostedService<UserDataChangeNotifier>();
            services.AddHostedService<RecordingNotifier>();
            services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<MulletaFlixJobQueue>());
        }

        /// <summary>
        /// Configures the app builder for the webhost.
        /// </summary>
        /// <param name="app">The application builder.</param>
        /// <param name="env">The webhost environment.</param>
        /// <param name="appConfig">The application config.</param>
        public void Configure(
            IApplicationBuilder app,
            IWebHostEnvironment env,
            IConfiguration appConfig)
        {
            app.UseBaseUrlRedirection();

            // Wrap rest of configuration so everything only listens on BaseUrl.
            var config = _serverConfigurationManager.GetNetworkConfiguration();
            app.Map(config.BaseUrl, mainApp =>
            {
                if (env.IsDevelopment())
                {
                    mainApp.UseDeveloperExceptionPage();
                }

                mainApp.UseForwardedHeaders();
                mainApp.UseMiddleware<ExceptionMiddleware>();

                mainApp.UseMiddleware<SecurityHeadersMiddleware>();
                mainApp.UseMiddleware<ResponseTimeMiddleware>();

                if (config.RequireHttps && _serverApplicationHost.ListenWithHttps)
                {
                    mainApp.UseHsts();
                }

                mainApp.UseMiddleware<RateLimitMiddleware>();

                mainApp.UseWebSockets();

                mainApp.UseResponseCompression();

                mainApp.UseCors();

                mainApp.UseRequestLocalization();

                if (config.RequireHttps && _serverApplicationHost.ListenWithHttps)
                {
                    mainApp.UseHttpsRedirection();
                }

                if (appConfig.HostWebClient())
                {
                    var extensionProvider = new FileExtensionContentTypeProvider();

                    // subtitles octopus requires .data, .mem files.
                    extensionProvider.Mappings.Add(".data", MediaTypeNames.Application.Octet);
                    extensionProvider.Mappings.Add(".mem", MediaTypeNames.Application.Octet);
                    mainApp.UseDefaultFiles(new DefaultFilesOptions
                    {
                        FileProvider = new PhysicalFileProvider(_serverConfigurationManager.ApplicationPaths.WebPath),
                        RequestPath = "/web"
                    });
                    mainApp.Use(async (context, next) =>
                    {
                        if (context.Request.Path.StartsWithSegments("/web/assets", out var remainingPath)
                            && string.Equals(Path.GetExtension(remainingPath.Value), ".js", StringComparison.OrdinalIgnoreCase))
                        {
                            var relativeAssetPath = remainingPath.Value?.TrimStart('/', '\\') ?? string.Empty;
                            var assetPath = Path.GetFullPath(Path.Combine(_serverConfigurationManager.ApplicationPaths.WebPath, "assets", relativeAssetPath));
                            var assetsRoot = Path.GetFullPath(Path.Combine(_serverConfigurationManager.ApplicationPaths.WebPath, "assets"));

                            if (assetPath.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase)
                                && !File.Exists(assetPath))
                            {
                                context.Response.ContentType = "application/javascript; charset=utf-8";
                                context.Response.Headers.CacheControl = new StringValues("no-store, no-cache, must-revalidate");
                                await context.Response.WriteAsync("globalThis.location && globalThis.location.reload(); export default {};").ConfigureAwait(false);
                                return;
                            }
                        }

                        await next().ConfigureAwait(false);
                    });
                    mainApp.UseStaticFiles(new StaticFileOptions
                    {
                        FileProvider = new PhysicalFileProvider(_serverConfigurationManager.ApplicationPaths.WebPath),
                        RequestPath = "/web",
                        ContentTypeProvider = extensionProvider,
                        OnPrepareResponse = (context) =>
                        {
                            var extension = Path.GetExtension(context.File.Name);
                            if (string.Equals(extension, ".html", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(Path.GetFileName(context.File.Name), "manifest.json", StringComparison.OrdinalIgnoreCase))
                            {
                                context.Context.Response.Headers.CacheControl = new StringValues("no-store, no-cache, must-revalidate");
                                context.Context.Response.Headers.Pragma = new StringValues("no-cache");
                                context.Context.Response.Headers.Expires = new StringValues("0");
                            }
                            else if (string.Equals(extension, ".js", StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(extension, ".css", StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(extension, ".wasm", StringComparison.OrdinalIgnoreCase))
                            {
                                context.Context.Response.Headers.CacheControl = new StringValues("public, max-age=3600");
                            }
                        }
                    });

                    mainApp.UseRobotsRedirection();
                }

                mainApp.UseStaticFiles();
                mainApp.UseAuthentication();
                mainApp.UseMulletaFlixApiSwagger(_serverConfigurationManager);
                mainApp.UseQueryStringDecoding();
                mainApp.UseRouting();
                mainApp.UseAuthorization();

                mainApp.UseIPBasedAccessValidation();
                mainApp.UseWebSocketHandler();
                mainApp.UseServerStartupMessage();

                // Metrics stay enabled in the stage so the sprint gate can validate observability.
                mainApp.UseHttpMetrics();

                mainApp.UseEndpoints(endpoints =>
                {
                    endpoints.MapControllers();
                    endpoints.MapMetrics();

                    endpoints.MapHealthChecks("/health");
                });
            });
        }

        private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
            => HttpPolicyExtensions
                .HandleTransientHttpError()
                .Or<TimeoutRejectedException>()
                .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
    }
}

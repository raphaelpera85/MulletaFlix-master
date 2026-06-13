using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using MulletaFlix.Api.Attributes;
using MulletaFlix.Api.Models.AiMetadata;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Chapters;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.LiveTv;
using MulletaFlix.Data.Enums;

namespace MulletaFlix.Api.Controllers;

/// <summary>
/// AI metadata configuration controller.
/// </summary>
[Route("AiMetadata")]
[Authorize]
[Tags("AiMetadata")]
public class AiMetadataController : BaseMulletaFlixApiController
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly object ActivityLock = new();
    private static readonly object RunStateLock = new();
    private static readonly List<AiMetadataActivityItemDto> Activity = [];
    private static CancellationTokenSource? ActiveRunCancellation;
    private static string? ActiveRunActivityId;
    private static readonly Regex NoisePattern = new(
        @"(?i)(\s*\[(LEG|DUB|DUBLADO|LEGENDADO|PT-BR|BR)\]|\s*\((LEG|DUB|DUBLADO|LEGENDADO|PT-BR|BR)\)|\b1080p\b|\b720p\b|\b480p\b|\bWEB[- ]?DL\b|\bBluRay\b|\bBRRip\b|\bHDR\b|\bX264\b|\bX265\b|\bHEVC\b)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IServerConfigurationManager _configurationManager;
    private readonly IProviderManager _providerManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IChapterManager _chapterManager;
    private readonly ILiveTvManager _liveTvManager;
    private readonly IUserManager _userManager;
    private readonly IFileSystem _fileSystem;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDataProtector _dataProtector;

    /// <summary>
    /// Initializes a new instance of the <see cref="AiMetadataController"/> class.
    /// </summary>
    /// <param name="configurationManager">The server configuration manager.</param>
    /// <param name="providerManager">The provider manager.</param>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="chapterManager">The chapter manager.</param>
    /// <param name="liveTvManager">The live TV manager.</param>
    /// <param name="userManager">The user manager.</param>
    /// <param name="fileSystem">The file system.</param>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="dataProtectionProvider">The data protection provider.</param>
    public AiMetadataController(
        IServerConfigurationManager configurationManager,
        IProviderManager providerManager,
        ILibraryManager libraryManager,
        IChapterManager chapterManager,
        ILiveTvManager liveTvManager,
        IUserManager userManager,
        IFileSystem fileSystem,
        IHttpClientFactory httpClientFactory,
        IDataProtectionProvider dataProtectionProvider)
    {
        _configurationManager = configurationManager;
        _providerManager = providerManager;
        _libraryManager = libraryManager;
        _chapterManager = chapterManager;
        _liveTvManager = liveTvManager;
        _userManager = userManager;
        _fileSystem = fileSystem;
        _httpClientFactory = httpClientFactory;
        _dataProtector = dataProtectionProvider.CreateProtector("MulletaFlix.AiMetadata.ApiKeys.v1");
    }

    /// <summary>
    /// Gets AI metadata configuration.
    /// </summary>
    /// <response code="200">AI metadata configuration returned.</response>
    /// <returns>AI metadata configuration.</returns>
    [HttpGet("Configuration")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<AiMetadataConfigurationDto> GetConfiguration()
    {
        return ToDto(GetStoredConfiguration());
    }

    /// <summary>
    /// Updates AI metadata configuration.
    /// </summary>
    /// <param name="configuration">The AI metadata configuration.</param>
    /// <response code="204">AI metadata configuration updated.</response>
    /// <returns>Update status.</returns>
    [HttpPost("Configuration")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult UpdateConfiguration([FromBody, Required] AiMetadataConfigurationDto configuration)
    {
        var current = GetStoredConfiguration();
        var stored = FromDto(configuration, current);

        _configurationManager.SaveConfiguration(AiMetadataConfiguration.ConfigurationKey, stored);
        return NoContent();
    }

    /// <summary>
    /// Gets AI metadata activity.
    /// </summary>
    /// <response code="200">AI metadata activity returned.</response>
    /// <returns>Recent AI metadata activity.</returns>
    [HttpGet("Activity")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<AiMetadataActivityItemDto>> GetActivity()
    {
        lock (ActivityLock)
        {
            return Activity
                .OrderByDescending(item => item.CreatedAt)
                .Take(50)
                .Select(CloneActivity)
                .ToArray();
        }
    }

    /// <summary>
    /// Starts an AI metadata activity run.
    /// </summary>
    /// <param name="request">Run request.</param>
    /// <response code="200">AI metadata run queued.</response>
    /// <returns>Queued AI metadata activity.</returns>
    [HttpPost("Run")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [Consumes(MediaTypeNames.Application.Json)]
    public ActionResult<AiMetadataActivityItemDto> StartRun([FromBody] AiMetadataRunRequest? request)
    {
        var configuration = GetStoredConfiguration();
        var providers = configuration.Providers
            .Where(provider => provider.Enabled)
            .ToImmutableArray();
        var mediaTypes = GetEnabledMediaTypes(configuration).ToImmutableArray();
        var now = DateTimeOffset.UtcNow;
        CancellationTokenSource runCancellation;

        lock (RunStateLock)
        {
            if (ActiveRunCancellation is not null)
            {
                return Conflict(new
                {
                    message = "Ja existe uma execucao de IA em andamento."
                });
            }

            runCancellation = new CancellationTokenSource();
            ActiveRunCancellation = runCancellation;
        }

        var activity = new AiMetadataActivityItemDto
        {
            Id = Guid.NewGuid().ToString("N"),
            CreatedAt = now,
            UpdatedAt = now,
            Status = "Queued",
            Title = "Analise de IA e metadados",
            CurrentStep = "Aguardando inicio",
            Providers = providers.Select(provider => provider.DisplayName).ToArray(),
            MediaTypes = mediaTypes,
            Progress = 0,
            Summary = $"Escopo: {request?.Scope ?? "configured"}",
            Logs = [
                $"[{now:yyyy-MM-dd HH:mm:ss}] Execucao criada para validar provedores e preparar analise de metadados."
            ]
        };

        AddActivity(activity);
        lock (RunStateLock)
        {
            ActiveRunActivityId = activity.Id;
        }

        _ = Task.Run(() => RunAiMetadataActivityAsync(activity.Id, configuration, providers, mediaTypes, runCancellation.Token));

        return CloneActivity(activity);
    }

    /// <summary>
    /// Stops the active AI metadata activity run.
    /// </summary>
    /// <response code="200">Active AI metadata run cancelled.</response>
    /// <response code="404">No active AI metadata run was found.</response>
    [HttpPost("Stop")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<AiMetadataActivityItemDto> StopRun()
    {
        string? activityId;
        CancellationTokenSource? cancellationSource;

        lock (RunStateLock)
        {
            activityId = ActiveRunActivityId;
            cancellationSource = ActiveRunCancellation;

            if (activityId is null || cancellationSource is null)
            {
                return NotFound(new
                {
                    message = "Nao existe execucao de IA em andamento."
                });
            }
        }

        UpdateActivity(activityId, activity =>
        {
            activity.Status = "Stopping";
            activity.CurrentStep = "Cancelamento solicitado";
            activity.Summary = "A parada da execucao foi solicitada.";
        }, "Solicitacao de parada recebida. Cancelando execucao atual.");

        cancellationSource.Cancel();

        AiMetadataActivityItemDto? activity;
        lock (ActivityLock)
        {
            activity = Activity.FirstOrDefault(item => string.Equals(item.Id, activityId, StringComparison.OrdinalIgnoreCase));
        }

        return Ok(activity is not null
            ? CloneActivity(activity)
            : new AiMetadataActivityItemDto
            {
                Id = activityId,
                Status = "Stopping",
                CurrentStep = "Cancelamento solicitado",
                Summary = "A parada da execucao foi solicitada."
            });
    }

    /// <summary>
    /// Tests an AI metadata provider.
    /// </summary>
    /// <param name="request">Provider test request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">Provider test result returned.</response>
    /// <returns>Provider test result.</returns>
    [HttpPost("TestProvider")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [Consumes(MediaTypeNames.Application.Json)]
    public async Task<ActionResult<AiMetadataProviderTestResult>> TestProvider(
        [FromBody, Required] AiMetadataProviderTestRequest request,
        CancellationToken cancellationToken)
    {
        var provider = ResolveProviderForTest(request);
        if (provider is null)
        {
            return Ok(new AiMetadataProviderTestResult
            {
                Success = false,
                Message = "Provedor não encontrado."
            });
        }

        var validation = ValidateProvider(provider);
        if (validation is not null)
        {
            return Ok(validation);
        }

        if (IsLocalProvider(provider.Provider))
        {
            return await TestOllamaCompatibleProvider(provider, cancellationToken).ConfigureAwait(false);
        }

        return await TestOpenAiCompatibleProvider(provider, cancellationToken).ConfigureAwait(false);
    }

    private AiMetadataConfiguration GetStoredConfiguration()
    {
        return (AiMetadataConfiguration)_configurationManager.GetConfiguration(AiMetadataConfiguration.ConfigurationKey);
    }

    private async Task RunAiMetadataActivityAsync(
        string activityId,
        AiMetadataConfiguration configuration,
        IReadOnlyList<AiMetadataProviderConfiguration> providers,
        IReadOnlyList<string> mediaTypes,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!configuration.Enabled)
            {
                UpdateActivity(activityId, activity =>
                {
                    activity.Status = "Failed";
                    activity.CurrentStep = "IA desativada";
                    activity.Progress = 100;
                    activity.Summary = "Ative a curadoria de metadados por IA antes de executar.";
                }, "A IA nao esta ativada na configuracao.");
                return;
            }

            var items = await CollectCandidateItemsAsync(configuration, mediaTypes, cancellationToken).ConfigureAwait(false);
            UpdateActivity(activityId, activity =>
            {
                activity.Status = "Running";
                activity.CurrentStep = "Fila carregada";
                activity.Progress = 3;
                activity.Summary = $"{items.Count} itens elegiveis localizados para processamento.";
            }, $"{items.Count} itens foram localizados para analise.");

            if (items.Count == 0)
            {
                UpdateActivity(activityId, activity =>
                {
                    activity.Status = "Completed";
                    activity.CurrentStep = "Sem itens";
                    activity.Progress = 100;
                    activity.Summary = "Nenhum item elegivel foi encontrado para processar.";
                }, "Nenhum item precisou de analise.");
                return;
            }

            var applied = 0;
            var skipped = 0;
            var failed = 0;

            for (var index = 0; index < items.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var item = items[index];
                UpdateActivity(activityId, activity =>
                {
                    activity.CurrentStep = $"Analisando {item.TypeName}: {item.Name}";
                    activity.Progress = 5 + (int)Math.Round((index / (double)Math.Max(items.Count, 1)) * 90);
                }, $"[{item.TypeName}] {item.Name}");

                try
                {
                    var result = await ProcessItemAsync(item, configuration, providers, cancellationToken).ConfigureAwait(false);
                    if (result.Applied)
                    {
                        applied++;
                    }
                    else if (result.Skipped)
                    {
                        skipped++;
                    }
                    else
                    {
                        failed++;
                    }

                    UpdateActivity(activityId, _ => { }, result.LogMessage);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or UriFormatException)
                {
                    failed++;
                    UpdateActivity(activityId, _ => { }, $"[{item.TypeName}] falhou: {ex.Message}");
                }
            }

            UpdateActivity(activityId, activity =>
            {
                activity.Status = failed > 0 ? "Completed" : "Completed";
                activity.CurrentStep = "Concluido";
                activity.Progress = 100;
                activity.Summary = $"Processados {items.Count} itens. Aplicados: {applied}. Pulados: {skipped}. Falhas: {failed}.";
            }, $"Execucao concluida. Aplicados: {applied}; pulados: {skipped}; falhas: {failed}.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            UpdateActivity(activityId, activity =>
            {
                activity.Status = "Cancelled";
                activity.CurrentStep = "Cancelado";
                activity.Progress = 100;
                activity.Summary = "A execucao foi cancelada pelo usuario.";
            }, "Execucao cancelada pelo usuario.");
        }
        finally
        {
            lock (RunStateLock)
            {
                if (string.Equals(ActiveRunActivityId, activityId, StringComparison.OrdinalIgnoreCase))
                {
                    ActiveRunActivityId = null;
                }

                ActiveRunCancellation?.Dispose();
                ActiveRunCancellation = null;
            }
        }
    }

    private async Task<IReadOnlyList<AiMetadataWorkItem>> CollectCandidateItemsAsync(
        AiMetadataConfiguration configuration,
        IReadOnlyList<string> mediaTypes,
        CancellationToken cancellationToken)
    {
        var items = new List<AiMetadataWorkItem>();

        if (configuration.MediaTypes.Movies && mediaTypes.Contains("Filmes", StringComparer.OrdinalIgnoreCase))
        {
            items.AddRange(CollectUnidentifiedItems(BaseItemKind.Movie, "Filme"));
        }

        if (configuration.MediaTypes.Series && mediaTypes.Contains("Series", StringComparer.OrdinalIgnoreCase))
        {
            items.AddRange(CollectUnidentifiedItems(BaseItemKind.Series, "Serie"));
        }

        if (configuration.MediaTypes.Books && mediaTypes.Contains("Livros", StringComparer.OrdinalIgnoreCase))
        {
            items.AddRange(CollectUnidentifiedItems(BaseItemKind.Book, "Livro"));
        }

        if (configuration.MediaTypes.Channels && mediaTypes.Contains("Canais", StringComparer.OrdinalIgnoreCase))
        {
            items.AddRange(CollectUnidentifiedItems(BaseItemKind.LiveTvChannel, "Canal"));
        }

        return await Task.FromResult(items
            .GroupBy(item => item.Item.Id)
            .Select(group => group.First())
            .OrderBy(item => GetWorkItemPriority(item.TypeName))
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList());
    }

    private static int GetWorkItemPriority(string typeName)
    {
        return typeName switch
        {
            "Filme" => 0,
            "Serie" => 1,
            "Livro" => 2,
            "Canal" => 3,
            _ => 4
        };
    }

    private IEnumerable<AiMetadataWorkItem> CollectUnidentifiedItems(BaseItemKind itemKind, string typeName)
    {
        var items = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [itemKind],
            Recursive = true,
            IsVirtualItem = false,
            DtoOptions = new DtoOptions(false)
            {
                EnableImages = false
            }
        });

        foreach (var item in items)
        {
            if (ShouldProcessItem(item))
            {
                yield return new AiMetadataWorkItem(typeName, item);
            }
        }
    }

    private bool ShouldProcessItem(BaseItem item)
    {
        if (item is null)
        {
            return false;
        }

        if (item.IsLocked && GetStoredConfiguration().Automation.ProtectManualMetadata)
        {
            return false;
        }

        if (item.ProviderIds is not null && item.ProviderIds.Count > 0)
        {
            return false;
        }

        return LooksLikeUnidentifiedMedia(item);
    }

    private static bool LooksLikeUnidentifiedMedia(BaseItem item)
    {
        var name = item.Name ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            return true;
        }

        if (NoisePattern.IsMatch(name))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(item.Path))
        {
            return false;
        }

        TryParseCleanTitle(item.Path, out var parsedTitle, out _);
        if (string.IsNullOrWhiteSpace(parsedTitle))
        {
            return false;
        }

        var normalizedName = NormalizeDisplayTitle(name);
        var normalizedParsedTitle = NormalizeDisplayTitle(parsedTitle);
        return !string.Equals(normalizedName, normalizedParsedTitle, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryParseCleanTitle(string path, out string title, out int? year)
    {
        title = string.Empty;
        year = null;

        try
        {
            var filename = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(filename))
            {
                return;
            }

            filename = filename.Replace('.', ' ').Replace('_', ' ').Trim();

            var yearMatch = Regex.Match(filename, @"\b(19\d\d|20\d\d)\b");
            if (yearMatch.Success)
            {
                year = int.Parse(yearMatch.Value, CultureInfo.InvariantCulture);
                filename = filename[..yearMatch.Index].Trim();
            }

            var cleanPatterns = new[]
            {
                @"\b(1080[pi]|2160[pi]|720[pi]|480[pi]|576[pi])\b",
                @"\b(BluRay|Blu-ray|WEB-DL|WEBRip|HDRip|BRRip|DVDRip|DVD|HDTV|TS|CAM)\b",
                @"\b(x264|x265|h264|h265|HEVC|AVC|AV1|VP9)\b",
                @"\b(AAC|DTS|AC3|TRUEHD|FLAC|MP3|5\.1|7\.1|2\.0)\b",
                @"\b(IMAX|EXTENDED|UNCUT|UNRATED|DIRECTORS?[-\s]?CUT|THEATRICAL|REMUX|PROPER|REPACK|INTERNAL)\b",
                @"\b(3[Dd]|SBS|Half[-]?SBS|OU|Half[-]?OU)\b",
                @"\[.*?\]|\(.*?\)",
                @"\bS\d{1,2}(E\d{1,2})?\b"
            };

            foreach (var pattern in cleanPatterns)
            {
                filename = Regex.Replace(filename, pattern, " ", RegexOptions.IgnoreCase);
            }

            filename = Regex.Replace(filename, @"\s+", " ").Trim();

            if (filename.Length > 2)
            {
                var ci = CultureInfo.InvariantCulture;
                var ti = ci.TextInfo;
                filename = ti.ToTitleCase(filename.ToLower(ci));
            }

            title = filename;
        }
        catch
        {
            title = string.Empty;
            year = null;
        }
    }

    private static string NormalizeDisplayTitle(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var cleaned = Regex.Replace(value, @"[\p{P}\p{S}]+", " ", RegexOptions.CultureInvariant);
        cleaned = Regex.Replace(cleaned, @"\s{2,}", " ", RegexOptions.CultureInvariant).Trim();
        return cleaned;
    }

    private async Task<AiMetadataItemResult> ProcessItemAsync(
        AiMetadataWorkItem workItem,
        AiMetadataConfiguration configuration,
        IReadOnlyList<AiMetadataProviderConfiguration> providers,
        CancellationToken cancellationToken)
    {
        return workItem.Item switch
        {
            Movie movie => await ProcessMovieAsync(movie, configuration, providers, cancellationToken).ConfigureAwait(false),
            Series series => await ProcessSeriesAsync(series, configuration, providers, cancellationToken).ConfigureAwait(false),
            Book book => await ProcessBookAsync(book, configuration, providers, cancellationToken).ConfigureAwait(false),
            LiveTvChannel channel => await ProcessChannelAsync(channel, configuration, providers, cancellationToken).ConfigureAwait(false),
            _ => AiMetadataItemResult.CreateSkipped(workItem.TypeName, workItem.Item.Name, "Tipo de item nao suportado.")
        };
    }

    private async Task<AiMetadataItemResult> ProcessMovieAsync(
        Movie item,
        AiMetadataConfiguration configuration,
        IReadOnlyList<AiMetadataProviderConfiguration> providers,
        CancellationToken cancellationToken)
    {
        var normalized = await BuildConsensusNormalizationAsync(item, "Filme", providers, cancellationToken).ConfigureAwait(false);
        var lookupInfo = item.GetLookupInfo();
        lookupInfo.Name = normalized.NormalizedTitle;
        lookupInfo.OriginalTitle = normalized.OriginalTitle ?? lookupInfo.OriginalTitle;
        lookupInfo.Year = normalized.Year ?? lookupInfo.Year;
        lookupInfo.MetadataLanguage = item.GetPreferredMetadataLanguage();
        lookupInfo.MetadataCountryCode = item.GetPreferredMetadataCountryCode();

        var results = await _providerManager.GetRemoteSearchResults<Movie, MovieInfo>(
            new RemoteSearchQuery<MovieInfo>
            {
                ItemId = item.Id,
                SearchInfo = lookupInfo,
                IncludeDisabledProviders = false
            },
            cancellationToken).ConfigureAwait(false);

        var best = results
            .OrderByDescending(r => r.Score)
            .ThenByDescending(r => r.PremiereDate)
            .FirstOrDefault();

        if (best is null)
        {
            item.Name = normalized.NormalizedTitle;
            item.SortName = null;
            await item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
            return AiMetadataItemResult.CreateApplied(
                "Filme",
                item.Name,
                $"Titulo normalizado para '{normalized.NormalizedTitle}' e salvo sem correspondencia externa.",
                $"[{item.Name}] titulo normalizado para '{normalized.NormalizedTitle}' sem correspondencia remota.");
        }

        await ApplyRemoteSearchResultAsync(item, best, configuration, cancellationToken).ConfigureAwait(false);
        return AiMetadataItemResult.CreateApplied(
            "Filme",
            item.Name,
            $"Atualizado com '{best.Name}' (score {best.Score:0.0}).",
            $"[{item.Name}] atualizado para '{best.Name}' usando consenso e busca remota.");
    }

    private async Task<AiMetadataItemResult> ProcessSeriesAsync(
        Series item,
        AiMetadataConfiguration configuration,
        IReadOnlyList<AiMetadataProviderConfiguration> providers,
        CancellationToken cancellationToken)
    {
        var normalized = await BuildConsensusNormalizationAsync(item, "Serie", providers, cancellationToken).ConfigureAwait(false);
        var best = await SearchSeriesAsync(item, normalized, cancellationToken).ConfigureAwait(false);

        if (best is null)
        {
            item.Name = normalized.NormalizedTitle;
            item.SortName = null;
            await item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
            return AiMetadataItemResult.CreateApplied(
                "Serie",
                item.Name,
                $"Titulo normalizado para '{normalized.NormalizedTitle}' e salvo sem correspondencia externa.",
                $"[{item.Name}] titulo normalizado para '{normalized.NormalizedTitle}' sem correspondencia remota.");
        }

        await ApplyRemoteSearchResultAsync(item, best, configuration, cancellationToken).ConfigureAwait(false);
        return AiMetadataItemResult.CreateApplied(
            "Serie",
            item.Name,
            $"Atualizado com '{best.Name}' (score {best.Score:0.0}).",
            $"[{item.Name}] atualizado para '{best.Name}' usando consenso e busca remota.");
    }

    private async Task<RemoteSearchResult?> SearchSeriesAsync(
        Series item,
        AiMetadataNormalization normalized,
        CancellationToken cancellationToken)
    {
        var attempts = BuildSeriesSearchAttempts(item, normalized);

        foreach (var attempt in attempts)
        {
            var lookupInfo = item.GetLookupInfo();
            lookupInfo.Name = attempt.Title;
            lookupInfo.OriginalTitle = normalized.OriginalTitle ?? lookupInfo.OriginalTitle;
            lookupInfo.Year = attempt.Year;
            lookupInfo.MetadataLanguage = item.GetPreferredMetadataLanguage();
            lookupInfo.MetadataCountryCode = item.GetPreferredMetadataCountryCode();

            var results = await _providerManager.GetRemoteSearchResults<Series, SeriesInfo>(
                new RemoteSearchQuery<SeriesInfo>
                {
                    ItemId = item.Id,
                    SearchInfo = lookupInfo,
                    IncludeDisabledProviders = false
                },
                cancellationToken).ConfigureAwait(false);

            var best = results
                .OrderByDescending(r => r.Score)
                .ThenByDescending(r => r.PremiereDate)
                .FirstOrDefault();

            if (best is not null)
            {
                return best;
            }
        }

        return null;
    }

    private IEnumerable<(string Title, int? Year)> BuildSeriesSearchAttempts(Series item, AiMetadataNormalization normalized)
    {
        var title = string.IsNullOrWhiteSpace(normalized.NormalizedTitle)
            ? item.Name
            : normalized.NormalizedTitle;
        var year = normalized.Year ?? item.ProductionYear;

        yield return (title, year);
        yield return (RemoveSeriesNoiseTokens(title), year);
        yield return (title, null);
        yield return (RemoveSeriesNoiseTokens(title), null);

        if (!string.IsNullOrWhiteSpace(item.Path))
        {
            TryParseCleanTitle(item.Path, out var parsedTitle, out var parsedYear);
            if (!string.IsNullOrWhiteSpace(parsedTitle))
            {
                yield return (parsedTitle, parsedYear ?? year);
                yield return (RemoveSeriesNoiseTokens(parsedTitle), parsedYear ?? year);
                yield return (parsedTitle, null);
                yield return (RemoveSeriesNoiseTokens(parsedTitle), null);
            }
        }
    }

    private static string RemoveSeriesNoiseTokens(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var cleaned = Regex.Replace(value, @"(?i)\b(dorama|dorama|minis(?:érie|erie|eries)|miniserie|miniseries|miniss(?:érie|erie|eries)|youtube|web\s*series|webseries|webserie|episode|episodio|episódio|season|temporada|official|serie|série)\b", " ", RegexOptions.CultureInvariant);
        cleaned = Regex.Replace(cleaned, @"\s{2,}", " ", RegexOptions.CultureInvariant).Trim();
        return NormalizeDisplayTitle(cleaned);
    }

    private async Task<AiMetadataItemResult> ProcessBookAsync(
        Book item,
        AiMetadataConfiguration configuration,
        IReadOnlyList<AiMetadataProviderConfiguration> providers,
        CancellationToken cancellationToken)
    {
        var normalized = await BuildConsensusNormalizationAsync(item, "Livro", providers, cancellationToken).ConfigureAwait(false);
        var best = await SearchBookAsync(item, normalized, cancellationToken).ConfigureAwait(false);

        if (best is null)
        {
            item.Name = normalized.NormalizedTitle;
            item.SortName = null;
            await item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
            await RefreshBookMetadataAsync(item, cancellationToken).ConfigureAwait(false);
            return AiMetadataItemResult.CreateApplied(
                "Livro",
                item.Name,
                $"Titulo normalizado para '{normalized.NormalizedTitle}' e salvo sem correspondencia externa.",
                $"[{item.Name}] titulo normalizado para '{normalized.NormalizedTitle}' sem correspondencia remota.");
        }

        await ApplyRemoteSearchResultAsync(item, best, configuration, cancellationToken).ConfigureAwait(false);
        await EnsureBookChaptersAsync(item, cancellationToken).ConfigureAwait(false);
        return AiMetadataItemResult.CreateApplied(
            "Livro",
            item.Name,
            $"Atualizado com '{best.Name}' (score {best.Score:0.0}).",
            $"[{item.Name}] atualizado para '{best.Name}' usando consenso e busca remota.");
    }

    private async Task<RemoteSearchResult?> SearchBookAsync(
        Book item,
        AiMetadataNormalization normalized,
        CancellationToken cancellationToken)
    {
        var lookupInfo = item.GetLookupInfo();

        foreach (var attempt in BuildBookSearchAttempts(item, normalized))
        {
            lookupInfo.Name = attempt.Title;
            lookupInfo.OriginalTitle = normalized.OriginalTitle ?? lookupInfo.OriginalTitle;
            lookupInfo.Year = attempt.Year ?? normalized.Year ?? lookupInfo.Year;
            lookupInfo.MetadataLanguage = item.GetPreferredMetadataLanguage();
            lookupInfo.MetadataCountryCode = item.GetPreferredMetadataCountryCode();

            var results = await _providerManager.GetRemoteSearchResults<Book, BookInfo>(
                new RemoteSearchQuery<BookInfo>
                {
                    ItemId = item.Id,
                    SearchInfo = lookupInfo,
                    IncludeDisabledProviders = false
                },
                cancellationToken).ConfigureAwait(false);

            var best = results
                .OrderByDescending(r => r.Score)
                .ThenByDescending(r => r.PremiereDate)
                .FirstOrDefault();

            if (best is not null)
            {
                return best;
            }
        }

        return null;
    }

    private IEnumerable<(string Title, int? Year)> BuildBookSearchAttempts(Book item, AiMetadataNormalization normalized)
    {
        var title = string.IsNullOrWhiteSpace(normalized.NormalizedTitle)
            ? item.Name
            : normalized.NormalizedTitle;
        var year = normalized.Year ?? item.ProductionYear;

        yield return (title, year);
        yield return (title, null);

        if (!string.IsNullOrWhiteSpace(item.Path))
        {
            TryParseCleanTitle(item.Path, out var parsedTitle, out var parsedYear);
            if (!string.IsNullOrWhiteSpace(parsedTitle))
            {
                yield return (parsedTitle, parsedYear ?? year);
                yield return (parsedTitle, null);
            }
        }
    }

    private async Task RefreshBookMetadataAsync(Book item, CancellationToken cancellationToken)
    {
        var options = new MetadataRefreshOptions(new DirectoryService(_fileSystem))
        {
            MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
            ImageRefreshMode = MetadataRefreshMode.FullRefresh,
            ReplaceAllMetadata = true,
            ReplaceAllImages = true,
            RemoveOldMetadata = true,
            ForceSave = true,
            IsAutomated = true
        };

        await _providerManager.RefreshFullItem(item, options, cancellationToken).ConfigureAwait(false);
        await EnsureBookChaptersAsync(item, cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureBookChaptersAsync(Book item, CancellationToken cancellationToken)
    {
        var chapters = await BuildBookChaptersAsync(item, cancellationToken).ConfigureAwait(false);
        if (chapters.Count == 0)
        {
            return;
        }

        var currentRuntimeTicks = item.RunTimeTicks.GetValueOrDefault();
        var runtimeTicks = Math.Max(currentRuntimeTicks, TimeSpan.FromMinutes(chapters.Count + 1).Ticks);
        if (currentRuntimeTicks != runtimeTicks)
        {
            item.RunTimeTicks = runtimeTicks;
            await item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
        }

        _chapterManager.SaveChapters(item, chapters);
    }

    private async Task<IReadOnlyList<ChapterInfo>> BuildBookChaptersAsync(Book item, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(item.Path) || !string.Equals(Path.GetExtension(item.Path), ".epub", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        try
        {
            using var epub = ZipFile.OpenRead(item.Path);
            var opfFilePath = ReadEpubContentFilePath(epub);
            if (string.IsNullOrWhiteSpace(opfFilePath))
            {
                return [];
            }

            var opfEntry = epub.GetEntry(opfFilePath);
            if (opfEntry is null)
            {
                return [];
            }

            using var opfStream = opfEntry.Open();
            var opfDocument = new XmlDocument();
            opfDocument.Load(opfStream);

            var namespaceManager = new XmlNamespaceManager(opfDocument.NameTable);
            namespaceManager.AddNamespace("opf", "http://www.idpf.org/2007/opf");

            var manifestItems = opfDocument
                .SelectNodes("//opf:manifest/opf:item", namespaceManager)?
                .Cast<XmlElement>()
                .ToArray() ?? [];

            var spineItems = opfDocument
                .SelectNodes("//opf:spine/opf:itemref", namespaceManager)?
                .Cast<XmlElement>()
                .ToArray() ?? [];

            var opfRootDirectory = Path.GetDirectoryName(opfFilePath) ?? string.Empty;
            var chapters = new List<ChapterInfo>();

            foreach (var spineItem in spineItems)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var idref = spineItem.GetAttribute("idref");
                if (string.IsNullOrWhiteSpace(idref))
                {
                    continue;
                }

                var manifestItem = manifestItems.FirstOrDefault(itemNode =>
                    string.Equals(itemNode.GetAttribute("id"), idref, StringComparison.OrdinalIgnoreCase));
                if (manifestItem is null)
                {
                    continue;
                }

                var href = manifestItem.GetAttribute("href");
                if (string.IsNullOrWhiteSpace(href))
                {
                    continue;
                }

                var chapterName = await TryReadEpubSectionTitleAsync(epub, opfRootDirectory, href, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(chapterName))
                {
                    chapterName = Path.GetFileNameWithoutExtension(href);
                }

                chapters.Add(new ChapterInfo
                {
                    StartPositionTicks = chapters.Count == 0 ? 0 : TimeSpan.FromMinutes(chapters.Count).Ticks,
                    Name = NormalizeDisplayTitle(chapterName)
                });
            }

            return chapters;
        }
        catch
        {
            return [];
        }
    }

    private async Task<string?> TryReadEpubSectionTitleAsync(
        ZipArchive epub,
        string opfRootDirectory,
        string href,
        CancellationToken cancellationToken)
    {
        var entryPath = Path.Combine(opfRootDirectory, Uri.UnescapeDataString(href));
        var entry = epub.GetEntry(entryPath);
        if (entry is null)
        {
            return null;
        }

        var extension = Path.GetExtension(entry.FullName);
        if (string.Equals(extension, ".xhtml", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".html", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".htm", StringComparison.OrdinalIgnoreCase))
        {
            using var stream = entry.Open();
            var sectionDocument = new XmlDocument();
            try
            {
                sectionDocument.Load(stream);
                var titleNode = sectionDocument.SelectSingleNode("//*[local-name()='title']");
                if (!string.IsNullOrWhiteSpace(titleNode?.InnerText))
                {
                    return titleNode.InnerText.Trim();
                }

                var headingNode = sectionDocument.SelectSingleNode("//*[local-name()='h1' or local-name()='h2' or local-name()='h3']");
                if (!string.IsNullOrWhiteSpace(headingNode?.InnerText))
                {
                    return headingNode.InnerText.Trim();
                }
            }
            catch
            {
                // Fall back to the file name below.
            }
        }

        return await Task.FromResult(Path.GetFileNameWithoutExtension(entry.FullName));
    }

    private static string? ReadEpubContentFilePath(ZipArchive epub)
    {
        var container = epub.GetEntry(Path.Combine("META-INF", "container.xml"));
        if (container is null)
        {
            return null;
        }

        using var containerStream = container.Open();
        var containerDocument = XDocument.Load(containerStream);
        var containerNamespace = XNamespace.Get("urn:oasis:names:tc:opendocument:xmlns:container");
        var rootFile = containerDocument
            .Descendants(containerNamespace + "rootfile")
            .FirstOrDefault();

        return rootFile?.Attribute("full-path")?.Value;
    }

    private async Task<AiMetadataItemResult> ProcessChannelAsync(
        LiveTvChannel item,
        AiMetadataConfiguration configuration,
        IReadOnlyList<AiMetadataProviderConfiguration> providers,
        CancellationToken cancellationToken)
    {
        var normalized = await BuildConsensusNormalizationAsync(item, "Canal", providers, cancellationToken).ConfigureAwait(false);
        var newName = normalized.NormalizedTitle;

        if (!string.IsNullOrWhiteSpace(newName) && !string.Equals(newName, item.Name, StringComparison.Ordinal))
        {
            item.Name = newName;
            item.SortName = null;
            await item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
            return AiMetadataItemResult.CreateApplied(
                "Canal",
                item.Name,
                $"Nome do canal normalizado para '{newName}'.",
                $"[{item.Name}] nome normalizado para '{newName}'.");
        }

        return AiMetadataItemResult.CreateSkipped("Canal", item.Name, "Canal ja estava normalizado ou sem alteracao segura.");
    }

    private async Task ApplyRemoteSearchResultAsync(
        BaseItem item,
        RemoteSearchResult result,
        AiMetadataConfiguration configuration,
        CancellationToken cancellationToken)
    {
        item.ProviderIds = result.ProviderIds;

        var options = new MetadataRefreshOptions(new DirectoryService(_fileSystem))
        {
            MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
            ImageRefreshMode = configuration.Automation.AllowAutomaticApply ? MetadataRefreshMode.FullRefresh : MetadataRefreshMode.Default,
            ReplaceAllMetadata = true,
            ReplaceAllImages = true,
            RemoveOldMetadata = true,
            ForceSave = true,
            IsAutomated = true,
            SearchResult = result
        };

        await _providerManager.RefreshFullItem(item, options, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AiMetadataNormalization> BuildConsensusNormalizationAsync(
        BaseItem item,
        string itemTypeName,
        IReadOnlyList<AiMetadataProviderConfiguration> providers,
        CancellationToken cancellationToken)
    {
        var rawTitle = item.Name ?? string.Empty;
        var titleFromCode = NormalizeTitle(rawTitle);
        var year = item.ProductionYear;
        var suggestions = new List<AiMetadataNormalization>();

        foreach (var provider in providers.Where(provider => provider.Enabled))
        {
            try
            {
                var suggestion = await RequestNormalizationAsync(provider, item, itemTypeName, titleFromCode, year, cancellationToken).ConfigureAwait(false);
                if (suggestion is not null && !string.IsNullOrWhiteSpace(suggestion.NormalizedTitle))
                {
                    suggestions.Add(suggestion);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or UriFormatException)
            {
                _ = ex;
            }
        }

        if (suggestions.Count == 0)
        {
            return new AiMetadataNormalization
            {
                NormalizedTitle = titleFromCode,
                OriginalTitle = item.OriginalTitle,
                Year = year,
                Confidence = 0
            };
        }

        var topTitle = suggestions
            .GroupBy(suggestion => suggestion.NormalizedTitle, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => group.Max(entry => entry.Confidence))
            .First()
            .Key;

        var chosen = suggestions
            .Where(suggestion => string.Equals(suggestion.NormalizedTitle, topTitle, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(suggestion => suggestion.Confidence)
            .First();

        return chosen with
        {
            NormalizedTitle = topTitle,
            Year = suggestions.Where(suggestion => suggestion.Year.HasValue).Select(suggestion => suggestion.Year).GroupBy(value => value).OrderByDescending(group => group.Count()).Select(group => group.Key).FirstOrDefault() ?? chosen.Year
        };
    }

    private async Task<AiMetadataNormalization?> RequestNormalizationAsync(
        AiMetadataProviderConfiguration provider,
        BaseItem item,
        string itemTypeName,
        string normalizedTitle,
        int? year,
        CancellationToken cancellationToken)
    {
        var prompt = $$"""
            You are cleaning metadata for a media library.
            Return only valid JSON with this shape:
            {"normalizedTitle":"string","originalTitle":"string or empty","year":2022,"confidence":0-100,"reason":"short string","skip":false}

            Rules:
            - Remove noise such as LEG, DUB, resolution, codec, release-group tags and extra brackets.
            - Preserve the actual work title only.
            - If unsure, keep the safest title and set confidence lower.
            - If the item is not a real media work title, set skip=true.

            Item type: {{itemTypeName}}
            Raw title: {{item.Name}}
            Cleaned title guess: {{normalizedTitle}}
            Current year: {{(year?.ToString(CultureInfo.InvariantCulture) ?? string.Empty)}}
            Path: {{item.Path}}
            Original title: {{item.OriginalTitle}}
            """;

        var content = await CallProviderChatAsync(provider, prompt, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var payload = ExtractJsonObject(content);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        var dto = JsonSerializer.Deserialize<AiMetadataNormalizationDto>(payload, JsonOptions);
        if (dto is null || dto.Skip)
        {
            return null;
        }

        return new AiMetadataNormalization
        {
            NormalizedTitle = NormalizeTitle(dto.NormalizedTitle ?? normalizedTitle),
            OriginalTitle = string.IsNullOrWhiteSpace(dto.OriginalTitle) ? item.OriginalTitle : dto.OriginalTitle,
            Year = dto.Year ?? year,
            Confidence = Clamp(dto.Confidence, 0, 100),
            Reason = dto.Reason ?? string.Empty
        };
    }

    private async Task<string?> CallProviderChatAsync(
        AiMetadataProviderConfiguration provider,
        string prompt,
        CancellationToken cancellationToken)
    {
        var apiKey = UnprotectApiKey(provider.ProtectedApiKey);
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(40);
        client.BaseAddress = new Uri(NormalizeBaseUrl(provider.BaseUrl), UriKind.Absolute);

        if (!string.IsNullOrWhiteSpace(apiKey) && !IsLocalProvider(provider.Provider))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        var payload = new
        {
            model = provider.Model,
            messages = new object[]
            {
                new { role = "system", content = "You output strict JSON only." },
                new { role = "user", content = prompt }
            },
            temperature = 0,
            max_tokens = 256,
            stream = false
        };

        var endpoint = IsLocalProvider(provider.Provider) ? "api/chat" : "chat/completions";
        using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, MediaTypeNames.Application.Json);
        using var response = await client.PostAsync(endpoint, content, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException($"Falha ao consultar {provider.DisplayName}: {(int)response.StatusCode} {response.ReasonPhrase}. {TrimMessage(body)}");
        }

        var bodyText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(bodyText);
        if (document.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
        {
            var message = choices[0].GetProperty("message");
            if (message.TryGetProperty("content", out var contentElement))
            {
                return contentElement.GetString();
            }
        }

        if (document.RootElement.TryGetProperty("message", out var ollamaMessage)
            && ollamaMessage.TryGetProperty("content", out var ollamaContent))
        {
            return ollamaContent.GetString();
        }

        return bodyText;
    }

    private static string ExtractJsonObject(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var span = value.AsSpan();
        var start = span.IndexOf('{');
        var end = span.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return string.Empty;
        }

        return value.Substring(start, end - start + 1);
    }

    private static string NormalizeTitle(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var cleaned = NoisePattern.Replace(value, string.Empty);
        cleaned = Regex.Replace(cleaned, @"[\[\]\(\)\{\}]", string.Empty, RegexOptions.CultureInvariant);
        cleaned = Regex.Replace(cleaned, @"\s{2,}", " ", RegexOptions.CultureInvariant).Trim();
        cleaned = cleaned.Trim('-', ' ', '.');
        return cleaned;
    }

    private static void AddActivity(AiMetadataActivityItemDto activity)
    {
        lock (ActivityLock)
        {
            Activity.Add(activity);
            if (Activity.Count > 50)
            {
                Activity.RemoveRange(0, Activity.Count - 50);
            }
        }
    }

    private static void UpdateActivity(string activityId, Action<AiMetadataActivityItemDto> update, string? log = null)
    {
        lock (ActivityLock)
        {
            var activity = Activity.FirstOrDefault(item => string.Equals(item.Id, activityId, StringComparison.OrdinalIgnoreCase));
            if (activity is null)
            {
                return;
            }

            update(activity);
            activity.UpdatedAt = DateTimeOffset.UtcNow;

            if (!string.IsNullOrWhiteSpace(log))
            {
                activity.Logs = activity.Logs
                    .Concat([$"[{activity.UpdatedAt:yyyy-MM-dd HH:mm:ss}] {log}"])
                    .TakeLast(80)
                    .ToArray();
            }
        }
    }

    private static AiMetadataActivityItemDto CloneActivity(AiMetadataActivityItemDto activity)
    {
        return new AiMetadataActivityItemDto
        {
            Id = activity.Id,
            CreatedAt = activity.CreatedAt,
            UpdatedAt = activity.UpdatedAt,
            Status = activity.Status,
            Title = activity.Title,
            CurrentStep = activity.CurrentStep,
            Providers = activity.Providers.ToArray(),
            MediaTypes = activity.MediaTypes.ToArray(),
            Progress = activity.Progress,
            Summary = activity.Summary,
            Logs = activity.Logs.ToArray()
        };
    }

    private static IEnumerable<string> GetEnabledMediaTypes(AiMetadataConfiguration configuration)
    {
        if (configuration.MediaTypes.Movies) yield return "Filmes";
        if (configuration.MediaTypes.Series) yield return "Series";
        if (configuration.MediaTypes.Books) yield return "Livros";
        if (configuration.MediaTypes.Channels) yield return "Canais";
        if (configuration.MediaTypes.Logos) yield return "Logos";
        if (configuration.MediaTypes.Epg) yield return "EPG";
    }

    private AiMetadataProviderConfiguration? ResolveProviderForTest(AiMetadataProviderTestRequest request)
    {
        if (request.Provider is not null)
        {
            return FromDtoProvider(request.Provider, null);
        }

        if (string.IsNullOrWhiteSpace(request.ProviderId))
        {
            return null;
        }

        return GetStoredConfiguration().Providers.FirstOrDefault(
            provider => string.Equals(provider.Id, request.ProviderId, StringComparison.OrdinalIgnoreCase));
    }

    private AiMetadataProviderTestResult? ValidateProvider(AiMetadataProviderConfiguration provider)
    {
        if (string.IsNullOrWhiteSpace(provider.Provider))
        {
            return new AiMetadataProviderTestResult { Success = false, Message = "Selecione um provedor." };
        }

        if (string.IsNullOrWhiteSpace(provider.BaseUrl))
        {
            return new AiMetadataProviderTestResult { Success = false, Message = "Informe a URL base." };
        }

        if (string.IsNullOrWhiteSpace(provider.Model))
        {
            return new AiMetadataProviderTestResult { Success = false, Message = "Informe o modelo." };
        }

        if (!IsLocalProvider(provider.Provider) && string.IsNullOrWhiteSpace(UnprotectApiKey(provider.ProtectedApiKey)))
        {
            return new AiMetadataProviderTestResult { Success = false, Message = "Informe a API key." };
        }

        return null;
    }

    private async Task<AiMetadataProviderTestResult> TestOpenAiCompatibleProvider(
        AiMetadataProviderConfiguration provider,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(20);
            client.BaseAddress = new Uri(NormalizeBaseUrl(provider.BaseUrl), UriKind.Absolute);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UnprotectApiKey(provider.ProtectedApiKey));

            var payload = new
            {
                model = provider.Model,
                messages = new[]
                {
                    new { role = "system", content = "Responda apenas OK." },
                    new { role = "user", content = "Teste de conexão MulletaFlix." }
                },
                max_tokens = 4,
                temperature = 0
            };

            using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, MediaTypeNames.Application.Json);
            using var response = await client.PostAsync("chat/completions", content, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return new AiMetadataProviderTestResult { Success = true, Message = "Conexão validada com sucesso." };
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return new AiMetadataProviderTestResult
            {
                Success = false,
                Message = $"Falha no provedor: {(int)response.StatusCode} {response.ReasonPhrase}. {TrimMessage(body)}"
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            return new AiMetadataProviderTestResult
            {
                Success = false,
                Message = $"Falha ao testar provedor: {ex.Message}"
            };
        }
    }

    private async Task<AiMetadataProviderTestResult> TestOllamaCompatibleProvider(
        AiMetadataProviderConfiguration provider,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);
            client.BaseAddress = new Uri(NormalizeBaseUrl(provider.BaseUrl), UriKind.Absolute);

            using var response = await client.GetAsync("api/tags", cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return new AiMetadataProviderTestResult { Success = true, Message = "Servidor local respondeu com sucesso." };
            }

            return new AiMetadataProviderTestResult
            {
                Success = false,
                Message = $"Servidor local respondeu {(int)response.StatusCode} {response.ReasonPhrase}."
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            return new AiMetadataProviderTestResult
            {
                Success = false,
                Message = $"Falha ao testar provedor local: {ex.Message}"
            };
        }
    }

    private AiMetadataConfigurationDto ToDto(AiMetadataConfiguration configuration)
    {
        return new AiMetadataConfigurationDto
        {
            Enabled = configuration.Enabled,
            DecisionMode = configuration.DecisionMode,
            PrimaryProviderId = configuration.PrimaryProviderId,
            JudgeProviderId = configuration.JudgeProviderId,
            Providers = configuration.Providers.Select(provider => new AiMetadataProviderDto
            {
                Id = provider.Id,
                Provider = provider.Provider,
                DisplayName = provider.DisplayName,
                Enabled = provider.Enabled,
                BaseUrl = provider.BaseUrl,
                Model = provider.Model,
                ApiKeyConfigured = !string.IsNullOrWhiteSpace(provider.ProtectedApiKey)
            }).ToArray(),
            Automation = new AiMetadataAutomationDto
            {
                MinimumSuggestionConfidence = configuration.Automation.MinimumSuggestionConfidence,
                AutomaticApplyConfidence = configuration.Automation.AutomaticApplyConfidence,
                AllowAutomaticApply = configuration.Automation.AllowAutomaticApply,
                RequireTwoProviderAgreement = configuration.Automation.RequireTwoProviderAgreement,
                ProtectExistingEpg = configuration.Automation.ProtectExistingEpg,
                ExistingEpgReplaceConfidence = configuration.Automation.ExistingEpgReplaceConfidence,
                ProtectManualMetadata = configuration.Automation.ProtectManualMetadata
            },
            MediaTypes = new AiMetadataMediaTypesDto
            {
                Movies = configuration.MediaTypes.Movies,
                Series = configuration.MediaTypes.Series,
                Books = configuration.MediaTypes.Books,
                Channels = configuration.MediaTypes.Channels,
                Logos = configuration.MediaTypes.Logos,
                Epg = configuration.MediaTypes.Epg
            }
        };
    }

    private AiMetadataConfiguration FromDto(AiMetadataConfigurationDto dto, AiMetadataConfiguration current)
    {
        return new AiMetadataConfiguration
        {
            Enabled = dto.Enabled,
            DecisionMode = string.IsNullOrWhiteSpace(dto.DecisionMode) ? "single" : dto.DecisionMode,
            PrimaryProviderId = dto.PrimaryProviderId,
            JudgeProviderId = dto.JudgeProviderId,
            Providers = dto.Providers.Select(provider =>
            {
                var currentProvider = current.Providers.FirstOrDefault(saved =>
                    string.Equals(saved.Id, provider.Id, StringComparison.OrdinalIgnoreCase));
                return FromDtoProvider(provider, currentProvider);
            }).ToArray(),
            Automation = new AiMetadataAutomationConfiguration
            {
                MinimumSuggestionConfidence = Clamp(dto.Automation.MinimumSuggestionConfidence, 0, 100),
                AutomaticApplyConfidence = Clamp(dto.Automation.AutomaticApplyConfidence, 0, 100),
                AllowAutomaticApply = dto.Automation.AllowAutomaticApply,
                RequireTwoProviderAgreement = dto.Automation.RequireTwoProviderAgreement,
                ProtectExistingEpg = dto.Automation.ProtectExistingEpg,
                ExistingEpgReplaceConfidence = Clamp(dto.Automation.ExistingEpgReplaceConfidence, 0, 100),
                ProtectManualMetadata = dto.Automation.ProtectManualMetadata
            },
            MediaTypes = new AiMetadataMediaTypesConfiguration
            {
                Movies = dto.MediaTypes.Movies,
                Series = dto.MediaTypes.Series,
                Books = dto.MediaTypes.Books,
                Channels = dto.MediaTypes.Channels,
                Logos = dto.MediaTypes.Logos,
                Epg = dto.MediaTypes.Epg
            }
        };
    }

    private AiMetadataProviderConfiguration FromDtoProvider(
        AiMetadataProviderDto dto,
        AiMetadataProviderConfiguration? currentProvider)
    {
        var apiKey = dto.ClearApiKey
            ? string.Empty
            : !string.IsNullOrWhiteSpace(dto.ApiKey)
                ? _dataProtector.Protect(dto.ApiKey)
                : currentProvider?.ProtectedApiKey ?? string.Empty;

        return new AiMetadataProviderConfiguration
        {
            Id = string.IsNullOrWhiteSpace(dto.Id) ? Guid.NewGuid().ToString("N") : dto.Id,
            Provider = dto.Provider,
            DisplayName = string.IsNullOrWhiteSpace(dto.DisplayName) ? dto.Provider : dto.DisplayName,
            Enabled = dto.Enabled,
            BaseUrl = NormalizeConfiguredBaseUrl(dto.Provider, dto.BaseUrl),
            Model = dto.Model,
            ProtectedApiKey = apiKey
        };
    }

    private string UnprotectApiKey(string protectedApiKey)
    {
        if (string.IsNullOrWhiteSpace(protectedApiKey))
        {
            return string.Empty;
        }

        try
        {
            return _dataProtector.Unprotect(protectedApiKey);
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException)
        {
            return string.Empty;
        }
    }

    private static bool IsLocalProvider(string provider)
    {
        return string.Equals(provider, "Ollama", StringComparison.OrdinalIgnoreCase)
            || string.Equals(provider, "LMStudio", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeConfiguredBaseUrl(string provider, string baseUrl)
    {
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            return baseUrl.Trim();
        }

        return provider.ToUpperInvariant() switch
        {
            "OPENAI" => "https://api.openai.com/v1",
            "OPENROUTER" => "https://openrouter.ai/api/v1",
            "DEEPSEEK" => "https://api.deepseek.com",
            "GEMINI" => "https://generativelanguage.googleapis.com/v1beta/openai",
            "ANTHROPIC" => "https://api.anthropic.com/v1",
            "AZUREOPENAI" => string.Empty,
            "OLLAMA" => "http://localhost:11434",
            "LMSTUDIO" => "http://localhost:1234/v1",
            _ => baseUrl.Trim()
        };
    }

    private static string NormalizeBaseUrl(string baseUrl)
    {
        return baseUrl.Trim().TrimEnd('/') + "/";
    }

    private static int Clamp(int value, int minimum, int maximum)
    {
        return Math.Min(Math.Max(value, minimum), maximum);
    }

    private static string TrimMessage(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Length <= 300 ? value : value[..300];
    }
}

internal sealed record AiMetadataWorkItem(string TypeName, BaseItem Item)
{
    public string Name => Item.Name ?? string.Empty;
}

internal sealed record AiMetadataNormalization
{
    public string NormalizedTitle { get; init; } = string.Empty;

    public string? OriginalTitle { get; init; }

    public int? Year { get; init; }

    public int Confidence { get; init; }

    public string Reason { get; init; } = string.Empty;
}

internal sealed record AiMetadataItemResult
{
    public bool Applied { get; init; }

    public bool Skipped { get; init; }

    public string TypeName { get; init; } = string.Empty;

    public string ItemName { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public string LogMessage { get; init; } = string.Empty;

    public static AiMetadataItemResult CreateApplied(string typeName, string itemName, string message, string logMessage)
    {
        return new AiMetadataItemResult
        {
            Applied = true,
            TypeName = typeName,
            ItemName = itemName,
            Message = message,
            LogMessage = logMessage
        };
    }

    public static AiMetadataItemResult CreateSkipped(string typeName, string itemName, string message)
    {
        return new AiMetadataItemResult
        {
            Skipped = true,
            TypeName = typeName,
            ItemName = itemName,
            Message = message,
            LogMessage = $"[{typeName}] {itemName}: {message}"
        };
    }
}

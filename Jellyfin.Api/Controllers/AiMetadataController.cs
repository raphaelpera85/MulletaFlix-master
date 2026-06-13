using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MulletaFlix.Api.Attributes;
using MulletaFlix.Api.Models.AiMetadata;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Model.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

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
    private static readonly List<AiMetadataActivityItemDto> Activity = [];

    private readonly IServerConfigurationManager _configurationManager;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDataProtector _dataProtector;

    /// <summary>
    /// Initializes a new instance of the <see cref="AiMetadataController"/> class.
    /// </summary>
    /// <param name="configurationManager">The server configuration manager.</param>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="dataProtectionProvider">The data protection provider.</param>
    public AiMetadataController(
        IServerConfigurationManager configurationManager,
        IHttpClientFactory httpClientFactory,
        IDataProtectionProvider dataProtectionProvider)
    {
        _configurationManager = configurationManager;
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
        _ = Task.Run(() => RunAiMetadataActivityAsync(activity.Id, configuration, providers, mediaTypes, CancellationToken.None));

        return CloneActivity(activity);
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

        if (providers.Count == 0)
        {
            UpdateActivity(activityId, activity =>
            {
                activity.Status = "Failed";
                activity.CurrentStep = "Nenhum provedor ativo";
                activity.Progress = 100;
                activity.Summary = "Adicione e ative pelo menos uma IA.";
            }, "Nenhum provedor ativo foi encontrado.");
            return;
        }

        if (mediaTypes.Count == 0)
        {
            UpdateActivity(activityId, activity =>
            {
                activity.Status = "Failed";
                activity.CurrentStep = "Nenhum tipo de midia ativo";
                activity.Progress = 100;
                activity.Summary = "Selecione pelo menos um tipo de midia para processar.";
            }, "Nenhum tipo de midia foi selecionado.");
            return;
        }

        UpdateActivity(activityId, activity =>
        {
            activity.Status = "Running";
            activity.CurrentStep = "Validando provedores";
            activity.Progress = 5;
            activity.Summary = "IAs em execucao. Validando conectividade e preparando consenso.";
        }, $"Modo de decisao: {configuration.DecisionMode}. Provedores ativos: {providers.Count}.");

        var providerResults = new List<AiMetadataProviderTestResult>();
        for (var index = 0; index < providers.Count; index++)
        {
            var provider = providers[index];
            UpdateActivity(activityId, activity =>
            {
                activity.CurrentStep = $"Testando {provider.DisplayName}";
                activity.Progress = 10 + (index * 40 / Math.Max(providers.Count, 1));
            }, $"Testando provedor {provider.DisplayName} ({provider.Provider}/{provider.Model}).");

            var result = IsLocalProvider(provider.Provider)
                ? await TestOllamaCompatibleProvider(provider, cancellationToken).ConfigureAwait(false)
                : await TestOpenAiCompatibleProvider(provider, cancellationToken).ConfigureAwait(false);
            providerResults.Add(result);

            UpdateActivity(activityId, _ => { }, $"{provider.DisplayName}: {result.Message}");
        }

        var successfulProviders = providerResults.Count(result => result.Success);
        if (successfulProviders == 0)
        {
            UpdateActivity(activityId, activity =>
            {
                activity.Status = "Failed";
                activity.CurrentStep = "Nenhuma IA respondeu";
                activity.Progress = 100;
                activity.Summary = "Nenhum provedor ativo respondeu com sucesso. Verifique API keys, URL base e modelo.";
            }, "A execucao foi interrompida porque nenhum provedor respondeu com sucesso.");
            return;
        }

        for (var index = 0; index < mediaTypes.Count; index++)
        {
            var mediaType = mediaTypes[index];
            UpdateActivity(activityId, activity =>
            {
                activity.CurrentStep = $"Preparando analise de {mediaType}";
                activity.Progress = 55 + (index * 35 / Math.Max(mediaTypes.Count, 1));
            }, $"Fila preparada para {mediaType}: limpar titulo, buscar correspondencias, comparar logos/EPG e gerar sugestoes.");

            await Task.Delay(TimeSpan.FromMilliseconds(350), cancellationToken).ConfigureAwait(false);
        }

        UpdateActivity(activityId, activity =>
        {
            activity.Status = "Completed";
            activity.CurrentStep = "Concluido";
            activity.Progress = 100;
            activity.Summary = $"{successfulProviders}/{providers.Count} provedores responderam. Proxima etapa: ligar esta fila aos itens reais da biblioteca e aplicar sugestoes com auditoria.";
        }, "Execucao concluida. Nenhum metadado foi alterado automaticamente nesta etapa segura.");
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

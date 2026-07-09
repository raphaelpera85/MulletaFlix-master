#nullable disable
#pragma warning disable CS1591

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Emby.Server.Implementations.MediaEncoding
{
    /// <summary>
    /// Service that detects hardware capabilities on every server startup
    /// and configures encoding options for the best possible playback settings.
    /// Prioridade de deteccao: NVIDIA > AMD > Intel > Software (CPU).
    /// </summary>
    public class HardwareDetectionService
    {
        private readonly ILogger<HardwareDetectionService> _logger;
        private readonly IServerConfigurationManager _configurationManager;
        private readonly IMediaEncoder _mediaEncoder;
        private readonly IApplicationPaths _appPaths;

        // Codecs de decodificacao por tipo de GPU
        private static readonly string[] NvencDecodingCodecs = ["h264", "hevc", "mpeg2video", "mpeg4"];
        private static readonly string[] QsvDecodingCodecs = ["h264", "hevc", "mpeg2video", "av1"];
        private static readonly string[] AmfDecodingCodecs = ["h264", "hevc", "mpeg2video", "av1"];
        private static readonly string[] VaapiDecodingCodecs = ["h264", "hevc", "mpeg2video", "av1"];

        // Presets otimizados por fabricante de GPU
        private const string PresetNvidiaFast = "fast";
        private const string PresetNvidiaMedium = "medium";
        private const string PresetAmfSpeed = "speed";
        private const string PresetAmfQuality = "quality";
        private const string PresetQsvFast = "fast";
        private const string PresetQsvMedium = "medium";
        private const string PresetSoftware = "medium";

        // CRF otimizado para hardware encoding (valores maiores = mais compressao, menos qualidade)
        private const int H264CrfHardware = 24;
        private const int H265CrfHardware = 30;
        private const int H264CrfSoftware = 23;
        private const int H265CrfSoftware = 28;

        public HardwareDetectionService(
            ILogger<HardwareDetectionService> logger,
            IServerConfigurationManager configurationManager,
            IMediaEncoder mediaEncoder,
            IApplicationPaths appPaths)
        {
            _logger = logger;
            _configurationManager = configurationManager;
            _mediaEncoder = mediaEncoder;
            _appPaths = appPaths;
        }

        public void Run()
        {
            _logger.LogInformation("=== INICIANDO DETECCAO AUTOMATICA DE HARDWARE ===");

            try
            {
                var encodingOptions = _configurationManager.GetEncodingOptions();
                var changes = DetectAndApplyConfiguration(encodingOptions);

                if (changes > 0)
                {
                    _configurationManager.SaveConfiguration("encoding", encodingOptions);
                    _logger.LogInformation(
                        "Deteccao de hardware concluida: {Changes} configuracao(oes) foram atualizadas.",
                        changes);
                }
                else
                {
                    _logger.LogInformation(
                        "Deteccao de hardware concluida: Nenhuma alteracao necessaria. Configuracao ja esta otimizada.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro durante deteccao automatica de hardware para transcodificacao.");
            }

            _logger.LogInformation("=== DETECCAO DE HARDWARE FINALIZADA ===");
        }

        private int DetectAndApplyConfiguration(EncodingOptions options)
        {
            var changes = 0;

            // =========================================================
            // FASE 1: Detectar GPUs disponiveis no sistema
            // =========================================================
            var gpuList = GetVideoControllerNames();
            _logger.LogInformation("GPUs detectadas no sistema: {Count}", gpuList.Length);
            foreach (var gpu in gpuList)
            {
                _logger.LogInformation("  GPU: {Gpu}", gpu);
            }

            // =========================================================
            // FASE 2: Verificar suporte real do FFmpeg para cada fabricante
            // =========================================================
            var ffmpegSupportsNvenc = _mediaEncoder.SupportsEncoder("h264_nvenc");
            var ffmpegSupportsHevcNvenc = _mediaEncoder.SupportsEncoder("hevc_nvenc");
            var ffmpegSupportsAv1Nvenc = _mediaEncoder.SupportsEncoder("av1_nvenc");

            var ffmpegSupportsAmfH264 = _mediaEncoder.SupportsEncoder("h264_amf");
            var ffmpegSupportsAmfHevc = _mediaEncoder.SupportsEncoder("hevc_amf");
            var ffmpegSupportsAmfAv1 = _mediaEncoder.SupportsEncoder("av1_amf");

            var ffmpegSupportsQsvH264 = _mediaEncoder.SupportsEncoder("h264_qsv");
            var ffmpegSupportsQsvHevc = _mediaEncoder.SupportsEncoder("hevc_qsv");
            var ffmpegSupportsQsvAv1 = _mediaEncoder.SupportsEncoder("av1_qsv");

            // Tambem verifica encoders alternativos Intel
            var ffmpegSupportsQsvMpeg2 = _mediaEncoder.SupportsEncoder("mpeg2_qsv");
            var ffmpegSupportsQsvVc1 = _mediaEncoder.SupportsEncoder("vc1_qsv");
            var ffmpegSupportsQsvVp9 = _mediaEncoder.SupportsEncoder("vp9_qsv");

            _logger.LogInformation(
                "Suporte FFmpeg NVIDIA NVENC: H264={H264}, HEVC={Hevc}, AV1={Av1}",
                ffmpegSupportsNvenc,
                ffmpegSupportsHevcNvenc,
                ffmpegSupportsAv1Nvenc);
            _logger.LogInformation(
                "Suporte FFmpeg AMD AMF: H264={H264}, HEVC={Hevc}, AV1={Av1}",
                ffmpegSupportsAmfH264,
                ffmpegSupportsAmfHevc,
                ffmpegSupportsAmfAv1);
            _logger.LogInformation(
                "Suporte FFmpeg Intel QSV: H264={H264}, HEVC={Hevc}, AV1={Av1}",
                ffmpegSupportsQsvH264,
                ffmpegSupportsQsvHevc,
                ffmpegSupportsQsvAv1);

            // =========================================================
            // FASE 3: Determinar qual fabricante de GPU esta presente
            // =========================================================
            var hasNvidiaGpu = gpuList.Any(name =>
                ContainsAny(name, "NVIDIA", "GeForce", "RTX", "GTX", "Quadro", "TITAN", "Tesla"));

            var hasAmdGpu = gpuList.Any(name =>
                ContainsAny(name, "AMD", "Radeon", "Advanced Micro Devices", "780M", "680M", "880M"));

            var hasIntelGpu = gpuList.Any(name =>
                ContainsAny(name, "Intel", "Arc", "UHD Graphics", "HD Graphics", "Iris", "Iris Xe"));

            _logger.LogInformation(
                "Classificacao das GPUs detectadas: NVIDIA={Nvidia}, AMD={Amd}, Intel={Intel}",
                hasNvidiaGpu,
                hasAmdGpu,
                hasIntelGpu);

            // =========================================================
            // FASE 4: Determinar o tipo de aceleracao a usar
            // =========================================================
            // Regra de decisao:
            // 1. Se tem GPU NVIDIA E FFmpeg suporta nvenc -> usa nvenc
            // 2. Se tem GPU AMD E FFmpeg suporta amf -> usa amf
            // 3. Se tem GPU Intel E FFmpeg suporta qsv -> usa qsv
            // 4. Se FFmpeg suporta algum encoder de hardware mesmo sem GPU detectada -> usa o que funcionar
            // 5. Caso contrario -> none (software)

            HardwareAccelerationType actualHwType;
            string hwTypeReason;

            if (hasNvidiaGpu && ffmpegSupportsNvenc)
            {
                actualHwType = HardwareAccelerationType.nvenc;
                hwTypeReason = "GPU NVIDIA detectada com suporte NVENC confirmado pelo FFmpeg";
                _logger.LogInformation(
                    "DECISAO: Usando NVIDIA NVENC - {Reason}. GPU: {Gpu}",
                    hwTypeReason,
                    gpuList.FirstOrDefault(g => ContainsAny(g, "NVIDIA", "GeForce", "RTX", "GTX", "Quadro", "TITAN", "Tesla")) ?? "NVIDIA");
            }
            else if (hasAmdGpu && ffmpegSupportsAmfH264)
            {
                actualHwType = HardwareAccelerationType.amf;
                hwTypeReason = "GPU AMD detectada com suporte AMF confirmado pelo FFmpeg";
                _logger.LogInformation(
                    "DECISAO: Usando AMD AMF - {Reason}. GPU: {Gpu}",
                    hwTypeReason,
                    gpuList.FirstOrDefault(g => ContainsAny(g, "AMD", "Radeon", "Advanced Micro Devices")) ?? "AMD");
            }
            else if (hasIntelGpu && ffmpegSupportsQsvH264)
            {
                actualHwType = HardwareAccelerationType.qsv;
                hwTypeReason = "GPU Intel detectada com suporte QSV confirmado pelo FFmpeg";
                _logger.LogInformation(
                    "DECISAO: Usando Intel QSV - {Reason}. GPU: {Gpu}",
                    hwTypeReason,
                    gpuList.FirstOrDefault(g => ContainsAny(g, "Intel", "Arc")) ?? "Intel");
            }
            else if (ffmpegSupportsNvenc)
            {
                actualHwType = HardwareAccelerationType.nvenc;
                hwTypeReason = "FFmpeg possui suporte NVENC (possivel GPU NVIDIA nao listada via WMI)";
                _logger.LogInformation("DECISAO: Usando NVIDIA NVENC (fallback FFmpeg) - {Reason}", hwTypeReason);
            }
            else if (ffmpegSupportsAmfH264)
            {
                actualHwType = HardwareAccelerationType.amf;
                hwTypeReason = "FFmpeg possui suporte AMF (possivel GPU AMD nao listada via WMI)";
                _logger.LogInformation("DECISAO: Usando AMD AMF (fallback FFmpeg) - {Reason}", hwTypeReason);
            }
            else if (ffmpegSupportsQsvH264)
            {
                actualHwType = HardwareAccelerationType.qsv;
                hwTypeReason = "FFmpeg possui suporte QSV (possivel GPU Intel nao listada via WMI)";
                _logger.LogInformation("DECISAO: Usando Intel QSV (fallback FFmpeg) - {Reason}", hwTypeReason);
            }
            else
            {
                actualHwType = HardwareAccelerationType.none;
                hwTypeReason = "Nenhum encoder de hardware suportado pelo FFmpeg encontrado";
                _logger.LogInformation(
                    "DECISAO: Usando transcodificacao por SOFTWARE (CPU) - {Reason}",
                    hwTypeReason);
            }

            // =========================================================
            // FASE 5: Determinar codecs avancados suportados via FFmpeg
            // =========================================================
            var hevcSupported = actualHwType switch
            {
                HardwareAccelerationType.nvenc => ffmpegSupportsHevcNvenc,
                HardwareAccelerationType.amf => ffmpegSupportsAmfHevc,
                HardwareAccelerationType.qsv => ffmpegSupportsQsvHevc,
                _ => false
            };

            var av1Supported = actualHwType switch
            {
                HardwareAccelerationType.nvenc => ffmpegSupportsAv1Nvenc,
                HardwareAccelerationType.amf => ffmpegSupportsAmfAv1,
                HardwareAccelerationType.qsv => ffmpegSupportsQsvAv1,
                _ => false
            };

            var intelLowPowerSupported = actualHwType == HardwareAccelerationType.qsv;

            _logger.LogInformation(
                "Codecs confirmados via FFmpeg: HEVC={Hevc}, AV1={Av1}, IntelLowPower={Ilp}",
                hevcSupported,
                av1Supported,
                intelLowPowerSupported);

            // =========================================================
            // FASE 6: Aplicar configuracoes
            // =========================================================

            // 6a. Tipo de aceleracao de hardware
            if (options.HardwareAccelerationType != actualHwType)
            {
                var oldType = options.HardwareAccelerationType;
                options.HardwareAccelerationType = actualHwType;
                changes++;
                _logger.LogInformation(
                    "CONFIG: HardwareAccelerationType alterado de {Old} para {New} - {Reason}",
                    oldType,
                    actualHwType,
                    hwTypeReason);
            }
            else
            {
                _logger.LogDebug(
                    "CONFIG: HardwareAccelerationType ja esta como {Type} (sem alteracoes)",
                    actualHwType);
            }

            if (actualHwType != HardwareAccelerationType.none)
            {
                // =========================================================
                // --- SUB-FASE 6a: Aceleracao de hardware ATIVADA ---
                // =========================================================

                // Codecs de decodificacao especificos por tipo de GPU
                var targetCodecs = GetDecodingCodecsForHardware(actualHwType);
                if (options.HardwareDecodingCodecs is null
                    || !options.HardwareDecodingCodecs.SequenceEqual(targetCodecs, StringComparer.OrdinalIgnoreCase))
                {
                    options.HardwareDecodingCodecs = targetCodecs;
                    changes++;
                    _logger.LogInformation(
                        "CONFIG: HardwareDecodingCodecs configurados para {HwType}: {Codecs}",
                        actualHwType,
                        string.Join(", ", targetCodecs));
                }

                // EnableHardwareEncoding
                if (ApplySetting(
                    !options.EnableHardwareEncoding,
                    () => options.EnableHardwareEncoding = true,
                    "CONFIG: EnableHardwareEncoding ativado (hardware disponivel)"))
                    changes++;

                // =========================================================
                // SUB-FASE 6b: EncoderPreset especifico por fabricante
                // =========================================================
                var targetPreset = actualHwType switch
                {
                    HardwareAccelerationType.nvenc => EncoderPreset.fast,
                    HardwareAccelerationType.amf => EncoderPreset.medium,
                    HardwareAccelerationType.qsv => EncoderPreset.fast,
                    _ => EncoderPreset.medium
                };
                if (ApplySetting(
                    options.EncoderPreset != targetPreset,
                    () => options.EncoderPreset = targetPreset,
                    $"CONFIG: EncoderPreset configurado para {targetPreset} ({actualHwType})"))
                    changes++;

                // =========================================================
                // SUB-FASE 6c: CRF otimizado (HW usa valores levemente maiores)
                // =========================================================
                if (ApplySetting(
                    options.H264Crf != H264CrfHardware,
                    () => options.H264Crf = H264CrfHardware,
                    $"CONFIG: H264Crf ajustado para {H264CrfHardware} (hardware encoding)"))
                    changes++;
                if (ApplySetting(
                    options.H265Crf != H265CrfHardware,
                    () => options.H265Crf = H265CrfHardware,
                    $"CONFIG: H265Crf ajustado para {H265CrfHardware} (hardware encoding)"))
                    changes++;

                // =========================================================
                // SUB-FASE 6d: Tonemapping (HDR->SDR) - configuracao completa
                // =========================================================
                if (ApplySetting(
                    !options.EnableTonemapping,
                    () => options.EnableTonemapping = true,
                    "CONFIG: EnableTonemapping ativado (HDR->SDR)"))
                    changes++;
                if (ApplySetting(
                    options.EnableVppTonemapping,
                    () => options.EnableVppTonemapping = false,
                    null))
                    changes++;
                if (ApplySetting(
                    options.EnableVideoToolboxTonemapping,
                    () => options.EnableVideoToolboxTonemapping = false,
                    null))
                    changes++;

                // TonemappingAlgorithm: bt2390 (padrao/recomendado) funciona bem em todas as GPUs
                if (ApplySetting(
                    options.TonemappingAlgorithm != TonemappingAlgorithm.bt2390,
                    () => options.TonemappingAlgorithm = TonemappingAlgorithm.bt2390,
                    "CONFIG: TonemappingAlgorithm configurado para bt2390"))
                    changes++;

                // TonemappingMode: auto deixa o FFmpeg decidir (max/rgb/luminance)
                if (ApplySetting(
                    options.TonemappingMode != TonemappingMode.auto,
                    () => options.TonemappingMode = TonemappingMode.auto,
                    "CONFIG: TonemappingMode configurado para auto"))
                    changes++;

                // TonemappingRange: auto permite que o FFmpeg detecte automaticamente
                if (ApplySetting(
                    options.TonemappingRange != TonemappingRange.auto,
                    () => options.TonemappingRange = TonemappingRange.auto,
                    null))
                    changes++;

                // TonemappingDesat: 0 = desativado (evita perda de cor em HDR)
                if (ApplySetting(
                    options.TonemappingDesat != 0,
                    () => options.TonemappingDesat = 0,
                    null))
                    changes++;

                // TonemappingPeak: 100 = luminancia peak padrao (nit)
                if (ApplySetting(
                    options.TonemappingPeak != 100,
                    () => options.TonemappingPeak = 100,
                    null))
                    changes++;

                // TonemappingParam: 0 = parametro padrao (deixa o algoritmo decidir)
                if (ApplySetting(
                    options.TonemappingParam != 0,
                    () => options.TonemappingParam = 0,
                    null))
                    changes++;

                // VPP Tonemapping brightness/contrast (mantem defaults seguros)
                if (ApplySetting(
                    options.VppTonemappingBrightness != 16,
                    () => options.VppTonemappingBrightness = 16,
                    null))
                    changes++;
                if (ApplySetting(
                    options.VppTonemappingContrast != 1,
                    () => options.VppTonemappingContrast = 1,
                    null))
                    changes++;

                // =========================================================
                // SUB-FASE 6e: Decodificacao 10-bit e 12-bit
                // =========================================================
                if (ApplySetting(
                    !options.EnableDecodingColorDepth10Hevc,
                    () => options.EnableDecodingColorDepth10Hevc = true,
                    null))
                    changes++;
                if (ApplySetting(
                    !options.EnableDecodingColorDepth10Vp9,
                    () => options.EnableDecodingColorDepth10Vp9 = true,
                    null))
                    changes++;
                if (ApplySetting(
                    !options.EnableDecodingColorDepth10HevcRext,
                    () => options.EnableDecodingColorDepth10HevcRext = true,
                    null))
                    changes++;
                if (ApplySetting(
                    !options.EnableDecodingColorDepth12HevcRext,
                    () => options.EnableDecodingColorDepth12HevcRext = true,
                    null))
                    changes++;

                // =========================================================
                // SUB-FASE 6f: Configuracoes especificas por fabricante
                // =========================================================

                // NVIDIA: EnableEnhancedNvdecDecoder
                var shouldUseEnhancedNvdec = actualHwType == HardwareAccelerationType.nvenc;
                if (ApplySetting(
                    options.EnableEnhancedNvdecDecoder != shouldUseEnhancedNvdec,
                    () => options.EnableEnhancedNvdecDecoder = shouldUseEnhancedNvdec,
                    shouldUseEnhancedNvdec
                        ? "CONFIG: EnableEnhancedNvdecDecoder ativado (NVIDIA - necessario para HDR, Dolby Vision)"
                        : "CONFIG: EnableEnhancedNvdecDecoder desativado (nao NVIDIA)"))
                    changes++;

                // PreferSystemNativeHwDecoder: ativar para NVIDIA e Intel, desativar para AMD
                var shouldPreferNative = actualHwType is HardwareAccelerationType.nvenc or HardwareAccelerationType.qsv;
                if (ApplySetting(
                    options.PreferSystemNativeHwDecoder != shouldPreferNative,
                    () => options.PreferSystemNativeHwDecoder = shouldPreferNative,
                    shouldPreferNative
                        ? "CONFIG: PreferSystemNativeHwDecoder ativado (NVIDIA/Intel - melhor compatibilidade)"
                        : "CONFIG: PreferSystemNativeHwDecoder desativado (AMD - usa AMF)"))
                    changes++;

                // Intel Low Power: ativar apenas para QSV
                if (ApplySetting(
                    options.EnableIntelLowPowerH264HwEncoder != intelLowPowerSupported,
                    () => options.EnableIntelLowPowerH264HwEncoder = intelLowPowerSupported,
                    intelLowPowerSupported
                        ? "CONFIG: EnableIntelLowPowerH264HwEncoder ativado (Intel QSV - baixo consumo)"
                        : "CONFIG: EnableIntelLowPowerH264HwEncoder desativado (nao Intel)"))
                    changes++;

                if (ApplySetting(
                    options.EnableIntelLowPowerHevcHwEncoder != intelLowPowerSupported,
                    () => options.EnableIntelLowPowerHevcHwEncoder = intelLowPowerSupported,
                    intelLowPowerSupported
                        ? "CONFIG: EnableIntelLowPowerHevcHwEncoder ativado (Intel QSV - baixo consumo)"
                        : "CONFIG: EnableIntelLowPowerHevcHwEncoder desativado (nao Intel)"))
                    changes++;

                // HEVC Encoding
                if (ApplySetting(
                    options.AllowHevcEncoding != hevcSupported,
                    () => options.AllowHevcEncoding = hevcSupported,
                    hevcSupported
                        ? "CONFIG: AllowHevcEncoding ativado (FFmpeg confirmou suporte)"
                        : "CONFIG: AllowHevcEncoding desativado (FFmpeg NAO suporta)"))
                    changes++;

                // AV1 Encoding - DESATIVADO (causa falha de reproducao em diversas midias)
                if (ApplySetting(
                    options.AllowAv1Encoding,
                    () => options.AllowAv1Encoding = false,
                    "CONFIG: AllowAv1Encoding DESATIVADO (compativelidade com playback)"))
                    changes++;

                // =========================================================
                // SUB-FASE 6g: Dispositivo especifico (QSV, VAAPI)
                // =========================================================
                if (actualHwType == HardwareAccelerationType.qsv)
                {
                    // QSV: usar dispositivo padrao ou auto-detectado
                    if (ApplySetting(
                        !string.IsNullOrEmpty(options.QsvDevice),
                        () => options.QsvDevice = string.Empty,
                        "CONFIG: QsvDevice configurado para auto-detecção (vazio)"))
                        changes++;
                }

                // =========================================================
                // SUB-FASE 6h: Desentrelacamento - yadif com double rate
                // =========================================================
                if (ApplySetting(
                    options.DeinterlaceMethod != DeinterlaceMethod.yadif,
                    () => options.DeinterlaceMethod = DeinterlaceMethod.yadif,
                    "CONFIG: DeinterlaceMethod configurado para yadif"))
                    changes++;
                if (ApplySetting(
                    !options.DeinterlaceDoubleRate,
                    () => options.DeinterlaceDoubleRate = true,
                    "CONFIG: DeinterlaceDoubleRate ativado (yadif x2)"))
                    changes++;
            }
            else
            {
                // =========================================================
                // --- SUB-FASE 6a: Apenas CPU, sem aceleracao de hardware ---
                // =========================================================

                if (ApplySetting(
                    options.EnableHardwareEncoding,
                    () => options.EnableHardwareEncoding = false,
                    "CONFIG: EnableHardwareEncoding desativado (apenas CPU)"))
                    changes++;

                // EncoderPreset: medium para CPU (bom equilibrio qualidade/desempenho)
                if (ApplySetting(
                    options.EncoderPreset != EncoderPreset.medium,
                    () => options.EncoderPreset = EncoderPreset.medium,
                    "CONFIG: EncoderPreset configurado para medium (CPU)"))
                    changes++;

                // CRF: valores padrao para CPU (mais qualidade que HW)
                if (ApplySetting(
                    options.H264Crf != H264CrfSoftware,
                    () => options.H264Crf = H264CrfSoftware,
                    $"CONFIG: H264Crf ajustado para {H264CrfSoftware} (CPU encoding)"))
                    changes++;
                if (ApplySetting(
                    options.H265Crf != H265CrfSoftware,
                    () => options.H265Crf = H265CrfSoftware,
                    $"CONFIG: H265Crf ajustado para {H265CrfSoftware} (CPU encoding)"))
                    changes++;

                // Tonemapping: desligado para CPU (muito pesado)
                if (ApplySetting(
                    options.EnableTonemapping,
                    () => options.EnableTonemapping = false,
                    null))
                    changes++;
                if (ApplySetting(
                    options.EnableVppTonemapping,
                    () => options.EnableVppTonemapping = false,
                    null))
                    changes++;
                if (ApplySetting(
                    options.EnableVideoToolboxTonemapping,
                    () => options.EnableVideoToolboxTonemapping = false,
                    null))
                    changes++;

                // Desabilitar tudo especifico de GPU
                if (ApplySetting(
                    options.EnableEnhancedNvdecDecoder,
                    () => options.EnableEnhancedNvdecDecoder = false,
                    null))
                    changes++;
                if (ApplySetting(
                    options.PreferSystemNativeHwDecoder,
                    () => options.PreferSystemNativeHwDecoder = false,
                    null))
                    changes++;
                if (ApplySetting(
                    options.EnableIntelLowPowerH264HwEncoder,
                    () => options.EnableIntelLowPowerH264HwEncoder = false,
                    null))
                    changes++;
                if (ApplySetting(
                    options.EnableIntelLowPowerHevcHwEncoder,
                    () => options.EnableIntelLowPowerHevcHwEncoder = false,
                    null))
                    changes++;
                if (ApplySetting(
                    options.AllowHevcEncoding,
                    () => options.AllowHevcEncoding = false,
                    null))
                    changes++;
                if (ApplySetting(
                    options.AllowAv1Encoding,
                    () => options.AllowAv1Encoding = false,
                    null))
                    changes++;

                // Decodificacao 10-bit ainda deve ficar ativa mesmo sem HW encoding
                if (ApplySetting(
                    !options.EnableDecodingColorDepth10Hevc,
                    () => options.EnableDecodingColorDepth10Hevc = true,
                    null))
                    changes++;
                if (ApplySetting(
                    !options.EnableDecodingColorDepth10Vp9,
                    () => options.EnableDecodingColorDepth10Vp9 = true,
                    null))
                    changes++;
                if (ApplySetting(
                    !options.EnableDecodingColorDepth10HevcRext,
                    () => options.EnableDecodingColorDepth10HevcRext = true,
                    null))
                    changes++;
                if (ApplySetting(
                    !options.EnableDecodingColorDepth12HevcRext,
                    () => options.EnableDecodingColorDepth12HevcRext = true,
                    null))
                    changes++;

                // Desentrelacamento: yadif mesmo em CPU (leve o suficiente)
                if (ApplySetting(
                    options.DeinterlaceMethod != DeinterlaceMethod.yadif,
                    () => options.DeinterlaceMethod = DeinterlaceMethod.yadif,
                    null))
                    changes++;
                if (ApplySetting(
                    !options.DeinterlaceDoubleRate,
                    () => options.DeinterlaceDoubleRate = true,
                    null))
                    changes++;
            }

            // =========================================================
            // FASE 7: Configuracoes comuns (aplicam-se a todos os cenarios)
            // =========================================================

            // Legendas
            if (ApplySetting(
                !options.EnableSubtitleExtraction,
                () => options.EnableSubtitleExtraction = true,
                "CONFIG: EnableSubtitleExtraction ativado"))
                changes++;
            if (ApplySetting(
                options.SubtitleExtractionTimeoutMinutes != 30,
                () => options.SubtitleExtractionTimeoutMinutes = 30,
                "CONFIG: SubtitleExtractionTimeoutMinutes ajustado para 30 minutos"))
                changes++;

            // Desentrelacamento
            if (ApplySetting(
                !options.DeinterlaceDoubleRate,
                () => options.DeinterlaceDoubleRate = true,
                "CONFIG: DeinterlaceDoubleRate ativado"))
                changes++;

            // Fonte fallback para legendas
            if (ApplySetting(
                !options.EnableFallbackFont,
                () => options.EnableFallbackFont = true,
                "CONFIG: EnableFallbackFont ativado"))
                changes++;

            // Audio VBR
            if (ApplySetting(
                !options.EnableAudioVbr,
                () => options.EnableAudioVbr = true,
                "CONFIG: EnableAudioVbr ativado"))
                changes++;

            return changes;
        }

        private bool ApplySetting(
            bool condition,
            Action applyAction,
            string logMessage)
        {
            if (!condition)
            {
                return false;
            }

            applyAction();

            if (logMessage is not null)
            {
                _logger.LogInformation("{Message}", logMessage);
            }

            return true;
        }

        private static string[] GetDecodingCodecsForHardware(HardwareAccelerationType hwType)
        {
            return hwType switch
            {
                HardwareAccelerationType.nvenc => NvencDecodingCodecs,
                HardwareAccelerationType.qsv => QsvDecodingCodecs,
                HardwareAccelerationType.amf => AmfDecodingCodecs,
                HardwareAccelerationType.vaapi => VaapiDecodingCodecs,
                _ => NvencDecodingCodecs
            };
        }

        private static string[] GetVideoControllerNames()
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
#if NET6_0_OR_GREATER
                    ArgumentList =
                    {
                        "-NoProfile",
                        "-Command",
                        "Get-CimInstance Win32_VideoController | Select-Object Name,AdapterCompatibility | Format-Table -HideTableHeaders"
                    },
#else
                    Arguments = "-NoProfile -Command \"Get-CimInstance Win32_VideoController | Select-Object Name,AdapterCompatibility | Format-Table -HideTableHeaders\"",
#endif
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process is null)
                {
                    return Array.Empty<string>();
                }

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    return Array.Empty<string>();
                }

                var lines = output
                    .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(line => !string.IsNullOrWhiteSpace(line) && line.Length > 3)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                // Se o Format-Table nao funcionar, tenta apenas o nome
                if (lines.Length == 0)
                {
                    var fallbackStartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
#if NET6_0_OR_GREATER
                        ArgumentList =
                        {
                            "-NoProfile",
                            "-Command",
                            "Get-CimInstance Win32_VideoController | Select-Object -ExpandProperty Name"
                        },
#else
                        Arguments = "-NoProfile -Command \"Get-CimInstance Win32_VideoController | Select-Object -ExpandProperty Name\"",
#endif
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var fallbackProcess = Process.Start(fallbackStartInfo);
                    if (fallbackProcess is null)
                    {
                        return Array.Empty<string>();
                    }

                    var fallbackOutput = fallbackProcess.StandardOutput.ReadToEnd();
                    fallbackProcess.WaitForExit();

                    if (fallbackProcess.ExitCode != 0)
                    {
                        return Array.Empty<string>();
                    }

                    return fallbackOutput
                        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                }

                return lines;
            }
            catch (Exception)
            {
                return Array.Empty<string>();
            }
        }

        private static bool ContainsAny(string value, params string[] needles)
        {
            foreach (var needle in needles)
            {
                if (value.Contains(needle, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Serialization;
using Emby.Server.Implementations;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace MulletaFlix.Server.Migrations.PreStartupRoutines;

/// <inheritdoc />
#pragma warning disable CS0618 // Type or member is obsolete
[MulletaFlixMigration("2026-06-06T05:00:00", nameof(ConfigureTranscodingFromHardware), "D3D9E8F6-6F90-48A1-9F54-2B7E40E9D1B2", Stage = Stages.MulletaFlixMigrationStageTypes.PreInitialisation)]
public sealed class ConfigureTranscodingFromHardware : IMigrationRoutine
#pragma warning restore CS0618 // Type or member is obsolete
{
    private static readonly string[] DefaultHardwareDecodingCodecs =
    [
        "h264",
        "vc1",
        "hevc",
        "mpeg4",
        "mpeg2video",
        "vp8",
        "vp9",
        "av1"
    ];

    private readonly ServerApplicationPaths _applicationPaths;
    private readonly ILogger<ConfigureTranscodingFromHardware> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigureTranscodingFromHardware"/> class.
    /// </summary>
    /// <param name="applicationPaths">An instance of <see cref="ServerApplicationPaths"/>.</param>
    /// <param name="loggerFactory">An instance of the <see cref="ILoggerFactory"/> interface.</param>
    public ConfigureTranscodingFromHardware(ServerApplicationPaths applicationPaths, ILoggerFactory loggerFactory)
    {
        _applicationPaths = applicationPaths;
        _logger = loggerFactory.CreateLogger<ConfigureTranscodingFromHardware>();
    }

    /// <inheritdoc />
    public void Perform()
    {
        if (!OperatingSystem.IsWindows())
        {
            _logger.LogDebug("Skipping transcoding hardware detection on non-Windows startup.");
            return;
        }

        var encodingPath = Path.Combine(_applicationPaths.ConfigurationDirectoryPath, "encoding.xml");
        if (!File.Exists(encodingPath))
        {
            _logger.LogDebug("Encoding configuration file not found, skipping hardware-aware transcoding setup: {Path}", encodingPath);
            return;
        }

        var hardware = DetectHardware();
        if (hardware is null)
        {
            _logger.LogWarning("No GPU or CPU information was detected, leaving transcoding configuration unchanged.");
            return;
        }

        EncodingOptions? encodingOptions;
        try
        {
            var serializer = new XmlSerializer(typeof(EncodingOptions));
            using var xmlReader = XmlReader.Create(encodingPath);
            encodingOptions = (EncodingOptions?)serializer.Deserialize(xmlReader);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read transcoding configuration from {Path}", encodingPath);
            return;
        }

        if (encodingOptions is null)
        {
            _logger.LogWarning("Encoding configuration could not be loaded from {Path}", encodingPath);
            return;
        }

        var changed = ApplyRecommendedProfile(encodingOptions, hardware.Value);
        if (!changed)
        {
            if (hardware.Value.IsGpu)
            {
                _logger.LogInformation(
                    "GPU detected: {Name}. Transcoding configuration already matches the recommended profile: {Profile}",
                    hardware.Value.Name,
                    hardware.Value.HardwareAccelerationType);
            }
            else
            {
                _logger.LogInformation(
                    "CPU detected: {Name}. Transcoding configuration already matches the recommended profile: software",
                    hardware.Value.Name);
            }

            return;
        }

        try
        {
            var serializer = new XmlSerializer(typeof(EncodingOptions));
            var xmlWriterSettings = new XmlWriterSettings
            {
                Indent = true
            };

            using var xmlWriter = XmlWriter.Create(encodingPath, xmlWriterSettings);
            serializer.Serialize(xmlWriter, encodingOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write transcoding configuration to {Path}", encodingPath);
            return;
        }

        if (hardware.Value.IsGpu)
        {
            _logger.LogInformation(
                "GPU detected: {Name}. Transcoding profile updated to {Profile}.",
                hardware.Value.Name,
                hardware.Value.HardwareAccelerationType);
        }
        else
        {
            _logger.LogInformation(
                "CPU detected: {Name}. Transcoding profile updated for software fallback.",
                hardware.Value.Name);
        }
    }

    private static HardwareDetection? DetectHardware()
    {
        var gpuNames = GetVideoControllerNames();
        if (gpuNames.Length > 0)
        {
            var gpuName = gpuNames[0];
            var gpuProfile = DetectHardwareAccelerationType(gpuNames);
            return new HardwareDetection(gpuName, true, gpuProfile, SupportsAv1Encoding(gpuProfile, gpuNames));
        }

        var cpuName = GetProcessorName();
        if (!string.IsNullOrWhiteSpace(cpuName))
        {
            return new HardwareDetection(cpuName, false, HardwareAccelerationType.none, false);
        }

        return null;
    }

    private static HardwareAccelerationType DetectHardwareAccelerationType(string[] controllerNames)
    {
        foreach (var controllerName in controllerNames)
        {
            if (ContainsAny(controllerName, "NVIDIA", "GeForce", "RTX", "GTX"))
            {
                return HardwareAccelerationType.nvenc;
            }

            if (ContainsAny(controllerName, "AMD", "Radeon", "Advanced Micro Devices"))
            {
                return HardwareAccelerationType.amf;
            }

            if (ContainsAny(controllerName, "Intel", "Arc"))
            {
                return HardwareAccelerationType.qsv;
            }
        }

        return HardwareAccelerationType.none;
    }

    private static string[] GetVideoControllerNames()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -Command \"Get-CimInstance Win32_VideoController | Select-Object -ExpandProperty Name\"",
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
            _ = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                return Array.Empty<string>();
            }

            return output
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    private static string? GetProcessorName()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -Command \"Get-CimInstance Win32_Processor | Select-Object -First 1 -ExpandProperty Name\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            _ = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                return null;
            }

            return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool ApplyRecommendedProfile(EncodingOptions encodingOptions, HardwareDetection hardware)
    {
        var changed = false;

        if (SetIfDifferent(encodingOptions.HardwareAccelerationType, hardware.HardwareAccelerationType, value => encodingOptions.HardwareAccelerationType = value))
        {
            changed = true;
        }

        if (hardware.IsGpu)
        {
            if (SetIfDifferent(encodingOptions.EnableHardwareEncoding, true, value => encodingOptions.EnableHardwareEncoding = value))
            {
                changed = true;
            }

            if (SetIfDifferent(encodingOptions.EnableTonemapping, true, value => encodingOptions.EnableTonemapping = value))
            {
                changed = true;
            }

            if (SetIfDifferent(encodingOptions.EnableVppTonemapping, false, value => encodingOptions.EnableVppTonemapping = value))
            {
                changed = true;
            }

            if (SetIfDifferent(encodingOptions.EnableVideoToolboxTonemapping, false, value => encodingOptions.EnableVideoToolboxTonemapping = value))
            {
                changed = true;
            }

            if (SetIfDifferent(encodingOptions.EnableDecodingColorDepth10Hevc, true, value => encodingOptions.EnableDecodingColorDepth10Hevc = value))
            {
                changed = true;
            }

            if (SetIfDifferent(encodingOptions.EnableDecodingColorDepth10Vp9, true, value => encodingOptions.EnableDecodingColorDepth10Vp9 = value))
            {
                changed = true;
            }

            if (SetIfDifferent(encodingOptions.EnableDecodingColorDepth10HevcRext, true, value => encodingOptions.EnableDecodingColorDepth10HevcRext = value))
            {
                changed = true;
            }

            if (SetIfDifferent(encodingOptions.EnableDecodingColorDepth12HevcRext, true, value => encodingOptions.EnableDecodingColorDepth12HevcRext = value))
            {
                changed = true;
            }

            if (SetIfDifferent(encodingOptions.EnableEnhancedNvdecDecoder, hardware.HardwareAccelerationType == HardwareAccelerationType.nvenc, value => encodingOptions.EnableEnhancedNvdecDecoder = value))
            {
                changed = true;
            }

            if (SetIfDifferent(
                    encodingOptions.PreferSystemNativeHwDecoder,
                    hardware.HardwareAccelerationType is HardwareAccelerationType.qsv or HardwareAccelerationType.nvenc,
                    value => encodingOptions.PreferSystemNativeHwDecoder = value))
            {
                changed = true;
            }

            if (SetIfDifferent(encodingOptions.EnableIntelLowPowerH264HwEncoder, false, value => encodingOptions.EnableIntelLowPowerH264HwEncoder = value))
            {
                changed = true;
            }

            if (SetIfDifferent(encodingOptions.EnableIntelLowPowerHevcHwEncoder, false, value => encodingOptions.EnableIntelLowPowerHevcHwEncoder = value))
            {
                changed = true;
            }

            if (SetIfDifferent(encodingOptions.AllowHevcEncoding, true, value => encodingOptions.AllowHevcEncoding = value))
            {
                changed = true;
            }

            if (SetIfDifferent(encodingOptions.AllowAv1Encoding, hardware.SupportsAv1Encoding, value => encodingOptions.AllowAv1Encoding = value))
            {
                changed = true;
            }
        }
        else
        {
            if (SetIfDifferent(encodingOptions.EnableHardwareEncoding, false, value => encodingOptions.EnableHardwareEncoding = value))
            {
                changed = true;
            }

            if (SetIfDifferent(encodingOptions.EnableTonemapping, false, value => encodingOptions.EnableTonemapping = value))
            {
                changed = true;
            }

            if (SetIfDifferent(encodingOptions.EnableVppTonemapping, false, value => encodingOptions.EnableVppTonemapping = value))
            {
                changed = true;
            }

            if (SetIfDifferent(encodingOptions.EnableVideoToolboxTonemapping, false, value => encodingOptions.EnableVideoToolboxTonemapping = value))
            {
                changed = true;
            }

            if (SetIfDifferent(encodingOptions.EnableEnhancedNvdecDecoder, false, value => encodingOptions.EnableEnhancedNvdecDecoder = value))
            {
                changed = true;
            }

            if (SetIfDifferent(encodingOptions.PreferSystemNativeHwDecoder, false, value => encodingOptions.PreferSystemNativeHwDecoder = value))
            {
                changed = true;
            }

            if (SetIfDifferent(encodingOptions.EnableIntelLowPowerH264HwEncoder, false, value => encodingOptions.EnableIntelLowPowerH264HwEncoder = value))
            {
                changed = true;
            }

            if (SetIfDifferent(encodingOptions.EnableIntelLowPowerHevcHwEncoder, false, value => encodingOptions.EnableIntelLowPowerHevcHwEncoder = value))
            {
                changed = true;
            }

            if (SetIfDifferent(encodingOptions.AllowHevcEncoding, false, value => encodingOptions.AllowHevcEncoding = value))
            {
                changed = true;
            }

            if (SetIfDifferent(encodingOptions.AllowAv1Encoding, false, value => encodingOptions.AllowAv1Encoding = value))
            {
                changed = true;
            }

            if (SetIfDifferent(encodingOptions.EnableDecodingColorDepth10Hevc, true, value => encodingOptions.EnableDecodingColorDepth10Hevc = value))
            {
                changed = true;
            }

            if (SetIfDifferent(encodingOptions.EnableDecodingColorDepth10Vp9, true, value => encodingOptions.EnableDecodingColorDepth10Vp9 = value))
            {
                changed = true;
            }

            if (SetIfDifferent(encodingOptions.EnableDecodingColorDepth10HevcRext, true, value => encodingOptions.EnableDecodingColorDepth10HevcRext = value))
            {
                changed = true;
            }

            if (SetIfDifferent(encodingOptions.EnableDecodingColorDepth12HevcRext, true, value => encodingOptions.EnableDecodingColorDepth12HevcRext = value))
            {
                changed = true;
            }
        }

        if (SetIfDifferent(encodingOptions.EnableSubtitleExtraction, true, value => encodingOptions.EnableSubtitleExtraction = value))
        {
            changed = true;
        }

        if (SetIfDifferent(encodingOptions.SubtitleExtractionTimeoutMinutes, 30, value => encodingOptions.SubtitleExtractionTimeoutMinutes = value))
        {
            changed = true;
        }

        if (SetIfDifferent(encodingOptions.DeinterlaceDoubleRate, true, value => encodingOptions.DeinterlaceDoubleRate = value))
        {
            changed = true;
        }

        if (SetIfDifferent(encodingOptions.EnableFallbackFont, true, value => encodingOptions.EnableFallbackFont = value))
        {
            changed = true;
        }

        if (SetIfDifferent(encodingOptions.EnableAudioVbr, true, value => encodingOptions.EnableAudioVbr = value))
        {
            changed = true;
        }

        if (SetIfDifferent(encodingOptions.HardwareDecodingCodecs, DefaultHardwareDecodingCodecs, value => encodingOptions.HardwareDecodingCodecs = value))
        {
            changed = true;
        }

        if (SetIfDifferent(
                encodingOptions.AllowOnDemandMetadataBasedKeyframeExtractionForExtensions,
                ["mkv", "mp4", "m4v", "m2ts", "ts"],
                value => encodingOptions.AllowOnDemandMetadataBasedKeyframeExtractionForExtensions = value))
        {
            changed = true;
        }

        return changed;
    }

    private static bool SupportsAv1Encoding(HardwareAccelerationType hardwareAccelerationType, string[] controllerNames)
    {
        switch (hardwareAccelerationType)
        {
            case HardwareAccelerationType.nvenc:
                return controllerNames.Any(name => ContainsAny(name, "RTX 40", "RTX 50", "Ada", "L4", "L40"));
            case HardwareAccelerationType.amf:
                return controllerNames.Any(name => ContainsAny(name, "RX 7", "7000", "7900", "7800", "7700", "7600", "RDNA 3"));
            case HardwareAccelerationType.qsv:
                return controllerNames.Any(name => ContainsAny(name, "Arc", "A770", "A750", "A580", "A380"));
            default:
                return false;
        }
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        foreach (var needle in needles)
        {
            if (value.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SetIfDifferent<T>(T currentValue, T desiredValue, Action<T> applyValue)
    {
        if (EqualityComparer<T>.Default.Equals(currentValue, desiredValue))
        {
            return false;
        }

        applyValue(desiredValue);
        return true;
    }

    private static bool SetIfDifferent(string[] currentValue, string[] desiredValue, Action<string[]> applyValue)
    {
        if (currentValue is not null && currentValue.SequenceEqual(desiredValue, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        applyValue(desiredValue);
        return true;
    }

    private readonly record struct HardwareDetection(
        string Name,
        bool IsGpu,
        HardwareAccelerationType HardwareAccelerationType,
        bool SupportsAv1Encoding);
}


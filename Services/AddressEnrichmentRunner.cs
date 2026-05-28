using System.Diagnostics;

namespace StoreFetcher.Services;

public interface IAddressEnrichmentRunner
{
    AddressEnrichmentStatus GetStatus();
    AddressEnrichmentStartResult Start(AddressEnrichmentOptions options);
}

public sealed class AddressEnrichmentRunner(
    IWebHostEnvironment environment,
    ILogger<AddressEnrichmentRunner> logger) : IAddressEnrichmentRunner
{
    private readonly object gate = new();
    private AddressEnrichmentStatus status = AddressEnrichmentStatus.Idle();
    private Process? process;

    public AddressEnrichmentStatus GetStatus()
    {
        lock (gate)
        {
            if (process is { HasExited: true })
            {
                CompleteProcess();
            }

            return status;
        }
    }

    public AddressEnrichmentStartResult Start(AddressEnrichmentOptions options)
    {
        lock (gate)
        {
            if (process is { HasExited: false })
            {
                return new(false, "Address enrichment is already running.");
            }

            var scriptPath = Path.Combine(
                environment.ContentRootPath,
                "scripts",
                "enrich-missing-addresses.mjs");
            if (!File.Exists(scriptPath))
            {
                return new(false, $"Missing enrichment script: {scriptPath}");
            }

            var dataDirectory = Path.Combine(environment.ContentRootPath, "data");
            var logDirectory = Path.Combine(environment.ContentRootPath, "logs");
            Directory.CreateDirectory(dataDirectory);
            Directory.CreateDirectory(logDirectory);

            var outputPath = Path.Combine(dataDirectory, "address-enriched-import.json");
            var reviewPath = Path.Combine(dataDirectory, "address-review.json");
            var outputLogPath = Path.Combine(logDirectory, "address-enrichment.out.log");
            var errorLogPath = Path.Combine(logDirectory, "address-enrichment.err.log");

            var startInfo = new ProcessStartInfo
            {
                FileName = "node",
                WorkingDirectory = environment.ContentRootPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            startInfo.ArgumentList.Add(scriptPath);
            startInfo.ArgumentList.Add($"--api={options.ApiBaseUrl}");
            startInfo.ArgumentList.Add($"--output={outputPath}");
            startInfo.ArgumentList.Add($"--review={reviewPath}");

            if (!options.UseOsm)
            {
                startInfo.ArgumentList.Add("--no-osm");
            }

            if (!options.UsePlaceLookup)
            {
                startInfo.ArgumentList.Add("--no-place-lookup");
            }

            if (options.MaxStores is > 0)
            {
                startInfo.ArgumentList.Add($"--max-stores={options.MaxStores.Value}");
            }

            if (!string.IsNullOrWhiteSpace(options.LantmaterietPath))
            {
                startInfo.ArgumentList.Add($"--lantmateriet={options.LantmaterietPath.Trim()}");
            }

            File.WriteAllText(outputLogPath, "");
            File.WriteAllText(errorLogPath, "");

            process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true,
            };
            process.OutputDataReceived += (_, args) => AppendLine(outputLogPath, args.Data);
            process.ErrorDataReceived += (_, args) => AppendLine(errorLogPath, args.Data);
            process.Exited += (_, _) =>
            {
                lock (gate)
                {
                    CompleteProcess();
                }
            };

            try
            {
                if (!process.Start())
                {
                    process.Dispose();
                    process = null;
                    return new(false, "Failed to start address enrichment.");
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                status = new AddressEnrichmentStatus(
                    true,
                    process.Id,
                    DateTimeOffset.UtcNow,
                    null,
                    null,
                    outputPath,
                    reviewPath,
                    outputLogPath,
                    errorLogPath,
                    "Address enrichment started.");

                logger.LogInformation(
                    "Started address enrichment process {ProcessId}.",
                    process.Id);

                return new(true, $"Address enrichment started. Process {process.Id}.");
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                logger.LogError(ex, "Failed to start address enrichment.");
                process?.Dispose();
                process = null;
                return new(false, "Failed to start Node. Make sure Node.js is installed and available on PATH.");
            }
        }
    }

    private void CompleteProcess()
    {
        if (process is null)
        {
            return;
        }

        var exitCode = process.ExitCode;
        status = status with
        {
            Running = false,
            FinishedAt = DateTimeOffset.UtcNow,
            ExitCode = exitCode,
            Message = exitCode == 0
                ? "Address enrichment finished."
                : $"Address enrichment failed with exit code {exitCode}.",
        };

        process.Dispose();
        process = null;
    }

    private static void AppendLine(string path, string? line)
    {
        if (line is null)
        {
            return;
        }

        File.AppendAllText(path, $"{line}{Environment.NewLine}");
    }
}

public sealed record AddressEnrichmentOptions(
    string ApiBaseUrl,
    string? LantmaterietPath,
    int? MaxStores,
    bool UseOsm,
    bool UsePlaceLookup);

public sealed record AddressEnrichmentStartResult(
    bool Started,
    string Message);

public sealed record AddressEnrichmentStatus(
    bool Running,
    int? ProcessId,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    int? ExitCode,
    string OutputPath,
    string ReviewPath,
    string OutputLogPath,
    string ErrorLogPath,
    string Message)
{
    public static AddressEnrichmentStatus Idle() => new(
        false,
        null,
        null,
        null,
        null,
        "",
        "",
        "",
        "",
        "Address enrichment has not run yet.");
}

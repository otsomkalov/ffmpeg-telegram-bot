using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace Bot.Services;

public class FFMpegService
{
    private readonly FFMpegSettings _settings;
    private readonly ILogger<FFMpegService> _logger;

    public FFMpegService(IOptions<FFMpegSettings> settings, ILogger<FFMpegService> logger)
    {
        _logger = logger;
        _settings = settings.Value;
    }

    public async Task<string> ConvertAsync(string filePath)
    {
        var outputFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.mp4");

        var argumentsParts = new List<string>
        {
            $"-i {filePath}",
            "-filter:v scale='trunc(iw/2)*2:trunc(ih/2)*2'",
            "-c:v hevc",
            "-c:a aac",
            "-max_muxing_queue_size 1024",
            "-map 0:v:0",
            "-map 0:a?",
            outputFilePath
        };

        var processStartInfo = new ProcessStartInfo
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            FileName = _settings.Path,
            Arguments = string.Join(' ', argumentsParts)
        };

        using var process = Process.Start(processStartInfo);

        var error = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        if (process.ExitCode == 0)
        {
            return outputFilePath;
        }

        _logger.LogError("FFMpeg error occured: {Error}", error);

        return null;
    }

    public async Task<string> GetThumbnailAsync(string filePath)
    {
        var thumbnailFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.jpg");

        var processStartInfo = new ProcessStartInfo
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            FileName = _settings.Path,
            Arguments = $"-i {filePath} -ss 1 -vframes 1 {thumbnailFilePath}"
        };

        using var process = Process.Start(processStartInfo);

        var error = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            _logger.LogError("FFMpeg error occured: {Error}", error);
        }

        return thumbnailFilePath;
    }
}
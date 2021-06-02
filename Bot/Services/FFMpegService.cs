using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Bot.Settings;
using Microsoft.Extensions.Options;

namespace Bot.Services
{
    public class FFMpegService
    {
        private readonly FFMpegSettings _settings;

        public FFMpegService(IOptions<FFMpegSettings> settings)
        {
            _settings = settings.Value;
        }

        public async Task<string> ConvertAsync(string filePath)
        {
            var outputFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.mp4");
            
            var processStartInfo = new ProcessStartInfo
            {
                RedirectStandardInput  = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                FileName = _settings.Path,
                Arguments = $"-i {filePath} -filter:v scale='trunc(ih/2)*2:trunc(iw/2)*2' -c:a aac -max_muxing_queue_size 1024 {outputFilePath}"
            };
            
            var process = Process.Start(processStartInfo);

            await process.WaitForExitAsync();

            var toEndAsync = await process.StandardError.ReadToEndAsync();

            Console.WriteLine(toEndAsync);

            return outputFilePath;
        }

        public async Task<string> GetThumbnailAsync(string filePath)
        {
            var thumbnailFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.jpg");
            
            var processStartInfo = new ProcessStartInfo
            {
                RedirectStandardInput  = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                FileName = _settings.Path,
                Arguments = $"-i {filePath} -ss 1 -vframes 1 {thumbnailFilePath}"
            };


            var process = Process.Start(processStartInfo);

            await process.WaitForExitAsync();
            
            return thumbnailFilePath;
        }
    }
}
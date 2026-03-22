using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;

namespace RecordIt.Core.Services;

internal static class FfmpegInstaller
{
    // URL pointing to a small, widely-available FFmpeg Windows static build archive.
    // You can change this to a different provider or an internal CDN if desired.
    private const string DefaultZipUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";

    public static async Task<bool> InstallBundledAsync(string destinationDirectory)
    {
        try
        {
            Directory.CreateDirectory(destinationDirectory);

            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromMinutes(2);

            var tmp = Path.Combine(Path.GetTempPath(), "ffmpeg_dl_" + Guid.NewGuid() + ".zip");
            using (var resp = await http.GetAsync(DefaultZipUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                await using var s = await resp.Content.ReadAsStreamAsync();
                await using var fs = File.Create(tmp);
                await s.CopyToAsync(fs);
            }

            // Extract the ffmpeg.exe from the archive. Many builds include a folder like "ffmpeg-*-essentials_build/bin/ffmpeg.exe".
            using var archive = ZipFile.OpenRead(tmp);
            foreach (var entry in archive.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');
                if (name.EndsWith("/ffmpeg.exe", StringComparison.OrdinalIgnoreCase) || name.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                {
                    var outPath = Path.Combine(destinationDirectory, "ffmpeg.exe");
                    entry.ExtractToFile(outPath, overwrite: true);
                    File.SetAttributes(outPath, FileAttributes.Normal);
                    File.Delete(tmp);
                    return true;
                }
            }

            File.Delete(tmp);
            return false;
        }
        catch
        {
            return false;
        }
    }
}

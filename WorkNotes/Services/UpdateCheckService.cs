using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace WorkNotes.Services
{
    /// <summary>
    /// Manual update check (Chapter 17 model: read-only check + link, never
    /// auto-download or self-replace). Queries the GitHub Releases API once,
    /// on demand, and compares tag versions.
    /// </summary>
    public class UpdateCheckService
    {
        private const string ReleasesApi = "https://api.github.com/repos/S-Pillow/Workpad/releases/latest";
        public const string ReleasesPage = "https://github.com/S-Pillow/Workpad/releases";

        public class UpdateInfo
        {
            public bool UpdateAvailable { get; init; }
            public string? LatestTag { get; init; }
            public string? LatestUrl { get; init; }
            public string? Error { get; init; }
        }

        public async Task<UpdateInfo> CheckAsync()
        {
            try
            {
                using var http = new HttpClient();
                http.DefaultRequestHeaders.UserAgent.ParseAdd("WorkNotes-update-check");
                http.Timeout = TimeSpan.FromSeconds(10);
                var json = await http.GetStringAsync(ReleasesApi).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? string.Empty;
                var url = doc.RootElement.GetProperty("html_url").GetString() ?? ReleasesPage;

                var latest = ParseVersion(tag);
                var current = typeof(UpdateCheckService).Assembly.GetName().Version;
                var available = latest != null && current != null && latest > current;
                return new UpdateInfo { UpdateAvailable = available, LatestTag = tag, LatestUrl = url };
            }
            catch (Exception ex)
            {
                return new UpdateInfo { Error = $"Could not check for updates: {ex.Message}" };
            }
        }

        private static Version? ParseVersion(string tag)
        {
            var t = tag.TrimStart('v', 'V');
            return Version.TryParse(t, out var v) ? v : null;
        }
    }
}

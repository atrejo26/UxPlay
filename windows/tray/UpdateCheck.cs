using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace UxPlayTray
{
    /// <summary>
    /// Release tags of this app: "v" + UxPlay version + "-win" + build number, with an
    /// optional pre-release part, e.g. v1.74-win2 or v1.74-win3-beta.
    /// </summary>
    static class ReleaseTag
    {
        static readonly Regex Pattern = new Regex(@"^v(\d+(?:\.\d+)*)-win(\d+)(?:-(.+))?$", RegexOptions.CultureInvariant);

        /// <summary>The release this copy was built for; null for a build that isn't a release.</summary>
        public static string Current
        {
            get
            {
                foreach (AssemblyMetadataAttribute a in typeof(ReleaseTag).Assembly.GetCustomAttributes(typeof(AssemblyMetadataAttribute), false))
                    if (a.Key == "ReleaseTag" && Pattern.IsMatch(a.Value ?? ""))
                        return a.Value;
                return null;
            }
        }

        static bool TryParse(string tag, out Version upstream, out int build, out bool preRelease)
        {
            upstream = null;
            build = 0;
            preRelease = false;
            var m = Pattern.Match(tag ?? "");
            if (!m.Success || !int.TryParse(m.Groups[2].Value, out build))
                return false;
            string v = m.Groups[1].Value;
            if (!Version.TryParse(v.Contains(".") ? v : v + ".0", out upstream))
                return false;
            preRelease = m.Groups[3].Success;
            return true;
        }

        /// <summary>Whether <paramref name="latest"/> is a newer release than <paramref name="current"/>.</summary>
        public static bool IsNewer(string latest, string current)
        {
            if (!TryParse(latest, out var lv, out int lb, out bool lpre) ||
                !TryParse(current, out var cv, out int cb, out bool cpre))
                return false;
            int byVersion = lv.CompareTo(cv);
            if (byVersion != 0)
                return byVersion > 0;
            if (lb != cb)
                return lb > cb;
            return cpre && !lpre;  // the finished release of the beta this copy is
        }
    }

    /// <summary>Looks up the newest published release.</summary>
    interface IUpdateCheck
    {
        /// <summary>The newest release's tag, or null when it can't be found out.</summary>
        Task<string> LatestReleaseTagAsync();
    }

    static class UpdateCheckExtensions
    {
        /// <summary>The newest release's tag when it is newer than <paramref name="current"/>, else null.</summary>
        public static async Task<string> NewerReleaseAsync(this IUpdateCheck check, string current)
        {
            if (current == null)
                return null;
            string latest = await check.LatestReleaseTagAsync();
            return latest != null && ReleaseTag.IsNewer(latest, current) ? latest : null;
        }
    }

    /// <summary>One small request to GitHub's releases API (pre-releases aren't included).</summary>
    sealed class GitHubUpdateCheck : IUpdateCheck
    {
        readonly string latestReleaseApi;

        public GitHubUpdateCheck(string ownerAndRepo)
        {
            latestReleaseApi = "https://api.github.com/repos/" + ownerAndRepo + "/releases/latest";
        }

        /// <summary>The web address asked for the newest release.</summary>
        public string ApiUrl => latestReleaseApi;

        static readonly Regex TagName = new Regex("\"tag_name\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.CultureInvariant);

        /// <summary>The tag_name of a GitHub release as JSON, or null.</summary>
        public static string ParseTag(string json)
        {
            var m = TagName.Match(json ?? "");
            return m.Success ? m.Groups[1].Value : null;
        }

        // Talks to GitHub, so the unit tests only cover ParseTag.
        [ExcludeFromCodeCoverage]
        public Task<string> LatestReleaseTagAsync() => Task.Run(() =>
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                var request = (HttpWebRequest)WebRequest.Create(latestReleaseApi);
                request.UserAgent = "UxPlay-for-Windows";  // GitHub's API asks for one
                request.Accept = "application/vnd.github+json";
                request.Timeout = 15000;
                request.ReadWriteTimeout = 15000;
                using (var response = request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream()))
                    return ParseTag(reader.ReadToEnd());
            }
            catch (Exception ex) when (ex is WebException || ex is IOException || ex is NotSupportedException)
            {
                return null;  // offline, blocked or rate limited: try again next time
            }
        });
    }
}

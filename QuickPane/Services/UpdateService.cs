using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;

namespace QuickPane.Services
{
    /// <summary>One GitHub release, cut down to the two things an update check needs.</summary>
    [DataContract]
    internal sealed class GithubRelease
    {
        [DataMember(Name = "tag_name")] public string TagName { get; set; }
        [DataMember(Name = "html_url")] public string HtmlUrl { get; set; }
        [DataMember(Name = "draft")] public bool Draft { get; set; }
        [DataMember(Name = "prerelease")] public bool Prerelease { get; set; }
        [DataMember(Name = "assets")] public GithubAsset[] Assets { get; set; }
    }

    [DataContract]
    internal sealed class GithubAsset
    {
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "browser_download_url")] public string DownloadUrl { get; set; }
    }

    /// <summary>What a finished check found.</summary>
    internal sealed class UpdateCheckResult
    {
        public bool Available;
        public Version Latest;
        public string DownloadUrl;
        public string Error;
    }

    /// <summary>
    /// Reads the running build's version from its own assembly and asks GitHub whether a newer release
    /// is published. The version is never written down anywhere but AssemblyInfo, so the settings footer
    /// cannot drift out of step with the binary the way a hardcoded string would.
    /// </summary>
    internal static class UpdateService
    {
        // The fork's own releases, not upstream's. Offering upstream's installer to someone running a
        // fork build is a downgrade: it replaces the fork's fixes until the fork merges and releases.
        // `releases/latest` skips drafts and pre-releases, so fork releases must be published as full ones.
        private const string LatestReleaseApi = "https://api.github.com/repos/prisant-labs/windows-explorer-quickpane/releases/latest";
        internal const string ReleasesPage = "https://github.com/prisant-labs/windows-explorer-quickpane/releases/latest";

        /// <summary>The running build's version, from the assembly rather than a constant.</summary>
        public static Version Current
        {
            get
            {
                try
                {
                    var v = Assembly.GetExecutingAssembly().GetName().Version;
                    return v ?? new Version(0, 0, 0, 0);
                }
                catch { return new Version(0, 0, 0, 0); }
            }
        }

        /// <summary>"3.9.0", trimmed of a trailing zero revision so it reads like the release tag.</summary>
        public static string CurrentDisplay
        {
            get { return Display(Current); }
        }

        /// <summary>
        /// A version as its release tag spells it: "3.10.0" upstream, "3.10.0.1" for a fork build.
        /// A plain ToString(3) drops the fork's fourth part, so a newer fork release read as the version
        /// already running; and it throws on a two-part tag such as "v3.11", which TryParseTag accepts.
        /// </summary>
        internal static string Display(Version v)
        {
            if (v == null) return "";
            if (v.Revision > 0) return v.ToString(4);
            return v.Build >= 0 ? v.ToString(3) : v.ToString(2);
        }

        /// <summary>Ask GitHub for the latest release on the worker, then hand the answer to
        /// <paramref name="done"/> on the UI thread. Never throws at the caller.</summary>
        public static void CheckAsync(Action<UpdateCheckResult> done)
        {
            if (done == null) return;
            WorkQueue.Post(() =>
            {
                var result = Check();
                WorkQueue.PostUI(() => done(result));
            });
        }

        /// <summary>Opens the latest release's page in the default browser, off the UI thread because
        /// starting a process goes through the shell. The update notice goes here rather than to the
        /// installer, so the user reads what changed and starts the download themselves (#176).</summary>
        public static void OpenReleasePage()
        {
            WorkQueue.Post(() =>
            {
                try { Process.Start(new ProcessStartInfo(ReleasesPage) { UseShellExecute = true }); }
                catch (Exception ex) { Log.Error("open the release page", ex); }
            });
        }

        private static UpdateCheckResult Check()
        {
            var r = new UpdateCheckResult();
            try
            {
                // GitHub's API refuses anonymous calls without a user agent, and .NET 4.8 still defaults
                // to a protocol GitHub dropped, so both have to be set before the request goes out.
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                var req = (HttpWebRequest)WebRequest.Create(LatestReleaseApi);
                req.UserAgent = "QuickPane/" + CurrentDisplay;
                req.Accept = "application/vnd.github+json";
                req.Timeout = 10000;
                req.ReadWriteTimeout = 10000;

                GithubRelease release;
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var stream = resp.GetResponseStream())
                {
                    if (stream == null) { r.Error = "no response from GitHub"; Log.Info("update check: " + r.Error + "."); return r; }
                    var ser = new DataContractJsonSerializer(typeof(GithubRelease));
                    release = ser.ReadObject(stream) as GithubRelease;
                }

                if (release == null || string.IsNullOrEmpty(release.TagName))
                {
                    r.Error = "GitHub returned no release";
                    Log.Info("update check: " + r.Error + ".");
                    return r;
                }

                Version latest;
                if (!TryParseTag(release.TagName, out latest))
                {
                    r.Error = "could not read the release tag \"" + release.TagName + "\"";
                    Log.Info("update check: " + r.Error + ".");
                    return r;
                }

                r.Latest = latest;
                r.Available = latest > Current;
                r.DownloadUrl = PickDownload(release);
                return r;
            }
            catch (WebException ex) when (IsNotFound(ex))
            {
                // A repository with no published full release answers `releases/latest` with 404. Nothing
                // newer exists then, so the footer says "Up to date" rather than "Update check failed".
                Log.Info("update check: no published release yet.");
                return r;
            }
            catch (Exception ex)
            {
                Log.Error("check for updates", ex);
                r.Error = ex.Message;
                return r;
            }
        }

        private static bool IsNotFound(WebException ex)
        {
            var resp = ex.Response as HttpWebResponse;
            return resp != null && resp.StatusCode == HttpStatusCode.NotFound;
        }

        /// <summary>The installer if the release ships one, else the release page.</summary>
        private static string PickDownload(GithubRelease release)
        {
            try
            {
                if (release.Assets != null)
                {
                    foreach (var a in release.Assets)
                    {
                        if (a == null || string.IsNullOrEmpty(a.DownloadUrl) || string.IsNullOrEmpty(a.Name)) continue;
                        var ext = Path.GetExtension(a.Name);
                        if (string.Equals(ext, ".exe", StringComparison.OrdinalIgnoreCase)) return a.DownloadUrl;
                    }
                    foreach (var a in release.Assets)
                    {
                        if (a == null || string.IsNullOrEmpty(a.DownloadUrl) || string.IsNullOrEmpty(a.Name)) continue;
                        var ext = Path.GetExtension(a.Name);
                        if (string.Equals(ext, ".zip", StringComparison.OrdinalIgnoreCase)) return a.DownloadUrl;
                    }
                }
            }
            catch (Exception ex) { Log.Error("pick update download", ex); }
            return string.IsNullOrEmpty(release.HtmlUrl) ? ReleasesPage : release.HtmlUrl;
        }

        /// <summary>
        /// The version inside a release tag, wherever it sits in it. Tags have been published as
        /// "v3.9.0" and as "QuickPaneSetup-3.7.0-release", so anything that assumes the tag starts with
        /// the number reads the second one as no version at all.
        /// </summary>
        internal static bool TryParseTag(string tag, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(tag)) return false;
            var m = Regex.Match(tag, @"\d+(?:\.\d+){1,3}");
            return m.Success && Version.TryParse(m.Value, out version);
        }
    }
}

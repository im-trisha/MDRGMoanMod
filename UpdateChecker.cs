using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using MelonLoader;
using UnityEngine.Networking;

namespace MoanMod
{

    public class SemanticVersion : IComparable<SemanticVersion>
    {
        private static readonly Regex SemVerRegex = new Regex(
            @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?(?:\+([0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$",
            RegexOptions.Compiled
        );

        public int Major { get; private set; }
        public int Minor { get; private set; }
        public int Patch { get; private set; }
        public string Prerelease { get; private set; } = "";
        public string BuildMetadata { get; private set; } = "";

        public bool IsPrerelease => !string.IsNullOrEmpty(Prerelease);
        public bool HasBuildMetadata => !string.IsNullOrEmpty(BuildMetadata);

        public SemanticVersion(string versionString)
        {
            if (string.IsNullOrWhiteSpace(versionString))
                throw new ArgumentNullException(nameof(versionString));

            versionString = versionString.TrimStart('v', 'V');

            Match match = SemVerRegex.Match(versionString);
            if (!match.Success) throw new ArgumentException($"Invalid semver 2.0.0: {versionString}");

            Major = int.Parse(match.Groups[1].Value);
            Minor = int.Parse(match.Groups[2].Value);
            Patch = int.Parse(match.Groups[3].Value);

            Prerelease = match.Groups[4].Value;
            BuildMetadata = match.Groups[5].Value;
        }


        public int CompareTo(SemanticVersion other)
        {
            if (other == null) return 1;

            if (Major != other.Major) return Major.CompareTo(other.Major);
            if (Minor != other.Minor) return Minor.CompareTo(other.Minor);
            if (Patch != other.Patch) return Patch.CompareTo(other.Patch);

            if (!IsPrerelease && other.IsPrerelease) return 1;
            if (IsPrerelease && !other.IsPrerelease) return -1;
            if (!IsPrerelease && !other.IsPrerelease) return 0;

            return Prerelease.CompareTo(other.Prerelease); // wrong according to semver, but kept because CBA
        }

        public override string ToString()
        {
            var version = $"{Major}.{Minor}.{Patch}";
            if (IsPrerelease) version += $"-{Prerelease}";
            if (HasBuildMetadata) version += $"+{BuildMetadata}";
            return version;
        }

        public override bool Equals(object obj) => CompareTo(obj as SemanticVersion) == 0;

        public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, Prerelease);
        
        public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;

        public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;

        public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;

        public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;
    }

    public class GitHubRelease
    {
        public string TagName { get; set; }
        public string HtmlUrl { get; set; }
        public SemanticVersion Version { get; set; }
    }

    public class UpdateChecker
    {
        private UnityWebRequest githubRequest = null;
        private List<GitHubRelease> cachedReleases = null;
        private string updateReleaseUrl = null;
        private string currentVersion = null;

        public List<GitHubRelease> CachedReleases => cachedReleases;
        public string UpdateReleaseUrl => updateReleaseUrl;
        public bool IsCheckComplete => cachedReleases != null;

        public void StartCheck(string version)
        {
            if (githubRequest != null)
                return;

            currentVersion = version;
            githubRequest = UnityWebRequest.Get("https://api.github.com/repos/IkariDevGIT/MDRGMoanMod/releases?per_page=10");
            githubRequest.downloadHandler = new DownloadHandlerBuffer();
            githubRequest.SendWebRequest();
        }

        public void Update()
        {
            if (githubRequest == null || !githubRequest.isDone)
                return;

            if (githubRequest.result == UnityWebRequest.Result.Success)
            {
                string json = githubRequest.downloadHandler.text;
                var releases = ParseGitHubReleasesJson(json);
                cachedReleases = releases;

                string updateMessage = GetUpdateMessage(currentVersion, releases);
                if (!string.IsNullOrEmpty(updateMessage))
                {
                    updateReleaseUrl = GetLatestReleaseUrl(currentVersion, releases);
                }
            }
            else
            {
                MelonLogger.Error($"Failed to fetch releases: {githubRequest.error}");
                cachedReleases = new System.Collections.Generic.List<GitHubRelease>();
            }

            githubRequest.Dispose();
            githubRequest = null;
        }

        public static string GetLatestReleaseUrl(string currentVersionStr, List<GitHubRelease> releases)
        {
            try
            {
                var currentVersion = new SemanticVersion(currentVersionStr);
                var suggestedRelease = FindSuggestedRelease(currentVersion, releases);

                if (suggestedRelease != null)
                {
                    return suggestedRelease.HtmlUrl;
                }
            }
            catch (Exception) { }

            return null;
        }

        public static string GetUpdateMessage(string currentVersionStr, List<GitHubRelease> releases)
        {
            try
            {
                var currentVersion = new SemanticVersion(currentVersionStr);
                var suggestedRelease = FindSuggestedRelease(currentVersion, releases);

                if (suggestedRelease != null && suggestedRelease.Version > currentVersion)
                {
                    return $"Your version \"{currentVersionStr}\" is outdated, get {suggestedRelease.Version} on github.";
                }
            }
            catch (Exception) { }

            return null;
        }

        private static GitHubRelease FindSuggestedRelease(SemanticVersion currentVersion, List<GitHubRelease> releases)
        {
            if (releases == null || releases.Count == 0)
                return null;

            var sortedReleases = releases.OrderByDescending(r => r.Version).ToList();

            if (currentVersion.IsPrerelease)
            {
                var newer = sortedReleases.FirstOrDefault(r => r.Version > currentVersion);
                return newer;
            }
            else
            {
                var newerStable = sortedReleases.FirstOrDefault(r => !r.Version.IsPrerelease && r.Version > currentVersion);
                return newerStable;
            }
        }

        private List<GitHubRelease> ParseGitHubReleasesJson(string json)
        {
            var releases = new List<GitHubRelease>();

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Array) return new List<GitHubRelease>();
                    
                foreach (var element in root.EnumerateArray())
                {
                    if (!element.TryGetProperty("tag_name", out var tagNameElement) ||
                        !element.TryGetProperty("html_url", out var htmlUrlElement)) continue;
                        
                    string tagName = tagNameElement.GetString();
                    string htmlUrl = htmlUrlElement.GetString();

                    try
                    {
                        var version = new SemanticVersion(tagName);
                        releases.Add(new GitHubRelease
                        {
                            TagName = tagName,
                            HtmlUrl = htmlUrl,
                            Version = version
                        });
                    }
                    catch (ArgumentException) { }
                        
                }
            }
            catch (Exception) { }

            return releases;
        }
    }
}
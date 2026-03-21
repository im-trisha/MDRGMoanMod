using System;
using System.Collections;
using System.Linq;
using System.Text.Json;
using MelonLoader;
using Microsoft.VisualBasic;
using MoanMod.PopupService;
using UnityEngine.Networking;

namespace MoanMod
{

    public class GitHubRelease
    {
        public string TagName { get; set; }
        public string HtmlUrl { get; set; }
        public SemanticVersion Version { get; set; }
    }

    public class UpdateChecker
    {
        private bool isCheckingForUpdates = false;
        private string updateReleaseUrl = null;
        private string currentVersion = null;

        private IPopupService popupService = new OverlayPopupService();

        public IEnumerator CheckForUpdatesCoroutine(SemanticVersion version)
        {
            if (isCheckingForUpdates) yield break;
            var githubRequest = UnityWebRequest.Get("https://api.github.com/repos/IkariDevGIT/MDRGMoanMod/releases?per_page=10");
            githubRequest.downloadHandler = new DownloadHandlerBuffer();
        
            yield return githubRequest.SendWebRequest();

            if (githubRequest.result != UnityWebRequest.Result.Success)
            {
                MelonLogger.Error($"Failed to fetch releases: {githubRequest.error}");
                isCheckingForUpdates = false;
                yield break;
            }
            
            string json = githubRequest.downloadHandler.text;
            var releases = ParseGitHubReleasesJson(json);

            string updateMessage = GetUpdateMessage(currentVersion, releases);
            if (string.IsNullOrEmpty(updateMessage))
            {
                MelonLogger.Error()
            }
                updateReleaseUrl = GetLatestReleaseUrl(currentVersion, releases);
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
            if (releases == null || releases.Count == 0) return null;

            return releases
                .Where(r => r.Version > currentVersion)
                .Where(r => currentVersion.IsPrerelease || !r.Version.IsPrerelease) // If stable, ignore pre-releases
                .OrderByDescending(r => r.Version)
                .FirstOrDefault();
        }

        private List<GitHubRelease> ParseGitHubReleasesJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new List<GitHubRelease>();

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return new List<GitHubRelease>();

                return doc.RootElement.EnumerateArray()
                    .Select(element => CreateReleaseFromElement(element))
                    .Where(release => release != null)
                    .ToList();
            }
            catch (JsonException ex)
            {
                MelonLogger.Error($"Failed to parse GitHub JSON: {ex.Message}");
                return new List<GitHubRelease>();
            }
        }

        private GitHubRelease CreateReleaseFromElement(JsonElement element)
        {
            if (!element.TryGetProperty("tag_name", out var tag) ||
                !element.TryGetProperty("html_url", out var url))
                return null;

            string tagName = tag.GetString();

            if (!SemanticVersion.TryParse(tagName, out var version)) return null;

            return new GitHubRelease
            {
                TagName = tagName,
                HtmlUrl = url.GetString(),
                Version = version
            };
        }

        private void ShowUpdatePopup()
        {
            var releases = updateChecker.CachedReleases;
            string updateMessage = UpdateChecker.GetUpdateMessage(modVersion, releases);

            var choices = new[] {
                new PopupChoice("Skip", () => { }),
                new PopupChoice("Open in Browser", () => OpenUrlInBrowser(updateChecker.UpdateReleaseUrl)),
                new PopupChoice("Disable Notifications", () =>
                {
                    prefUpdateCheckingEnabled.Value = false;
                    MelonPreferences.Save();
                })
            };

            uiOverlay.Popup("MoanMod - Update Available", updateMessage, choices);
        }

        private void OpenUrlInBrowser(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            try
            {
                Application.OpenURL(url);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Failed to open browser: {ex.Message}");
            }
        }
    }
}
using System.Text.RegularExpressions;
using Shouldly;

namespace BlokeBot.Tests;

/// <summary>
/// Enforces the accepted no-consent-banner boundary: the deployed applications may only use the
/// exempt authentication/security cookies and appearance-preference storage inventoried in the
/// published privacy notice. Introducing a new browser-storage key, cookie, third-party script,
/// or sensitive log template fails here until a consent contract and notice update are accepted.
/// </summary>
public sealed partial class ClientStorageConsentBoundaryTests
{
    private static readonly string[] _approvedStorageKeys =
    [
        "blokebot.theme",
        "blokebot.sidebar.guessing.open",
        "blokebot.sidebar.points.open",
        "blokebot.sidebar.customcommands.open",
        "blokebot.sidebar.automations.open",
        "blokebot.sidebar.nativetwitch.open",
        "blokebot.shell.rail.v1",
        "blokebot.preferences.disabled",
    ];

    private static readonly string[] _approvedCookieNames =
    [
        "BlokeBot.Auth",
        "BlokeBot.AuthState",
        "BlokeBot.AuthReturnUrl",
        "BlokeBot.ChannelBotState",
    ];

    private static readonly string[] _forbiddenLogPlaceholders =
    [
        "{Code}",
        "{State}",
        "{AccessToken}",
        "{RefreshToken}",
        "{Token}",
        "{ClientSecret}",
        "{Secret}",
        "{Signature}",
        "{Authorization}",
        "{Cookie}",
        "{CookieValue}",
        "{Body}",
        "{RawBody}",
        "{Payload}",
        "{AccessKey}",
        "{Query}",
        "{QueryString}",
    ];

    [Test]
    public void BrowserStorage_UsesOnlyInventoriedKeysAndNoNonExemptTechnology()
    {
        var violations = new List<string>();
        foreach (var file in SourceFiles())
        {
            var content = File.ReadAllText(file);
            var relative = Path.GetRelativePath(RepositoryRoot(), file);

            foreach (var api in (string[])["sessionStorage", "indexedDB", "document.cookie"])
            {
                if (content.Contains(api, StringComparison.Ordinal))
                {
                    violations.Add($"{relative}: uses {api}");
                }
            }

            // Storage keys can be carried as constants through JSInterop by files that never
            // mention localStorage themselves, so those files are scanned too.
            var touchesClientStorageSurface =
                content.Contains("localStorage", StringComparison.Ordinal)
                || content.Contains("JSInterop", StringComparison.Ordinal)
                || content.Contains("IJSRuntime", StringComparison.Ordinal);
            if (!touchesClientStorageSurface)
            {
                continue;
            }

            foreach (Match match in StorageKeyPattern().Matches(content))
            {
                var key = match.Groups[1].Value;
                if (!_approvedStorageKeys.Contains(key, StringComparer.Ordinal))
                {
                    violations.Add($"{relative}: unreviewed storage key {key}");
                }
            }
        }

        violations.ShouldBeEmpty(
            "New client-side storage or technology needs an accepted consent contract and a "
                + "privacy-notice update before it can ship"
        );
    }

    [Test]
    public void Cookies_AppendedByTheApplications_AreLimitedToTheInventoriedNames()
    {
        var appended = new List<string>();
        foreach (var file in SourceFiles())
        {
            var content = File.ReadAllText(file);
            foreach (Match match in CookieAppendPattern().Matches(content))
            {
                appended.Add(match.Groups[1].Value);
            }
        }

        appended.ShouldNotBeEmpty();
        appended
            .Where(name => !_approvedCookieNames.Contains(name, StringComparer.Ordinal))
            .ShouldBeEmpty(
                "A new cookie needs a privacy-notice inventory entry and PECR assessment first"
            );
    }

    [Test]
    public void Markup_LoadsNoThirdPartyScripts()
    {
        var violations = new List<string>();
        foreach (var file in SourceFiles())
        {
            var content = File.ReadAllText(file);
            var relative = Path.GetRelativePath(RepositoryRoot(), file);
            foreach (Match match in ExternalScriptPattern().Matches(content))
            {
                violations.Add($"{relative}: {match.Value}");
            }
        }

        violations.ShouldBeEmpty(
            "Third-party scripts are outside the accepted no-tracking boundary"
        );
    }

    [Test]
    public void LogTemplates_NeverNameSensitiveValues()
    {
        var violations = new List<string>();
        foreach (
            var file in SourceFiles()
                .Where(static file => file.EndsWith(".cs", StringComparison.Ordinal))
        )
        {
            var lines = File.ReadAllLines(file);
            var depth = 0;
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                if (depth == 0 && !LogCallPattern().IsMatch(line))
                {
                    continue;
                }

                foreach (var placeholder in _forbiddenLogPlaceholders)
                {
                    if (line.Contains(placeholder, StringComparison.Ordinal))
                    {
                        violations.Add(
                            $"{Path.GetRelativePath(RepositoryRoot(), file)}:{index + 1}: {placeholder}"
                        );
                    }
                }

                depth += line.Count(static c => c == '(') - line.Count(static c => c == ')');
                if (depth < 0)
                {
                    depth = 0;
                }
            }
        }

        violations.ShouldBeEmpty(
            "OAuth codes/state, tokens, secrets, signatures, cookies, keys, and raw payloads "
                + "must never enter log templates"
        );
    }

    [GeneratedRegex("\"(blokebot\\.[a-z0-9][a-z0-9.\\-]*)\"", RegexOptions.None)]
    private static partial Regex StorageKeyPattern();

    [GeneratedRegex("Cookies\\.Append\\(\\s*\"([^\"]+)\"", RegexOptions.None)]
    private static partial Regex CookieAppendPattern();

    [GeneratedRegex("<script[^>]*\\ssrc\\s*=\\s*\"https?://", RegexOptions.IgnoreCase)]
    private static partial Regex ExternalScriptPattern();

    [GeneratedRegex(
        "\\.(?:Log(?:Trace|Debug|Information|Warning|Error|Critical)|Log)\\s*\\(|Log\\.(?:Verbose|Debug|Information|Warning|Error|Fatal)\\s*\\("
    )]
    private static partial Regex LogCallPattern();

    private static IEnumerable<string> SourceFiles()
    {
        var sourceRoot = Path.Combine(RepositoryRoot(), "src");
        return Directory
            .EnumerateFiles(sourceRoot, "*.*", SearchOption.AllDirectories)
            .Where(static file =>
                Path.GetExtension(file) is ".cs" or ".razor" or ".js"
                && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !file.Contains("node_modules", StringComparison.Ordinal)
            );
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (
            directory is not null && !File.Exists(Path.Combine(directory.FullName, "BlokeBot.slnx"))
        )
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("The repository root could not be located.");
    }
}

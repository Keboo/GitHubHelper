using Octokit;
using System;
using System.Collections;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.IO;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Threading.Tasks;

namespace GitHubHelper;

class Program
{
    public static async Task<int> Main(string[] args)
    {
        Option<string> milestone = new("--milestone");
        Option<string?> accessToken = new("--access-token", getDefaultValue: () => Environment.GetEnvironmentVariable("GitHubAccessToken"));
        Option<string?> repoOwner = new("--repo-owner", getDefaultValue: () => "MaterialDesignInXAML");
        Option<string?> repoName = new("--repo-name", getDefaultValue: () => "MaterialDesignInXamlToolkit");

        Command contributors = new("contributors")
        {
            milestone,
            accessToken,
            repoOwner,
            repoName
        };
        contributors.SetHandler<IConsole, string, string?, string?, string?>(
            MilestoneContributors, milestone, accessToken, repoOwner, repoName);

        Option<DateTimeOffset> since = new("--since");
        Command projects = new("projects")
        {
            since,
            accessToken,
            repoOwner,
            repoName
        };
        projects.SetHandler<IConsole, DateTimeOffset, string?, string?, string?>(
            CreatedProjects, since, accessToken, repoOwner, repoName);
        Command createdFiles = new("created")
        {
            projects
        };

        Option<string> previousVersion = new("--previous-version");
        Option<string> currentVersion = new("--current-version");
        Command icons = new("icons")
        {
            previousVersion,
            currentVersion
        };
        icons.SetHandler<IConsole, string, string>(
            DiffIcons, previousVersion, currentVersion);

        Command diff = new("diff")
        {
            icons
        };

        return await new CommandLineBuilder(new RootCommand()
        {
            contributors,
            createdFiles,
            diff
        }).UseDefaults()
          .Build()
          .InvokeAsync(args);
    }

    public static async Task<int> CreatedProjects(
        IConsole console,
        DateTimeOffset since,
        string? accessToken = null,
        string? repoOwner = null,
        string? repoName = null)
    {
        if (!TryGetRepoInfo(console, ref repoOwner, ref repoName))
        {
            return 1;
        }

        IGitHubClient github = GitHub.GetClient(accessToken);

        IReadOnlyList<GitHubCommit> commits = Array.Empty<GitHubCommit>();
        var seenProjectFiles = new HashSet<string>();
        for (int i = 1; i <= 1 || commits.Any(); i++)
        {
            commits = await github.Repository.Commit.GetAll(repoOwner, repoName, new CommitRequest
            {
                Since = since
            },
            new ApiOptions
            {
                PageSize = 30,
                PageCount = 1,
                StartPage = i
            });

            foreach (string sha in commits.Select(x => x.Sha))
            {
                //TODO: For some reason files are not returned with the list
                GitHubCommit commit = await github.Repository.Commit.Get(repoOwner, repoName, sha);

                foreach (GitHubCommitFile file in commit.Files ?? Enumerable.Empty<GitHubCommitFile>())
                {
                    if (string.Equals(Path.GetExtension(file.Filename), ".csproj", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(file.Status, "added", StringComparison.OrdinalIgnoreCase)
                        && seenProjectFiles.Add(file.Filename))
                    {
                        console.Out.WriteLine($"{commit.Commit.Author.Date:d} => {file.Filename}");
                    }
                }
            }
        }

        return 0;
    }

    public static async Task<int> MilestoneContributors(
        IConsole console,
        string milestone,
        string? accessToken = null,
        string? repoOwner = null,
        string? repoName = null)
    {
        console.Out.WriteLine($"Getting contributors from {repoOwner}/{repoName} for milestone {milestone}");
        if (!TryGetRepoInfo(console, ref repoOwner, ref repoName))
        {
            return 1;
        }

        IGitHubClient github = GitHub.GetClient(accessToken);

        IReadOnlyList<Milestone> milestones = await github.Issue.Milestone.GetAllForRepository(repoOwner, repoName);

        Milestone? githubMilestone = milestones.FirstOrDefault(x => x.Title == milestone);

        if (githubMilestone is null)
        {
            console.Error.WriteLine($"Could not find milestone '{milestone}'");
            return 2;
        }

        var issueRequest = new RepositoryIssueRequest
        {
            Milestone = $"{githubMilestone.Number}",
            Filter = IssueFilter.All,
            State = ItemStateFilter.All
        };

        IReadOnlyList<Issue> issues = await github.Issue.GetAllForRepository(repoOwner, repoName, issueRequest);

        var prRequest = new PullRequestRequest
        {
            State = ItemStateFilter.All,
        };
        IReadOnlyList<PullRequest> pullRequests = await github.PullRequest.GetAllForRepository(repoOwner, repoName, prRequest);

        var users = new HashSet<User>(new UserComparer());
        var filePaths = new HashSet<string>();
        foreach (Issue issue in issues)
        {
            users.Add(issue.User);
        }
        foreach (PullRequest pr in pullRequests.Where(x => x.Milestone?.Id == githubMilestone.Id))
        {
            users.Add(pr.User);
        }

        var ignoredUsers = new[] { "Keboo", "MDIX-SA", "github-actions[bot]" };

        StringBuilder sb = new();
        sb.AppendLine("A big thank you to everyone who contributed (either with issues or pull requests):");

        foreach (string user in users
            .Select(x => x.Login)
            .Except(ignoredUsers)
            .OrderBy(x => x))
        {
            sb.AppendLine($"@{user}");
        }

        console.Out.WriteLine($"::set-output name=contributors::{sb}");

        return 0;
    }

    public static async Task<int> DiffIcons(
        IConsole console,
        string previousVersion,
        string currentVersion)
    {
        if (string.IsNullOrWhiteSpace(previousVersion))
        {
            console.Out.WriteLine("Previous version is required");
            return 1;
        }
        if (string.IsNullOrWhiteSpace(currentVersion))
        {
            console.Out.WriteLine("Current version is required");
            return 1;
        }

        IProcessManager processManager = new ProcessManager(console);

        string command = $"install MaterialDesignThemes -DirectDownload -Prerelease -Version ";

        FileInfo nuget = new("NuGet.exe");
        if (nuget.Directory is null)
        {
            console.Out.WriteLine($"Failed to find NuGet.exe");
            return 1;
        }
        if (!await processManager.RunNugetCommand(nuget, command + previousVersion, nuget.Directory))
        {
            console.Out.WriteLine($"Failed to download MaterialDesignThemes {previousVersion}");
            return 1;
        }
        if (!await processManager.RunNugetCommand(nuget, command + currentVersion, nuget.Directory))
        {
            console.Out.WriteLine($"Failed to download MaterialDesignThemes {currentVersion}");
            return 1;
        }

        var previousValues = ProcessDll(Path.GetFullPath($"MaterialDesignThemes.{previousVersion}"));
        if (previousValues is null) return 1;
        var newValues = ProcessDll(Path.GetFullPath($"MaterialDesignThemes.{currentVersion}"));
        if (newValues is null) return 1;

        var previousValuesByName = new Dictionary<string, int>();
        foreach (var kvp in previousValues)
        {
            foreach (var aliases in kvp.Value.aliases)
            {
                previousValuesByName[aliases] = kvp.Key;
            }
        }
        var newValuesByName = new Dictionary<string, int>();
        foreach (var kvp in newValues)
        {
            foreach (var aliases in kvp.Value.aliases)
            {
                newValuesByName[aliases] = kvp.Key;
            }
        }

        var newItems = newValuesByName.Keys.Except(previousValuesByName.Keys)
            .OrderBy(x => x)
            .ToList();

        var removedItems = previousValuesByName.Keys.Except(newValuesByName.Keys)
            .OrderBy(x => x)
            .ToList();

        var visuallyChanged = newValuesByName.Keys.Intersect(previousValuesByName.Keys)
            .Where(key => newValues[newValuesByName[key]].path != previousValues[previousValuesByName[key]].path)
            .OrderBy(x => x)
            .ToList();

        console.Out.WriteLine("## Pack Icon Changes");
        console.Out.WriteLine($"### New icons ({newItems.Count})");
        foreach (var iconGroup in newItems.GroupBy(name => newValuesByName[name]))
        {
            console.Out.WriteLine($"- {string.Join(", ", iconGroup)}");
        }

        console.Out.WriteLine($"### Icons with visual changes ({visuallyChanged.Count})");
        foreach (var iconGroup in visuallyChanged.GroupBy(name => newValuesByName[name]))
        {
            console.Out.WriteLine($"- {string.Join(", ", iconGroup)}");
        }

        console.Out.WriteLine($"### Removed icons ({removedItems.Count})");
        foreach (var iconGroup in removedItems.GroupBy(name => previousValuesByName[name]))
        {
            console.Out.WriteLine($"- {string.Join(", ", iconGroup)}");
        }

        return 0;
    }

    private static IReadOnlyDictionary<int, (HashSet<string> aliases, string? path)>? ProcessDll(string directory)
    {
        FileInfo? dll = Directory.EnumerateFiles(directory, "MaterialDesignThemes.Wpf.dll", SearchOption.AllDirectories)
            .Select(file => new FileInfo(file))
            .FirstOrDefault(file => file.Directory?.Name.StartsWith("netcore", StringComparison.OrdinalIgnoreCase) ?? false);

        if (dll is null) return null;

        AssemblyLoadContext context = new AssemblyLoadContext(Guid.NewGuid().ToString(), true);

        Assembly assembly = context.LoadFromStream(dll.OpenRead());

        Type? packIconKind = assembly.GetType("MaterialDesignThemes.Wpf.PackIconKind");
        Type? packIconDataFactory = assembly.GetType("MaterialDesignThemes.Wpf.PackIconDataFactory");

        if (packIconKind is null) return null;
        if (packIconDataFactory is null) return null;

        var rv = new Dictionary<int, (HashSet<string>, string?)>();

        MethodInfo? createMethod = packIconDataFactory.GetMethod("Create", BindingFlags.InvokeMethod | BindingFlags.NonPublic | BindingFlags.Static);

        IDictionary? pathDictionary = (IDictionary?)createMethod?.Invoke(null, new object?[0]);

        if (pathDictionary is null) return null;

        foreach (string enumName in Enum.GetNames(packIconKind))
        {
            object @enum = Enum.Parse(packIconKind, enumName);
            if (rv.TryGetValue((int)@enum, out var found))
            {
                found.Item1.Add(enumName);
                continue;
            }

            string? path = (string?)pathDictionary[@enum];
            rv[(int)@enum] = (new HashSet<string> { enumName }, path);
        }

        context.Unload();

        return rv;
    }

    private static bool TryGetRepoInfo(IConsole console, ref string? repoOwner, ref string? repoName)
    {
        repoOwner ??= Environment.GetEnvironmentVariable("GitHubOwner");
        if (string.IsNullOrWhiteSpace(repoOwner))
        {
            console.Error.WriteLine("Repository owner is required");
            return false;
        }
        repoName ??= Environment.GetEnvironmentVariable("GitHubRepo");
        if (string.IsNullOrWhiteSpace(repoName))
        {
            console.Error.WriteLine("Repository name is required");
            return false;
        }
        return true;
    }
}

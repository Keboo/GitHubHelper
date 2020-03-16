using Octokit;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.IO;
using System.CommandLine.Parsing;
using System.CommandLine.Rendering;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GitHubHelper
{

    class Program
    {
        public static async Task<int> Main(string[] args)
        {
            Command contributors = new Command("contibutors")
                .ConfigureFromMethod<IConsole, string, string?, string?, string?>(MilestoneContributors);
            Command createdFiles = new Command("created")
            {
                new Command("projects").ConfigureFromMethod<IConsole, DateTimeOffset, string?, string?, string?>(CreatedFiles)
            };

            return await new CommandLineBuilder()
                //.ConfigureHelpFromXmlComments(method, xmlDocsFilePath)
                .AddCommand(contributors)
                .AddCommand(createdFiles)
                .UseDefaults()
                .UseAnsiTerminalWhenAvailable()
                .Build()
                .InvokeAsync(args);
        }

        public static async Task<int> CreatedFiles(
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
            for(int i = 1; i <= 1 || commits.Any(); i++)
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
                            && string.Equals(file.Status, "added", StringComparison.OrdinalIgnoreCase))
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
            if (!TryGetRepoInfo(console, ref repoOwner, ref repoName))
            {
                return 1;
            }

            IGitHubClient github = GitHub.GetClient(accessToken);

            IReadOnlyList<Milestone> milestones = await github.Issue.Milestone.GetAllForRepository(repoOwner, repoName);

            Milestone githubMilestone = milestones.FirstOrDefault(x => x.Title == milestone);

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

            var ignoredUsers = new[] { "Keboo", "MDIX-SA" };

            console.Out.WriteLine("A big thank you to everyone who contributed (either logging issues or submitting PRs):");

            foreach (string user in users
                .Select(x => x.Login)
                .Except(ignoredUsers)
                .OrderBy(x => x))
            {
                console.Out.WriteLine($"@{user}");
            }

            return 0;
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
}

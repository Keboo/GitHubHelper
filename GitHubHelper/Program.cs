using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Linq;
using System.Threading.Tasks;
using Octokit;

namespace GitHubHelper
{
    class Program
    {
        static async Task<int> Main(IConsole console,
            string milestone,
            string? accessToken = null,
            string? repoOwner = null,
            string? repoName = null)
        {
            accessToken ??= Environment.GetEnvironmentVariable("GitHubAccessToken");
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                console.Error.WriteLine("Access token is required");
                return 1;
            }
            repoOwner ??= Environment.GetEnvironmentVariable("GitHubOwner");
            if (string.IsNullOrWhiteSpace(repoOwner))
            {
                console.Error.WriteLine("Repository owner is required");
                return 1;
            }
            repoName ??= Environment.GetEnvironmentVariable("GitHubRepo");
            if (string.IsNullOrWhiteSpace(repoName))
            {
                console.Error.WriteLine("Repository name is required");
                return 1;
            }

            IGitHubClient github = GetClient(accessToken);

            //IReadOnlyList<Release> releases = await github.Repository.Release.GetAll(repoOwner, repoName);
            //
            //Release githubRelease = releases.FirstOrDefault(x => x.Name?.EndsWith(release, StringComparison.OrdinalIgnoreCase) == true);
            //
            //if (githubRelease is null)
            //{
            //    return 2;
            //}

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

        private static IGitHubClient GetClient(string accessToken)
        {
            IClientFactory factory = new ClientFactory();
            return factory.GetClient(accessToken);
        }
    }

    public class UserComparer : IEqualityComparer<User>
    {
        public bool Equals(User x, User y) => x?.Id == y?.Id;

        public int GetHashCode(User obj) => obj.Id.GetHashCode();
    }
}

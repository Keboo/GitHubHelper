using Octokit;

namespace GitHubHelper
{
    public interface IClientFactory
    {
        IGitHubClient GetClient(string accessToken, string appName = "AutoReviewer");
    }
}
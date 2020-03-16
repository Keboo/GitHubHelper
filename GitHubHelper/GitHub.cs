using Octokit;
using System;

namespace GitHubHelper
{
    public static class GitHub
    {
        public static IGitHubClient GetClient(string? accessToken)
        {
            accessToken ??= Environment.GetEnvironmentVariable("GitHubAccessToken");
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new ArgumentException("Access token is required");
            }
            IClientFactory factory = new ClientFactory();
            return factory.GetClient(accessToken);
        }
    }
}

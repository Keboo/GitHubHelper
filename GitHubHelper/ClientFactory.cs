using System;
using System.Net.Http;
using Octokit;
using Octokit.Internal;

namespace GitHubHelper;

public class ClientFactory : IClientFactory
{
    private Func<HttpMessageHandler> MessageHandler { get; }

    public ClientFactory(Func<HttpMessageHandler>? messageHandler = null)
    {
        MessageHandler = messageHandler ?? HttpMessageHandlerFactory.CreateDefault;
    }

    public IGitHubClient GetClient(string accessToken, string appName = "GitHubHelper")
    {
        ArgumentNullException.ThrowIfNull(accessToken);
        ArgumentNullException.ThrowIfNull(appName);

        IHttpClient httpClient = new HttpClientAdapter(MessageHandler);
        IJsonSerializer jsonSerializer = new SimpleJsonSerializer();
        ICredentialStore credentialStore = new InMemoryCredentialStore(new Credentials(accessToken));
        var connection = new Connection(new ProductHeaderValue(appName), GitHubClient.GitHubApiUrl, credentialStore, httpClient, jsonSerializer);
        return new GitHubClient(connection);
    }
}

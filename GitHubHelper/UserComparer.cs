using System.Collections.Generic;
using Octokit;

namespace GitHubHelper;

public class UserComparer : IEqualityComparer<User>
{
    public bool Equals(User? x, User? y) => x?.Id == y?.Id;

    public int GetHashCode(User obj) => obj.Id.GetHashCode();
}

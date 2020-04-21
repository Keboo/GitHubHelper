using System;

namespace GitHubHelper
{
    public sealed class FromEnvVariableAttribute : DefaultValueAttribute
    {
        public string EnvironmentVariableName { get; }

        public FromEnvVariableAttribute(string environmentVariableName)
        {
            EnvironmentVariableName = environmentVariableName ?? throw new ArgumentNullException(nameof(environmentVariableName));
        }

        public override bool TryProvideValue(out object? value)
        {
            if (Environment.GetEnvironmentVariable(EnvironmentVariableName) is string environmentValue)
            {
                value = environmentValue;
                return true;
            }
            value = default;
            return false;
        }
    }
}
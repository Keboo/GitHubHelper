using System;

namespace GitHubHelper
{
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = true)]
    public abstract class DefaultValueAttribute : Attribute
    {
        //[return:NotNullIfTrue]
        public abstract bool TryProvideValue(out object? value);
    }
}
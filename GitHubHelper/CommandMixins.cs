using System;
using System.CommandLine;
using System.CommandLine.DragonFruit;
using System.Threading.Tasks;

namespace GitHubHelper
{
    public static class CommandMixins
    {
        public static Command ConfigureFromMethod<T1, T2, T3>(this Command command,
            Func<T1, T2, T3, Task<int>> action)
        {
            command.ConfigureFromMethod(action.Method, action.Target);
            return command;
        }

        public static Command ConfigureFromMethod<T1, T2, T3, T4, T5>(this Command command,
            Func<T1, T2, T3, T4, T5, Task<int>> action)
        {
            command.ConfigureFromMethod(action.Method, action.Target);
            return command;
        }

        public static Command ConfigureFromMethod<T1, T2, T3, T4, T5, T6>(this Command command,
            Func<T1, T2, T3, T4, T5, T6, Task<int>> action)
        {
            command.ConfigureFromMethod(action.Method, action.Target);
            return command;
        }
    }
}

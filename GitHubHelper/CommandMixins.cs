using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Binding;
using System.CommandLine.DragonFruit;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace GitHubHelper
{
    public static class CommandMixins
    {
        private static readonly string[] _argumentParameterNames =
        {
            "arguments",
            "argument",
            "args"
        };


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

        private static IEnumerable<Option> BuildOptions(this MethodInfo type)
        {
            var descriptor = HandlerDescriptor.FromMethodInfo(type);

            var omittedTypes = new[]
            {
                typeof(IConsole),
                typeof(InvocationContext),
                typeof(BindingContext),
                typeof(ParseResult),
                typeof(CancellationToken),
            };

            foreach (var option in descriptor.ParameterDescriptors
                .Where(d => !omittedTypes.Contains (d.Type))
                .Where(d => !_argumentParameterNames.Contains(d.ValueName))
                .Select(p => p.BuildOption()))
            {

                yield return option;
            }
        }

        private static void ConfigureFromMethod(
            this Command command,
            MethodInfo method,
            object? target = null)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            if (method == null)
            {
                throw new ArgumentNullException(nameof(method));
            }

            foreach (var option in method.BuildOptions())
            {
                command.AddOption(option);
            }

            if (method.GetParameters()
                .FirstOrDefault(p => _argumentParameterNames.Contains(p.Name)) is ParameterInfo argsParam)
            {
                var argument = new Argument
                {
                    ArgumentType = argsParam.ParameterType,
                    Name = argsParam.Name
                };

                if (argsParam.HasDefaultValue)
                {
                    if (argsParam.DefaultValue != null)
                    {
                        argument.SetDefaultValue(argsParam.DefaultValue);
                    }
                    else
                    {
                        argument.SetDefaultValueFactory(() => null);
                    }
                }

                command.AddArgument(argument);
            }

            command.Handler = CommandHandler.Create(method, target);
        }

        private class FooHandler : HandlerDescriptor
        {
            public static HandlerDescriptor Create(MethodInfo methodInfo, object? target = null)
            {
                return null!;
            }

            public override ICommandHandler GetCommandHandler()
            {
                var parameterBinders = ParameterDescriptors
                    .Select(parameterDescriptor => new ModelBinder(parameterDescriptor))
                    .ToList();

                if (_invocationTarget == null)
                {
                    var invocationTargetBinder =
                        _handlerMethodInfo.IsStatic
                            ? null
                            : new ModelBinder(_handlerMethodInfo.DeclaringType);

                    return new ModelBindingCommandHandler(
                        _handlerMethodInfo,
                        parameterBinders,
                        invocationTargetBinder);
                }
                else
                {
                    return new ModelBindingCommandHandler(
                        _handlerMethodInfo,
                        parameterBinders,
                        _invocationTarget);
                }
            }

            public override ModelDescriptor Parent => ModelDescriptor.FromType(_handlerMethodInfo.DeclaringType);

            protected override IEnumerable<ParameterDescriptor> InitializeParameterDescriptors() =>
                _handlerMethodInfo.GetParameters()
                    .Select(p => new ParameterDescriptor(p, this));
        }
    }
}

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

        public static Command ConfigureFromMethod<T1, T2, T3, T4, T5, T6, T7>(this Command command,
            Func<T1, T2, T3, T4, T5, T6, T7, Task<int>> action)
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
                .Where(d => !omittedTypes.Contains(d.ValueType))
                .Where(d => !_argumentParameterNames.Contains(d.ValueName))
                .Select(p => p.BuildOption()))
            {

                yield return option;
            }
        }

        //TODO This moves into API somehow
        private static readonly Func<ParameterDescriptor, ParameterInfo> _GetParameterInfo = BuildAccessor();
        private static Func<ParameterDescriptor, ParameterInfo> BuildAccessor()
        {
            FieldInfo fieldInfo = typeof(ParameterDescriptor).GetField("_parameterInfo", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.GetField)
                ?? throw new InvalidOperationException("Could not find _parameterInfo field");
            return x =>
            {
                return fieldInfo.GetValue(x) as ParameterInfo ?? throw new InvalidOperationException("Field was not ParameterInfo");
            };
        }

        private static Option BuildOption(this ParameterDescriptor parameter)
        {
            var argument = new Argument
            {
                ArgumentType = parameter.ValueType
            };

            ParameterInfo parameterInfo = _GetParameterInfo(parameter);

            List<DefaultValueAttribute> attributes =
                parameterInfo.GetCustomAttributes<DefaultValueAttribute>()
                .ToList();

            if (attributes.Any() || parameter.HasDefaultValue)
            {
                argument.SetDefaultValueFactory(() =>
                {
                    foreach (DefaultValueAttribute attribute in attributes)
                    { 
                        if (attribute.TryProvideValue(out object? defaultValue))
                        {
                            return defaultValue;
                        }
                    }
                    return parameter.GetDefaultValue();
                });
            }

            var option = new Option(
                parameter.BuildAlias(),
                parameter.ValueName)
            {
                Argument = argument
            };

            return option;
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
    }
}

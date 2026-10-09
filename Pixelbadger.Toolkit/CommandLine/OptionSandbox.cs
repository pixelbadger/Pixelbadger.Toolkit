using System.CommandLine;
using System.CommandLine.Parsing;
using System.CommandLine.Help;
using System.Reflection;

namespace Pixelbadger.Toolkit.CommandLine;

/// <summary>Validates stored/supplied tokens by parsing them as the real option of the real action, and typed-option helpers.</summary>
internal static class OptionSandbox
{
    internal sealed record Result(ParseResult Parse, IReadOnlyList<string> Errors);

    /// <summary>
    /// Parses <c>[topic, ...path, option, ...tokens]</c> against the real tree and keeps only the errors that belong to
    /// <paramref name="real"/> (its OptionResult, or an ArgumentResult beneath it). Everything else, such as missing
    /// required options, is ignored.
    /// </summary>
    public static Result Validate(RootCommand root, string topic, string[] path, Option real, IReadOnlyList<string> tokens)
    {
        var args = new List<string> { topic };
        args.AddRange(path);
        args.Add(real.Name);
        args.AddRange(tokens);

        var parse = root.Parse(args.ToArray());
        var errors = new List<string>();
        foreach (var error in parse.Errors)
        {
            var symbol = error.SymbolResult;
            var belongs = symbol switch
            {
                OptionResult o => ReferenceEquals(o.Option, real),
                ArgumentResult a => a.Parent is OptionResult po && ReferenceEquals(po.Option, real),
                _ => false
            };
            if (belongs)
                errors.Add(error.Message);
        }

        return new Result(parse, errors);
    }

    public static object? GetValue(ParseResult parse, Option option) =>
        (object?)typeof(OptionSandbox).GetMethod(nameof(GetValueCore), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(option.ValueType)
            .Invoke(null, [parse, option]);

    private static object? GetValueCore<T>(ParseResult parse, Option option) => parse.GetValue((Option<T>)option);

    /// <summary>Gets the option's current default-value factory (null when none), as an untyped delegate.</summary>
    public static Delegate? GetFactory(Option option) => (Delegate?)Call(nameof(GetFactoryCore), option, []);

    public static void SetFactory(Option option, Delegate? factory) => Call(nameof(SetFactoryCore), option, [factory]);

    /// <summary>Installs a factory that returns <paramref name="value"/>.</summary>
    public static void SetConstant(Option option, object? value) => Call(nameof(SetConstantCore), option, [value]);

    /// <summary>Installs a factory that fails the parse of the option with <paramref name="message"/>.</summary>
    public static void SetError(Option option, string message) => Call(nameof(SetErrorCore), option, [message]);

    private static object? Call(string method, Option option, object?[] extra)
    {
        var args = new object?[extra.Length + 1];
        args[0] = option;
        extra.CopyTo(args, 1);
        return typeof(OptionSandbox).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(option.ValueType)
            .Invoke(null, args);
    }

    private static object? GetFactoryCore<T>(Option option) => ((Option<T>)option).DefaultValueFactory;

    private static object? SetFactoryCore<T>(Option option, Delegate? factory)
    {
        ((Option<T>)option).DefaultValueFactory = (Func<ArgumentResult, T>?)factory;
        return null;
    }

    private static object? SetConstantCore<T>(Option option, object? value)
    {
        var typed = (T)value!;
        ((Option<T>)option).DefaultValueFactory = _ => typed;
        return null;
    }

    private static object? SetErrorCore<T>(Option option, string message)
    {
        var typed = (Option<T>)option;
        var original = typed.DefaultValueFactory;
        typed.DefaultValueFactory = result =>
        {
            result.AddError(message);
            return original is null ? default! : original(result);
        };
        return null;
    }
}

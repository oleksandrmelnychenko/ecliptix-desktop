namespace Ecliptix.Utilities;

public static class OptionExtensions
{
    public static Option<T> ToOption<T>(this T? value) where T : class =>
        value is not null ? Option<T>.Some(value) : Option<T>.None;

    public static T? ToNullable<T>(this Option<T> option) where T : class => option.IsSome ? option.Value : null;

    public static Option<T> Where<T>(this Option<T> option, Func<T, bool> predicate) =>
        option.IsSome && predicate(option.Value!) ? option : Option<T>.None;

    public static Option<TResult> Select<T, TResult>(this Option<T> option, Func<T, TResult> selector) =>
        option.Map(selector);

    public static Option<TResult> Bind<T, TResult>(this Option<T> option, Func<T, Option<TResult>> binder) =>
        option.IsSome ? binder(option.Value!) : Option<TResult>.None;

    public static T GetValueOrDefault<T>(this Option<T> option, T defaultValue) => option.ValueOr(defaultValue);

    public static T GetValueOrDefault<T>(this Option<T> option, Func<T> defaultFactory) => option.IsSome ? option.Value! : defaultFactory();

    public static void Do<T>(this Option<T> option, Action<T> action)
    {
        if (option.IsSome)
        {
            action(option.Value!);
        }
    }

    public static Option<T> Or<T>(this Option<T> option, Func<Option<T>> alternativeFactory) => option.IsSome ? option : alternativeFactory();
}

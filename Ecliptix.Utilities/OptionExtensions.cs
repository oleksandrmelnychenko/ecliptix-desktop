namespace Ecliptix.Utilities;

public static class OptionExtensions
{
    public static Option<T> ToOption<T>(this T? value) where T : class =>
        value is not null ? Option<T>.Some(value) : Option<T>.None;

    public static T? ToNullable<T>(this Option<T> option) where T : class => option.IsSome ? option.Value : null;

    extension<T>(Option<T> option)
    {
        public Option<T> Where(Func<T, bool> predicate) =>
            option.IsSome && predicate(option.Value!) ? option : Option<T>.None;

        public Option<TResult> Select<TResult>(Func<T, TResult> selector) =>
            option.Map(selector);

        public Option<TResult> Bind<TResult>(Func<T, Option<TResult>> binder) =>
            option.IsSome ? binder(option.Value!) : Option<TResult>.None;

        public T GetValueOrDefault(T defaultValue) => option.ValueOr(defaultValue);
        public T GetValueOrDefault(Func<T> defaultFactory) => option.IsSome ? option.Value! : defaultFactory();

        public void Do(Action<T> action)
        {
            if (option.IsSome)
            {
                action(option.Value!);
            }
        }

        public Option<T> Or(Func<Option<T>> alternativeFactory) => option.IsSome ? option : alternativeFactory();
    }
}

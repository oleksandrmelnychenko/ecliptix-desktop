namespace Ecliptix.Utilities;


public static class OptionExtensions
{
    public static Option<T> ToOption<T>(this T? value) where T : class
    {
        return value is not null ? Option<T>.Some(value) : Option<T>.None;
    }

    public static Option<T> ToOption<T>(this T? value) where T : struct
    {
        return value.HasValue ? Option<T>.Some(value.Value) : Option<T>.None;
    }

    public static T? ToNullable<T>(this Option<T> option) where T : class
    {
        return option.HasValue ? option.Value : null;
    }

    public static T? ToNullableStruct<T>(this Option<T> option) where T : struct
    {
        return option.HasValue ? option.Value : null;
    }

    public static Option<T> Where<T>(this Option<T> option, Func<T, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return option.HasValue && predicate(option.Value!) ? option : Option<T>.None;
    }

    public static Option<TResult> Select<T, TResult>(this Option<T> option, Func<T, TResult> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return option.Map(selector);
    }

    public static Option<TResult> Bind<T, TResult>(this Option<T> option, Func<T, Option<TResult>> binder)
    {
        ArgumentNullException.ThrowIfNull(binder);
        return option.HasValue ? binder(option.Value!) : Option<TResult>.None;
    }

    public static Option<TResult> SelectMany<T, TResult>(this Option<T> option, Func<T, Option<TResult>> selector)
    {
        return option.Bind(selector);
    }

    public static Option<TResult> SelectMany<T, TIntermediate, TResult>(
        this Option<T> option,
        Func<T, Option<TIntermediate>> intermediateSelector,
        Func<T, TIntermediate, TResult> resultSelector)
    {
        ArgumentNullException.ThrowIfNull(intermediateSelector);
        ArgumentNullException.ThrowIfNull(resultSelector);

        return option.Bind(x =>
            intermediateSelector(x).Select(y =>
                resultSelector(x, y)));
    }

    public static T GetValueOrDefault<T>(this Option<T> option, T defaultValue)
    {
        return option.ValueOr(defaultValue);
    }

    public static T GetValueOrDefault<T>(this Option<T> option, Func<T> defaultFactory)
    {
        ArgumentNullException.ThrowIfNull(defaultFactory);
        return option.HasValue ? option.Value! : defaultFactory();
    }

    public static Option<T> Do<T>(this Option<T> option, Action<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (option.HasValue)
        {
            action(option.Value!);
        }
        return option;
    }

    public static Result<T, TError> ToResult<T, TError>(this Option<T> option, TError errorWhenNone) where TError : notnull
    {
        ArgumentNullException.ThrowIfNull(errorWhenNone);
        return option.HasValue
            ? Result<T, TError>.Ok(option.Value!)
            : Result<T, TError>.Err(errorWhenNone);
    }

    public static Result<T, TError> ToResult<T, TError>(this Option<T> option, Func<TError> errorFactory) where TError : notnull
    {
        ArgumentNullException.ThrowIfNull(errorFactory);
        return option.HasValue
            ? Result<T, TError>.Ok(option.Value!)
            : Result<T, TError>.Err(errorFactory());
    }

    public static Option<TResult> Zip<T1, T2, TResult>(
        this Option<T1> option1,
        Option<T2> option2,
        Func<T1, T2, TResult> combiner)
    {
        ArgumentNullException.ThrowIfNull(combiner);
        return option1.HasValue && option2.HasValue
            ? Option<TResult>.Some(combiner(option1.Value!, option2.Value!))
            : Option<TResult>.None;
    }

    public static Option<T> Or<T>(this Option<T> option, Option<T> alternative)
    {
        return option.HasValue ? option : alternative;
    }

    public static Option<T> Or<T>(this Option<T> option, Func<Option<T>> alternativeFactory)
    {
        ArgumentNullException.ThrowIfNull(alternativeFactory);
        return option.HasValue ? option : alternativeFactory();
    }

    public static Option<T> Flatten<T>(this Option<Option<T>> nestedOption)
    {
        return nestedOption.HasValue ? nestedOption.Value! : Option<T>.None;
    }

    public static Option<IEnumerable<T>> Sequence<T>(this IEnumerable<Option<T>> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<T> results = new();
        foreach (Option<T> option in options)
        {
            if (!option.HasValue)
            {
                return Option<IEnumerable<T>>.None;
            }
            results.Add(option.Value!);
        }

        return Option<IEnumerable<T>>.Some(results);
    }

    public static IEnumerable<T> Choose<T>(this IEnumerable<Option<T>> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        foreach (Option<T> option in options)
        {
            if (option.HasValue)
            {
                yield return option.Value!;
            }
        }
    }
}

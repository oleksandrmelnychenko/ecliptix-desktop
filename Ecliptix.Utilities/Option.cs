
namespace Ecliptix.Utilities;

public readonly record struct Option<T>
{
    private Option(bool isSome, T? value)
    {
        IsSome = isSome;
        Value = value;
    }

    public bool IsSome { get; }
    public T? Value { get; }

    public static Option<T> None => new(false, default);

    public static Option<T> Some(T value)
    {
        return new Option<T>(true, value);
    }

    public TResult Match<TResult>(Func<T, TResult> onSome, Func<TResult> onNone)
    {
        return IsSome ? onSome(Value!) : onNone();
    }

    public void Match(Action<T> onSome, Action onNone)
    {
        if (IsSome)
        {
            onSome(Value!);
        }
        else
        {
            onNone();
        }
    }

    public T ValueOr(T fallback)
    {
        return IsSome ? Value! : fallback;
    }

    public Option<TResult> Map<TResult>(Func<T, TResult> selector)
    {
        return IsSome ? Option<TResult>.Some(selector(Value!)) : Option<TResult>.None;
    }

    public static Option<T> From(T? value)
    {
        return value is not null ? Some(value) : None;
    }
}

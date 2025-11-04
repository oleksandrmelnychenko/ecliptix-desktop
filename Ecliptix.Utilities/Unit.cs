namespace Ecliptix.Utilities;

public readonly struct Unit : IEquatable<Unit>
{
    public static readonly Unit Value = new();

    public bool Equals(Unit other)
    {
        return true;
    }

    public override bool Equals(object? obj)
    {
        return obj is Unit;
    }

    public override int GetHashCode()
    {
        return UtilityConstants.UnitType.HASH_CODE;
    }

    public static bool operator ==(Unit left, Unit right)
    {
        _ = left;
        _ = right;
        return true;
    }

    public static bool operator !=(Unit left, Unit right)
    {
        _ = left;
        _ = right;
        return false;
    }

    public override string ToString()
    {
        return UtilityConstants.UnitType.STRING_REPRESENTATION;
    }
}

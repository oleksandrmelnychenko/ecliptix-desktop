# Microsoft C# Class Member Ordering Convention

This document describes the official Microsoft C# convention for ordering class members.

## Member Order

Members should be ordered in the following sequence:

### 1. **Constant Fields**
```csharp
public const int MAX_BUFFER_SIZE = 1024;
private const string DEFAULT_NAME = "Unknown";
```

### 2. **Static Fields**
```csharp
public static readonly ILogger Logger = LoggerFactory.Create();
private static int _instanceCount;
```

### 3. **Instance Fields**
```csharp
private readonly IService _service;
private readonly string _name;
private int _count;
private bool _isInitialized;
```

### 4. **Constructors**
```csharp
public MyClass() { }
public MyClass(IService service) { }
private MyClass(string name, IService service) { }
```

### 5. **Finalizers (Destructors)**
```csharp
~MyClass()
{
    // Cleanup
}
```

### 6. **Delegates**
```csharp
public delegate void EventHandler(object sender, EventArgs e);
```

### 7. **Events**
```csharp
public event EventHandler? DataChanged;
private event Action? _internalEvent;
```

### 8. **Enums**
```csharp
public enum Status
{
    PENDING,
    ACTIVE,
    COMPLETED
}
```

### 9. **Interfaces** (nested)
```csharp
public interface IInternalService
{
    void Execute();
}
```

### 10. **Properties**
```csharp
public string Name { get; set; }
public int Count { get; private set; }
private bool IsValid => _count > 0;
```

### 11. **Indexers**
```csharp
public object this[int index]
{
    get => _items[index];
    set => _items[index] = value;
}
```

### 12. **Methods**
```csharp
public void Initialize() { }
public string GetName() => _name;
protected virtual void OnDataChanged() { }
private void Cleanup() { }
```

### 13. **Structs** (nested)
```csharp
public struct Point
{
    public int X { get; set; }
    public int Y { get; set; }
}
```

### 14. **Nested Classes**
```csharp
public class Configuration
{
    public string Setting { get; set; }
}

private class InternalHelper
{
    public void Help() { }
}
```

## Access Modifier Order

Within each member type category, order by access modifier:

1. `public`
2. `internal`
3. `protected internal`
4. `protected`
5. `private protected`
6. `private`

## Additional Modifier Order

Within each access level, order by additional modifiers:

1. `static`
2. `instance` (no modifier)
3. `readonly`
4. `non-readonly`

## Complete Example

```csharp
public class ExampleClass : IDisposable
{
    // 1. Constants
    public const int MAX_SIZE = 100;
    private const string DEFAULT_VALUE = "default";

    // 2. Static fields
    public static readonly ILogger Logger = LoggerFactory.Create();
    private static int _instanceCount;

    // 3. Instance fields
    private readonly IService _service;
    private readonly string _name;
    private int _count;
    private bool _disposed;

    // 4. Constructors
    public ExampleClass(IService service, string name)
    {
        _service = service;
        _name = name;
        _instanceCount++;
    }

    // 5. Finalizer
    ~ExampleClass()
    {
        Dispose(false);
    }

    // 7. Events
    public event EventHandler? DataChanged;

    // 10. Properties
    public string Name => _name;
    public int Count
    {
        get => _count;
        set => _count = value;
    }
    private bool IsValid => _count > 0;

    // 12. Methods - public
    public void Initialize()
    {
        // Implementation
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    // 12. Methods - protected
    protected virtual void OnDataChanged()
    {
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    // 12. Methods - private
    private void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            // Dispose managed resources
        }

        _disposed = true;
    }

    // 14. Nested classes
    private class Helper
    {
        public void DoWork() { }
    }
}
```

## Enforcement

The StyleCop analyzers SA1201, SA1202, SA1203, SA1204, and SA1214 are enabled in `.editorconfig` to enforce this ordering convention at the warning level.

## References

- [Microsoft C# Coding Conventions](https://docs.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- [StyleCop Ordering Rules](https://github.com/DotNetAnalyzers/StyleCopAnalyzers/blob/master/documentation/OrderingRules.md)

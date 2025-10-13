# Dead Code Removal - Implementation Patches

This document provides specific patches for removing identified dead code.

---

## Patch 1: Delete PerformanceProfiler.cs

**File to Delete:** `/Users/oleksandrmelnychenko/RiderProjects/ecliptix-desktop/Ecliptix.Protocol.System/Core/PerformanceProfiler.cs`

**Command:**
```bash
rm /Users/oleksandrmelnychenko/RiderProjects/ecliptix-desktop/Ecliptix.Protocol.System/Core/PerformanceProfiler.cs
```

**Verification:**
```bash
# Should return nothing:
grep -r "PerformanceProfiler" --include="*.cs" --exclude-dir={bin,obj}
```

---

## Patch 2: Delete CircuitBreaker.cs

**File to Delete:** `/Users/oleksandrmelnychenko/RiderProjects/ecliptix-desktop/Ecliptix.Protocol.System/Core/CircuitBreaker.cs`

**Command:**
```bash
rm /Users/oleksandrmelnychenko/RiderProjects/ecliptix-desktop/Ecliptix.Protocol.System/Core/CircuitBreaker.cs
```

**Verification:**
```bash
# Should return nothing:
grep -r "CircuitBreaker" --include="*.cs" --exclude-dir={bin,obj}
```

**Note:** This will also affect failure messages. Check if `EcliptixProtocolFailureMessages.CircuitBreaker` is referenced elsewhere:
```bash
grep -r "EcliptixProtocolFailureMessages.CircuitBreaker" --include="*.cs" --exclude-dir={bin,obj}
```
If messages class has unused CircuitBreaker section, remove that too.

---

## Patch 3: Remove SwapBytes Method

**File:** `/Users/oleksandrmelnychenko/RiderProjects/ecliptix-desktop/Ecliptix.Utilities/Helpers.cs`
**Lines to Remove:** 79-82

**Current Code (lines 79-82):**
```csharp
    private static void SwapBytes(Span<byte> bytes, int i, int j)
    {
        (bytes[i], bytes[j]) = (bytes[j], bytes[i]);
    }
```

**Action:** Delete lines 79-82 inclusive

**Context (what comes before line 79):**
```csharp
        Console.WriteLine($"[CLIENT-BYTES-TO-GUID] Bytes: {Convert.ToHexString(bytesOriginal)}, GUID: {result}");

        return result;
    }

    private static void SwapBytes(Span<byte> bytes, int i, int j)  // <-- DELETE FROM HERE
    {
        (bytes[i], bytes[j]) = (bytes[j], bytes[i]);
    }  // <-- DELETE TO HERE

    public static uint GenerateRandomUInt32InRange(uint min, uint max)
```

---

## Patch 4: Remove Console.WriteLine Debug Statements

**File:** `/Users/oleksandrmelnychenko/RiderProjects/ecliptix-desktop/Ecliptix.Utilities/Helpers.cs`

### Patch 4a: Remove line 58

**Current Code (lines 50-61):**
```csharp
    public static ByteString GuidToByteString(Guid guid)
    {
        byte[] bytes = guid.ToByteArray();

        Array.Reverse(bytes, 0, 4);
        Array.Reverse(bytes, 4, 2);
        Array.Reverse(bytes, 6, 2);

        Console.WriteLine($"[CLIENT-GUID-TO-BYTES] GUID: {guid}, Bytes: {Convert.ToHexString(bytes)}");  // <-- REMOVE THIS LINE

        return ByteString.CopyFrom(bytes);
    }
```

**After Removal:**
```csharp
    public static ByteString GuidToByteString(Guid guid)
    {
        byte[] bytes = guid.ToByteArray();

        Array.Reverse(bytes, 0, 4);
        Array.Reverse(bytes, 4, 2);
        Array.Reverse(bytes, 6, 2);

        return ByteString.CopyFrom(bytes);
    }
```

### Patch 4b: Remove line 74

**Current Code (lines 63-77):**
```csharp
    public static Guid FromByteStringToGuid(ByteString byteString)
    {
        byte[] bytesOriginal = byteString.ToByteArray();
        byte[] bytes = (byte[])bytesOriginal.Clone();

        Array.Reverse(bytes, 0, 4);
        Array.Reverse(bytes, 4, 2);
        Array.Reverse(bytes, 6, 2);

        Guid result = new Guid(bytes);

        Console.WriteLine($"[CLIENT-BYTES-TO-GUID] Bytes: {Convert.ToHexString(bytesOriginal)}, GUID: {result}");  // <-- REMOVE THIS LINE

        return result;
    }
```

**After Removal:**
```csharp
    public static Guid FromByteStringToGuid(ByteString byteString)
    {
        byte[] bytesOriginal = byteString.ToByteArray();
        byte[] bytes = (byte[])bytesOriginal.Clone();

        Array.Reverse(bytes, 0, 4);
        Array.Reverse(bytes, 4, 2);
        Array.Reverse(bytes, 6, 2);

        Guid result = new Guid(bytes);

        return result;
    }
```

---

## Complete Bash Script for Automated Removal

**File:** `remove_dead_code.sh`

```bash
#!/bin/bash

# Dead Code Removal Script
# Run from project root: /Users/oleksandrmelnychenko/RiderProjects/ecliptix-desktop

set -e  # Exit on error

PROJECT_ROOT="/Users/oleksandrmelnychenko/RiderProjects/ecliptix-desktop"
cd "$PROJECT_ROOT"

echo "=== Ecliptix Dead Code Removal ==="
echo ""

# Backup first
echo "Creating backup branch..."
git checkout -b dead-code-removal-backup-$(date +%Y%m%d-%H%M%S) || true
git checkout main  # or develop, depending on your workflow

echo ""
echo "Step 1: Removing PerformanceProfiler.cs..."
if [ -f "Ecliptix.Protocol.System/Core/PerformanceProfiler.cs" ]; then
    git rm "Ecliptix.Protocol.System/Core/PerformanceProfiler.cs"
    echo "✓ PerformanceProfiler.cs removed"
else
    echo "⚠ PerformanceProfiler.cs not found (may already be removed)"
fi

echo ""
echo "Step 2: Removing CircuitBreaker.cs..."
if [ -f "Ecliptix.Protocol.System/Core/CircuitBreaker.cs" ]; then
    git rm "Ecliptix.Protocol.System/Core/CircuitBreaker.cs"
    echo "✓ CircuitBreaker.cs removed"
else
    echo "⚠ CircuitBreaker.cs not found (may already be removed)"
fi

echo ""
echo "Step 3: Manual edits required for Helpers.cs"
echo "File: Ecliptix.Utilities/Helpers.cs"
echo ""
echo "Please manually:"
echo "  1. Remove SwapBytes method (lines 79-82)"
echo "  2. Remove Console.WriteLine at line 58"
echo "  3. Remove Console.WriteLine at line 74"
echo ""
echo "Then run: git add Ecliptix.Utilities/Helpers.cs"
echo ""

echo "Step 4: Building solution..."
dotnet build

if [ $? -eq 0 ]; then
    echo "✓ Build successful"
    echo ""
    echo "Step 5: Running tests..."
    dotnet test

    if [ $? -eq 0 ]; then
        echo "✓ Tests passed"
        echo ""
        echo "=== Ready to Commit ==="
        echo ""
        echo "Review changes with: git status"
        echo "Commit with: git add . && git commit -m 'refactor: remove unused code'"
    else
        echo "✗ Tests failed - review changes"
        exit 1
    fi
else
    echo "✗ Build failed - review changes"
    exit 1
fi
```

---

## Manual Removal Steps (If Not Using Script)

### Step 1: Delete Files
```bash
cd /Users/oleksandrmelnychenko/RiderProjects/ecliptix-desktop

# Remove PerformanceProfiler
rm Ecliptix.Protocol.System/Core/PerformanceProfiler.cs

# Remove CircuitBreaker
rm Ecliptix.Protocol.System/Core/CircuitBreaker.cs
```

### Step 2: Edit Helpers.cs
Open `/Users/oleksandrmelnychenko/RiderProjects/ecliptix-desktop/Ecliptix.Utilities/Helpers.cs`

1. Delete lines 79-82 (SwapBytes method)
2. Delete line 58 (Console.WriteLine in GuidToByteString)
3. Delete line 74 (Console.WriteLine in FromByteStringToGuid)

### Step 3: Build and Test
```bash
# Build
dotnet build

# Run tests
dotnet test

# Check for any compiler errors
```

### Step 4: Verify Removal
```bash
# Should return no results:
grep -r "PerformanceProfiler" --include="*.cs" --exclude-dir={bin,obj}
grep -r "new CircuitBreaker" --include="*.cs" --exclude-dir={bin,obj}
grep -r "SwapBytes" --include="*.cs" --exclude-dir={bin,obj}

# Should return no results in Utilities:
grep -r "Console.WriteLine" --include="*.cs" Ecliptix.Utilities/ --exclude-dir={bin,obj}
```

### Step 5: Commit
```bash
git add .
git commit -m "refactor: remove unused code and debug statements

- Remove PerformanceProfiler class (183 lines, zero usage)
- Remove CircuitBreaker class (190 lines, not integrated)
- Remove unused SwapBytes private method
- Remove debug Console.WriteLine statements

This cleanup reduces the codebase by ~380 lines of dead code
with zero impact on functionality."
```

---

## Post-Removal Verification Checklist

- [ ] Build succeeds: `dotnet build`
- [ ] Tests pass: `dotnet test`
- [ ] No references to PerformanceProfiler: `grep -r "PerformanceProfiler"`
- [ ] No references to CircuitBreaker instantiation: `grep -r "new CircuitBreaker"`
- [ ] No references to SwapBytes: `grep -r "SwapBytes"`
- [ ] No Console.WriteLine in Utilities: `grep "Console.WriteLine" Ecliptix.Utilities/Helpers.cs`
- [ ] Application runs correctly
- [ ] All authentication flows work
- [ ] Network operations function properly

---

## Rollback Procedure

If issues arise after removal:

```bash
# Rollback all changes
git reset --hard HEAD~1

# Or rollback specific file
git checkout HEAD~1 -- Ecliptix.Protocol.System/Core/PerformanceProfiler.cs
git checkout HEAD~1 -- Ecliptix.Protocol.System/Core/CircuitBreaker.cs
git checkout HEAD~1 -- Ecliptix.Utilities/Helpers.cs
```

---

## Additional Notes

### Why These Are Safe to Remove

1. **PerformanceProfiler**: Zero references in entire codebase
2. **CircuitBreaker**: No instantiation, only type name in unrelated contexts
3. **SwapBytes**: Private method never called
4. **Console.WriteLine**: Debug code that should never be in production

### Future Considerations

- If performance profiling is needed, consider using BenchmarkDotNet
- If circuit breaker pattern is needed, consider Polly library (already in use)
- For debugging, always use Serilog instead of Console.WriteLine

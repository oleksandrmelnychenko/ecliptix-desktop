# Instructions to Clear Incompatible Protocol State

The nonce format change makes all existing protocol state incompatible. You must clear state on BOTH server and client.

## Why This Happened
The old ASP.NET nonce format was fundamentally broken:
- **OLD FORMAT**: `[counter:8bytes][random:4bytes]` (counter overwrote random data)
- **NEW FORMAT**: `[random:8bytes][counter:4bytes]` (correct implementation)

These formats are cryptographically incompatible - messages encrypted with one cannot be decrypted with the other.

## Server Side (ASP.NET) - REQUIRED

1. **Stop the ASP.NET server**

2. **Run the SQL script** against your `EcliptixMemberships` database:
   ```bash
   # The script is at: /Users/oleksandrmelnychenko/RiderProjects/Ecliptix/clear_protocol_state.sql
   # Run it using your SQL Server client against server 78.152.175.67
   ```

3. **Restart the ASP.NET server**

## Client Side (Desktop) - REQUIRED

Clear the locally stored protocol state:

### Option 1: Clear via Code (Temporary)
Add this to `ApplicationInitializer.cs` before line 100:
```csharp
// TEMPORARY: Clear old incompatible state
await secureStorageProvider.DeleteAsync(connectId.ToString());
```

### Option 2: Clear Storage Manually
The secure storage is typically in:
- **Windows**: `%LOCALAPPDATA%\Ecliptix\`
- **macOS**: `~/Library/Application Support/Ecliptix/`
- **Linux**: `~/.config/Ecliptix/`

Delete the secure storage files or the entire directory.

### Option 3: Force New Connect ID
Change the connect ID calculation to force a new channel:
```csharp
// In ApplicationInitializer.cs around line 93
uint connectId = NetworkProvider.ComputeUniqueConnectId(applicationInstanceSettings,
    PubKeyExchangeType.DataCenterEphemeralConnect) + 1; // Add offset to force new ID
```

## Verification

After clearing state on both sides:
1. Start the ASP.NET server
2. Start the Desktop client
3. The client should establish a NEW secure channel
4. You should see: "Establishing new secrecy channel" in the logs
5. All subsequent messages should encrypt/decrypt successfully

## Important Note
This is a ONE-TIME fix needed because the nonce format was fundamentally changed. Future updates won't require this as long as the format remains consistent.
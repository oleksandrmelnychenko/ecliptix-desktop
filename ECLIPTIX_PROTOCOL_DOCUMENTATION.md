# Ecliptix Protocol Documentation

## Overview

Ecliptix is a secure communication protocol that provides end-to-end encrypted messaging with strong authentication and forward secrecy. The protocol combines multiple cryptographic primitives to establish secure sessions and authenticate users without transmitting passwords.

**Version**: 1.0
**Security Level**: Military-grade encryption
**Key Features**:
- SSL Certificate Pinning
- Forward Secrecy (X25519 ECDH)
- Zero-Knowledge Authentication (OPAQUE)
- Post-Compromise Security
- Hardware-Backed Key Storage (when available)

---

## Protocol Flow Overview

```
┌─────────────┐                                    ┌─────────────┐
│   CLIENT    │                                    │   SERVER    │
│  (Desktop)  │                                    │  (Backend)  │
└─────────────┘                                    └─────────────┘
      │                                                    │
      │  Phase 1: SSL Pinning + RSA Key Exchange          │
      ├───────────────────────────────────────────────────>│
      │                                                    │
      │  Phase 2: X25519 ECDH Key Agreement               │
      ├<──────────────────────────────────────────────────>│
      │                                                    │
      │  Phase 3: Session Key Derivation (HKDF)           │
      │                                                    │
      │  Phase 4: AES-256-GCM Encrypted Channel           │
      ├<══════════════════════════════════════════════════>│
      │                                                    │
      │  Phase 5: OPAQUE Authentication                   │
      ├<──────────────────────────────────────────────────>│
      │                                                    │
      │  Phase 6: Master Key Derivation                   │
      │                                                    │
      │  Phase 7: Shamir Secret Sharing                   │
      │                                                    │
      │  Phase 8: Authenticated Session                   │
      ├<══════════════════════════════════════════════════>│
```

---

## Phase 1: Certificate Pinning (Application Layer)

**Purpose**: Application-level server identity verification on top of TLS

### Overview

Ecliptix uses **application-layer certificate pinning** (`Ecliptix.SecurityCertificate.Pinning`) that operates above the TLS layer. This is not TLS certificate pinning, but rather an additional verification step performed by the application after the TLS connection is established.

**Note**: TLS 1.3 is used by default for transport security (gRPC over HTTPS), but certificate pinning is an application-level security mechanism.

### Process

1. **Client** initiates gRPC connection over TLS (default)
2. **Client** requests server's pinned certificate
3. **Server** provides its pinned certificate/public key
4. **Client** validates against locally stored pinned certificate
5. **Client** verifies certificate fingerprint (SHA-256)

### Security Properties
- **Additional layer** on top of TLS
- Protection against certificate authority compromise
- Prevention of MITM attacks at application level
- Server identity verification independent of PKI
- Pinned certificates embedded in application

### Implementation
- **Module**: `Ecliptix.SecurityCertificate.Pinning`
- **Client**: `NetworkProvider.cs` - Certificate validation
- **Storage**: Pinned certificates stored in application resources

```
Client                                    Server
  │                                          │
  │  1. gRPC over TLS (default transport)    │
  ├<════════════════════════════════════════>│
  │                                          │
  │  2. Request Pinned Certificate           │
  ├─────────────────────────────────────────>│
  │                                          │
  │  3. Server Pinned Certificate            │
  │<─────────────────────────────────────────┤
  │                                          │
  │  4. Validate Against Local Pin           │
  │     • Load embedded certificate          │
  │     • Compute SHA-256 fingerprint        │
  │     • Compare with received cert         │
  │                                          │
  │  5. Certificate Pinning Verified ✓       │
```

### Certificate Pinning Properties

**Pinned Certificate Bundle**:
```
{
  "Version": 1,
  "CertificateHash": "SHA256(DER-encoded certificate)",
  "PublicKeyHash": "SHA256(public key bytes)",
  "ValidFrom": "2025-01-01T00:00:00Z",
  "ValidUntil": "2026-01-01T00:00:00Z",
  "Subject": "CN=ecliptix.server",
  "Algorithm": "RSA-4096 or Ed25519"
}
```

**Verification Steps**:
1. Load pinned certificate from application resources
2. Extract public key from received certificate
3. Compute SHA-256 hash of public key
4. Compare with pinned hash (constant-time comparison)
5. Verify certificate expiration dates
6. Accept connection only if all checks pass

---

## Phase 2: X25519 ECDH Key Exchange

**Purpose**: Establish ephemeral shared secret for forward secrecy

### Process

1. **Client** generates ephemeral X25519 key pair
2. **Server** generates ephemeral X25519 key pair
3. **Both** exchange public keys
4. **Both** compute shared secret using ECDH

### Cryptographic Details
- **Algorithm**: X25519 Elliptic Curve Diffie-Hellman
- **Key Size**: 32 bytes (256 bits)
- **Library**: libsodium (`crypto_scalarmult_curve25519`)

### Security Properties
- Forward secrecy (ephemeral keys)
- Quantum-resistant key agreement preparation
- No key reuse

```
Client                                    Server
  │                                          │
  │  1. Generate X25519 keypair              │  1. Generate X25519 keypair
  │     client_private_key (32 bytes)        │     server_private_key (32 bytes)
  │     client_public_key (32 bytes)         │     server_public_key (32 bytes)
  │                                          │
  │  2. Send client_public_key               │
  ├─────────────────────────────────────────>│
  │                                          │
  │  3. Receive server_public_key            │
  │<─────────────────────────────────────────┤
  │                                          │
  │  4. Compute shared secret                │  4. Compute shared secret
  │     ECDH(client_priv, server_pub)        │     ECDH(server_priv, client_pub)
  │     = shared_secret (32 bytes)           │     = shared_secret (32 bytes)
  │                                          │
```

### Implementation
- **Client**: `EcliptixProtocolSystem.cs`
- **Server**: `EcliptixProtocolSystem.cs`

### X25519 Key Material Properties

**Ephemeral Key Pair**:
```
Private Key:
  • Size: 32 bytes (256 bits)
  • Encoding: Raw bytes (clamped scalar)
  • Clamping: Bits 0,1,2 = 0, Bit 255 = 1, Bit 254 = 0
  • Generation: crypto_box_keypair() from libsodium
  • Lifetime: Single session only (destroyed after use)

Public Key:
  • Size: 32 bytes (256 bits)
  • Encoding: Curve25519 point (compressed)
  • Format: Little-endian u-coordinate
  • Transmission: Sent as raw bytes in protobuf

Shared Secret:
  • Size: 32 bytes (256 bits)
  • Computation: crypto_scalarmult(private_key, peer_public_key)
  • Algorithm: X25519(a, B) = aB where a = scalar, B = point
  • Properties: Commutative (both sides derive identical secret)
```

**Key Exchange Bundle** (Protobuf):
```protobuf
message KeyExchangeBundle {
  bytes client_public_key = 1;     // 32 bytes
  bytes server_public_key = 2;     // 32 bytes
  uint64 timestamp = 3;            // Unix timestamp
  bytes signature = 4;             // Optional: Server signs bundle
}
```

---

## Phase 3: Session Key Derivation (HKDF)

**Purpose**: Derive encryption keys from shared secret

### Process

1. **Input**: Shared secret from X25519 ECDH (32 bytes)
2. **HKDF Extract**: Create pseudorandom key (PRK)
3. **HKDF Expand**: Derive multiple keys from PRK

### Key Hierarchy

```
Shared Secret (32 bytes)
    │
    │ HKDF-SHA256 Extract
    ├────────────────────────────────────────────┐
    │                                            │
    v                                            v
Root Key (32 bytes)                    Chain Key (32 bytes)
    │                                            │
    │ HKDF Expand                                │ HKDF Expand
    ├──────────────────┐                         ├──────────────────┐
    v                  v                         v                  v
Sending Key     Receiving Key              Message Key 1      Message Key 2
(32 bytes)      (32 bytes)                 (32 bytes)         (32 bytes)
```

### Cryptographic Details
- **Algorithm**: HKDF-SHA256
- **Info Strings**: Domain separation ("ecliptix-protocol-root-key", etc.)
- **Output**: Multiple 32-byte keys

### Implementation
- **File**: `EcliptixProtocolSystem.cs` - `DeriveKeys()` method

### HKDF Key Material Properties

**HKDF-Extract Phase**:
```
Input:
  • Salt: Optional (can be null or zero-filled)
  • IKM (Input Keying Material): shared_secret (32 bytes)

Output:
  • PRK (Pseudorandom Key): 32 bytes

Algorithm:
  PRK = HMAC-SHA256(salt, IKM)

Properties:
  • Extracts cryptographic strength from IKM
  • Produces uniformly random PRK
  • Salt provides domain separation
```

**HKDF-Expand Phase**:
```
Input:
  • PRK: 32 bytes (from Extract phase)
  • Info: Context string (e.g., "ecliptix-protocol-root-key")
  • L: Desired output length in bytes

Output:
  • OKM (Output Keying Material): L bytes

Algorithm:
  N = ceil(L / HashLen)
  T(0) = empty
  T(i) = HMAC-SHA256(PRK, T(i-1) || info || byte(i))  for i = 1..N
  OKM = first L bytes of T(1) || T(2) || ... || T(N)

Properties:
  • Expands PRK to multiple independent keys
  • Info provides context binding
  • Different info strings yield independent keys
```

**Derived Key Bundle**:
```
Root Key (32 bytes):
  • HKDF(shared_secret, info="ecliptix-protocol-root-key")
  • Used for protocol initialization
  • Parent of chain keys

Chain Key - Sending (32 bytes):
  • HKDF(root_key, info="sending-chain-key")
  • Ratcheted for each message sent
  • Never used directly for encryption

Chain Key - Receiving (32 bytes):
  • HKDF(root_key, info="receiving-chain-key")
  • Ratcheted for each message received
  • Never used directly for encryption

Message Key (32 bytes):
  • HKDF(chain_key, info="message-key-" || counter)
  • One per message
  • Used for AES-256-GCM encryption
  • Destroyed immediately after use
```

**Key Hierarchy Structure**:
```
shared_secret (32 bytes)
    │
    │ HKDF-Extract → PRK (32 bytes)
    │
    ├─────────────────────────────────────────────────────┐
    │                                                     │
    │ HKDF-Expand(info="root-key")                       │ HKDF-Expand(info="ratchet-key")
    │                                                     │
    v                                                     v
root_key (32 bytes)                               ratchet_key (32 bytes)
    │                                                     │
    ├────────────────────┐                                │
    │                    │                                │
    │ (sending)          │ (receiving)                    │ (for DH ratchet)
    v                    v                                v
chain_key_send      chain_key_recv                  Used for new DH
    │                    │                           key agreement
    │                    │
    │ Ratchet            │ Ratchet
    v                    v
msg_key_0           msg_key_0
msg_key_1           msg_key_1
msg_key_2           msg_key_2
  ...                  ...
```

**Ratcheting Process**:
```
For each message sent/received:
  1. Derive message key: msg_key = HKDF(chain_key, counter)
  2. Encrypt/decrypt message with msg_key
  3. Destroy msg_key immediately
  4. Ratchet chain key: chain_key_new = HKDF(chain_key, "ratchet")
  5. Increment counter
  6. Never reuse message keys
```

---

## Phase 4: Anonymous Session Establishment

**Purpose**: Create encrypted communication channel without authentication

### Process

1. **Both sides** derive AES-256-GCM encryption keys
2. **Client** registers device with encrypted request
3. **Server** assigns system device identifier
4. **Session state** saved to disk

### Encryption Details
- **Algorithm**: AES-256-GCM
- **Key Size**: 32 bytes (256 bits)
- **Nonce Size**: 12 bytes (96 bits)
- **Tag Size**: 16 bytes (128 bits authentication)

```
┌─────────────────────────────────────────────────────────────┐
│                   AES-256-GCM Encryption                    │
│                                                             │
│  Plaintext + AAD + Key + Nonce  →  Ciphertext + Tag        │
│                                                             │
│  • Authenticated Encryption with Associated Data           │
│  • Confidentiality + Integrity + Authenticity               │
│  • Nonce must never be reused with same key                │
└─────────────────────────────────────────────────────────────┘
```

### Session State Persistence
- **Location**: `~/.ecliptix/protocol_state/{connectId}.ecliptix`
- **Encryption**: AES-256-GCM with device-specific key
- **Contents**: Chain keys, message keys, ratchet state

### Implementation
- **Client**: `ApplicationInitializer.cs` - `EstablishAndSaveSecrecyChannelAsync()`
- **Storage**: `SecureProtocolStateStorage.cs`

### AES-256-GCM Encryption Properties

**Encryption Key Material**:
```
Encryption Key:
  • Size: 32 bytes (256 bits)
  • Source: Message key derived from chain key
  • Algorithm: AES-256
  • Mode: GCM (Galois/Counter Mode)
  • Lifetime: Single message only

Nonce (IV):
  • Size: 12 bytes (96 bits) - RECOMMENDED for GCM
  • Generation: Crypto-secure random (platform RNG)
  • Uniqueness: MUST be unique per (key, message) pair
  • Transmission: Sent in plaintext with ciphertext

Authentication Tag:
  • Size: 16 bytes (128 bits)
  • Algorithm: GMAC (GCM authentication)
  • Coverage: Ciphertext + AAD
  • Verification: Constant-time comparison

Associated Authenticated Data (AAD):
  • Purpose: Bind context to ciphertext
  • Contents: Message metadata (counter, timestamp, etc.)
  • Not encrypted, but authenticated
  • Prevents reordering, replay, substitution attacks
```

**Encryption Process**:
```
Input:
  • Plaintext: Message bytes (variable length)
  • Key: 32 bytes (from message key derivation)
  • Nonce: 12 bytes (generated securely random)
  • AAD: Context information (message counter, etc.)

Output:
  • Ciphertext: Same length as plaintext
  • Tag: 16 bytes (authentication tag)

Algorithm:
  1. Initialize AES-256-GCM with key
  2. Set nonce (12 bytes)
  3. Set AAD (authenticated but not encrypted)
  4. Encrypt plaintext → ciphertext
  5. Compute authentication tag over (ciphertext || AAD)
  6. Return (ciphertext || tag || nonce)
```

**Encrypted Message Bundle** (Protobuf):
```protobuf
message EncryptedMessage {
  bytes ciphertext = 1;           // Encrypted payload
  bytes nonce = 2;                // 12 bytes
  bytes tag = 3;                  // 16 bytes (authentication)
  bytes aad = 4;                  // Associated data
  uint64 message_counter = 5;     // For chain key ratcheting
  uint64 timestamp = 6;           // Unix timestamp
}
```

**Security Properties**:
```
AES-256-GCM provides:
  ✓ Confidentiality: Ciphertext reveals no information about plaintext
  ✓ Integrity: Any modification of ciphertext is detected
  ✓ Authenticity: Only holder of key can create valid ciphertext
  ✓ No malleability: Bit-flipping attacks impossible
  ✓ Parallel processing: Encryption/decryption can be parallelized

Critical Requirements:
  ⚠ Nonce MUST be unique for each (key, message) pair
  ⚠ Nonce reuse with same key breaks security completely
  ⚠ Key MUST NOT be reused (use message key derivation)
  ⚠ Tag verification MUST use constant-time comparison
```

---

## Phase 5: OPAQUE Authentication

**Purpose**: Password-authenticated key exchange without sending password

### OPAQUE Protocol Overview

OPAQUE is a zero-knowledge password-authenticated key exchange protocol that:
- Never transmits the password
- Protects against offline dictionary attacks
- Provides mutual authentication
- Generates a strong session key (export_key)

### Registration Flow

```
Client                                    Server
  │                                          │
  │  1. User enters password                 │
  │     password: string                     │
  │                                          │
  │  2. OPAQUE Registration Start            │
  │     CreateRegistrationRequest(password)  │
  │     → registration_request               │
  ├─────────────────────────────────────────>│
  │                                          │
  │                                          │  3. Server processes request
  │                                          │     CreateRegistrationResponse()
  │                                          │     → registration_response
  │  4. Receive response                     │
  │<─────────────────────────────────────────┤
  │                                          │
  │  5. Finalize registration                │
  │     FinalizeRequest(password, response)  │
  │     → registration_record                │
  │     → export_key (64 bytes) ★            │
  ├─────────────────────────────────────────>│
  │                                          │
  │                                          │  6. Store registration_record
  │                                          │     (includes password verifier)
  │  7. Registration Complete                │
  │<─────────────────────────────────────────┤
```

**Key Output**: `export_key` (64 bytes) - Used for master key derivation

### Login Flow

```
Client                                    Server
  │                                          │
  │  1. User enters password                 │
  │     password: string                     │
  │                                          │
  │  2. OPAQUE Login Start                   │
  │     CreateCredentialRequest(password)    │
  │     → credential_request                 │
  ├─────────────────────────────────────────>│
  │                                          │
  │                                          │  3. Server retrieves record
  │                                          │     CreateCredentialResponse()
  │                                          │     → credential_response
  │  4. Receive response                     │
  │<─────────────────────────────────────────┤
  │                                          │
  │  5. Finalize login                       │
  │     RecoverCredentials(password, resp)   │
  │     → export_key (64 bytes) ★            │
  │     (same as registration!)              │
  │                                          │
  │  6. Login Complete                       │
  │<════════════════════════════════════════>│
```

### Cryptographic Details
- **Protocol**: OPAQUE (RFC Draft)
- **Ciphersuite**: ristretto255-SHA512
- **Export Key**: 64 bytes (512 bits)
- **Properties**: Zero-knowledge, forward secrecy

### Implementation
- **Client**: `OpaqueAuthenticationService.cs`
- **Server**: `OpaqueAuthenticationService.cs`
- **Library**: `Ecliptix.Opaque.Protocol`

### OPAQUE Key Material Properties

**Export Key** (Primary Output):
```
Size: 64 bytes (512 bits)
Generation: Derived during OPAQUE protocol
Derivation: HKDF from shared OPAQUE secret
Purpose: Input for master key derivation chain
Security: Only available to authenticated client
Properties:
  • Deterministic: Same password → same export_key
  • Zero-knowledge: Server never sees password
  • Unique per user: Bound to membership ID
  • High entropy: Full 512 bits of randomness
```

**OPAQUE Internal Keys**:
```
Client Ephemeral:
  • r (scalar): 32 bytes - Random per protocol run
  • r·G (point): 32 bytes - Sent to server

Server Ephemeral:
  • s (scalar): 32 bytes - Random per protocol run
  • s·G (point): 32 bytes - Sent to client

Password Verifier (Server-stored):
  • Size: Variable (typically 96 bytes)
  • Contents: Encrypted envelope + salt
  • Purpose: Verify password without knowing it
  • Security: Useless for offline attacks

Shared Secret (Internal):
  • Computed via OPAQUE PAKE
  • Never transmitted
  • Used to derive export_key
  • Size: 32 bytes

Session Key (Internal):
  • Derived from shared secret
  • Used for protocol authentication
  • Size: 32 bytes
```

**OPAQUE Protocol Bundle** (Protobuf):
```protobuf
message OpaqueRegistrationRequest {
  bytes blinded_message = 1;      // 32 bytes (rG)
  string username = 2;
}

message OpaqueRegistrationResponse {
  bytes evaluation = 1;            // 32 bytes (H(pwd)^k)
  bytes server_public_key = 2;     // 32 bytes
}

message OpaqueRegistrationRecord {
  bytes envelope = 1;              // Encrypted credentials
  bytes server_public_key = 2;     // 32 bytes
  bytes masking_key = 3;           // 32 bytes
}

message OpaqueLoginRequest {
  bytes credential_request = 1;    // Blinded password
  string username = 2;
}

message OpaqueLoginResponse {
  bytes credential_response = 1;   // Server response
  bytes masked_response = 2;       // Envelope data
}
```

**Export Key Derivation**:
```
Input:
  • shared_secret: 32 bytes (from OPAQUE PAKE)
  • session_id: Unique session identifier
  • transcript: Full protocol transcript

Derivation:
  1. Compute session_key = HKDF(shared_secret, info="session")
  2. Compute export_key = HKDF(session_key, info="export", length=64)
  3. Return export_key (64 bytes)

Properties:
  • Domain separation via info strings
  • Transcript binding prevents tampering
  • Session-specific (includes session_id)
  • Deterministic for same password + user
```

**Fingerprint Example** (from real logs):
```
export_key fingerprint: F4134992858DAD13
  (First 16 hex chars of SHA-256(export_key))

This fingerprint MUST match on both:
  • Client (after login)
  • Server (after verification)

Mismatch indicates:
  ❌ Password incorrect
  ❌ Protocol implementation bug
  ❌ MITM attack
```

---

## Phase 6: Master Key Derivation Chain

**Purpose**: Derive cryptographic master key from OPAQUE export key

This is a multi-stage process to harden the key and bind it to the user's identity.

### Stage 1: Enhanced Key Derivation

**Input**: OPAQUE export_key (64 bytes)

```
export_key (64 bytes)
    │
    │ Step 1: Argon2id Key Stretching
    │ • Memory: 256 MB
    │ • Iterations: 4
    │ • Parallelism: 4
    │ • Output: 64 bytes
    │
    v
stretched_key (64 bytes)
    │
    │ Step 2: HKDF Expansion
    │ • Context: "ecliptix-session-key-ecliptix-signin-session"
    │ • Salt: SHA256(context)
    │ • Output: 64 bytes
    │
    v
expanded_key (64 bytes)
    │
    │ Step 3: Additional Rounds (3 rounds)
    │ • Round 0: HMAC-SHA512(key, "round-0") ⊕ SHA512(key)
    │ • Round 1: HMAC-SHA512(key, "round-1") ⊕ SHA512(key)
    │ • Round 2: HMAC-SHA512(key, "round-2") ⊕ SHA512(key)
    │ • Output: 64 bytes
    │
    v
enhanced_key (64 bytes)
```

**Implementation**: `HardenedKeyDerivation.cs` - `DeriveEnhancedMasterKeyHandleAsync()`

### Stage 2: Master Key Derivation

**Input**: enhanced_key (64 bytes), membershipId (16 bytes GUID)

```
enhanced_key (64 bytes)
    │
    │ Step 1: Create Argon2 Salt
    │ • Salt = SHA256(membershipId || version || domain_context)
    │ • Domain: "ECLIPTIX_MASTER_KEY"
    │
    v
argon2_salt (32 bytes)
    │
    │ Step 2: Argon2id Stretching
    │ • Memory: 256 MB
    │ • Iterations: 4
    │ • Parallelism: 4
    │ • Output: 32 bytes
    │
    v
stretched_key (32 bytes)
    │
    │ Step 3: Blake2b Hashing
    │ • Message: stretched_key
    │ • Salt: "ECLIPTIX_MSTR_V1" (16 bytes)
    │ • Personal: membershipId (16 bytes)
    │ • Output: 32 bytes
    │
    v
master_key (32 bytes) ★★★
```

**Implementation**: `MasterKeyDerivation.cs` - `DeriveMasterKeyHandle()`

### Master Key Material Properties

**Argon2id Parameters**:
```
Algorithm: Argon2id (hybrid of Argon2i and Argon2d)
Purpose: Memory-hard key stretching

Parameters:
  • Memory: 262144 KB (256 MB)
  • Iterations: 4 passes through memory
  • Parallelism: 4 threads
  • Output: 32 bytes
  • Salt: SHA-256(membershipId || version || domain)

Salt Construction:
  salt_input = membershipId (16 bytes GUID)
            || version (4 bytes int, value=1)
            || "ECLIPTIX_MASTER_KEY" (UTF-8)
  argon_salt = SHA-256(salt_input) → 32 bytes

Security Properties:
  ✓ Memory-hard: Requires 256MB RAM per computation
  ✓ Parallelization-hard: Limited to 4 cores benefit
  ✓ ASIC-resistant: Memory cost makes custom hardware expensive
  ✓ Side-channel resistant: Hybrid mode protects against timing attacks
```

**Blake2b Parameters**:
```
Algorithm: Blake2b (BLAKE2 in 64-bit mode)
Purpose: Final key derivation with domain separation

Parameters:
  • Message: stretched_key (32 bytes from Argon2id)
  • Key: null (no keyed hashing)
  • Salt: "ECLIPTIX_MSTR_V1" (16 bytes, UTF-8 encoded)
  • Personal: membershipId (16 bytes, GUID as bytes)
  • Output: 32 bytes (256 bits)

Salt ("ECLIPTIX_MSTR_V1"):
  Hex: 45 43 4C 49 50 54 49 58 5F 4D 53 54 52 5F 56 31
  Length: Must be exactly 16 bytes
  Purpose: Protocol version and domain separation

Personal (membershipId GUID):
  Format: 16-byte GUID in specific byte order
  Conversion: Via Helpers.GuidToByteString()
  Byte Order: Component reversal for consistency
  Example: 12345678-90AB-CDEF-1234-567890ABCDEF
         → [78 56 34 12 AB 90 EF CD 34 12 78 56 90 AB CD EF]

Security Properties:
  ✓ Collision-resistant: 256-bit output space
  ✓ Pre-image resistant: Cannot reverse to input
  ✓ Domain separation: Salt prevents cross-protocol attacks
  ✓ User binding: Personal parameter ties key to user
```

**Master Key Bundle**:
```
master_key (32 bytes):
  • Algorithm chain: Argon2id → Blake2b
  • Domain: ECLIPTIX_MASTER_KEY
  • User-bound: Via membershipId in Blake2b personal
  • Version: 1 (current)
  • Security level: 256 bits
  • Purpose: Parent key for all identity keys

Derivation Formula:
  argon_salt = SHA-256(membershipId || 0x01000000 || "ECLIPTIX_MASTER_KEY")
  stretched = Argon2id(
                input=export_key (64 bytes),
                salt=argon_salt,
                memory=256MB,
                iterations=4,
                parallelism=4,
                output=32 bytes
              )
  master_key = Blake2b(
                message=stretched,
                key=null,
                salt="ECLIPTIX_MSTR_V1" (16 bytes),
                personal=membershipId (16 bytes),
                output=32 bytes
              )

Fingerprint Calculation:
  fingerprint = SHA-256(master_key)[0..16]
  (First 16 hex characters for logging/verification)
```

**Constants Used**:
```c#
// From implementation
private const int KEY_SIZE = 32;
private const int ARGON2_ITERATIONS = 4;
private const int ARGON2_MEMORY_SIZE = 262144; // 256MB in KB
private const int ARGON2_PARALLELISM = 4;
private const int CURRENT_VERSION = 1;

private const string MASTER_SALT = "ECLIPTIX_MSTR_V1";
private const string DOMAIN_CONTEXT = "ECLIPTIX_MASTER_KEY";
```

**Example Derivation** (from real test logs):
```
Input:
  export_key fingerprint:    F4134992858DAD13
  membershipId (GUID):       12345678-90AB-CDEF-1234-567890ABCDEF

Stage 1 - Enhanced Key Derivation:
  Argon2id stretched:        33E3BC651ABC52E4
  HKDF expanded:             779D9DAEE54C26D5
  Additional rounds final:   D5AD0949491A4BBC
  → enhanced_key (64 bytes)

Stage 2 - Master Key Derivation:
  Argon2id salt hash:        [computed from membershipId]
  Argon2id stretched:        [from enhanced_key]
  Blake2b salt:              "ECLIPTIX_MSTR_V1" (16 bytes)
  Blake2b personal:          membershipId bytes (16 bytes)
  → master_key fingerprint:  C46F78CC6AED11B9 ✓

Both client and server MUST produce identical fingerprint!
```

### Stage 3: Identity Key Derivation

**Input**: master_key (32 bytes), membershipId (string)

```
master_key (32 bytes)
    │
    ├────────────────────────────────────┬─────────────────────────────────┐
    │                                    │                                 │
    │ Context: "ED25519"                 │ Context: "X25519"               │ Context: "SPK_X25519"
    │ Blake2b(key=master_key)            │ Blake2b(key=master_key)         │ Blake2b(key=master_key)
    │                                    │                                 │
    v                                    v                                 v
ed25519_seed (32 bytes)            x25519_seed (32 bytes)           spk_seed (32 bytes)
    │                                    │                                 │
    │ Generate keypair                   │ Generate keypair                │ Generate keypair
    │                                    │                                 │
    v                                    v                                 v
Ed25519 Keys                       X25519 Keys                     Signed Pre-Key
• Private: 32 bytes                • Private: 32 bytes             • Private: 32 bytes
• Public: 32 bytes                 • Public: 32 bytes              • Public: 32 bytes
(Identity Signing)                 (Identity Encryption)           (Pre-key for DH)
```

### Identity Key Material Properties

**Ed25519 Signing Key**:
```
Seed Derivation:
  context = version (4 bytes) || "ED25519" || membershipId (UTF-8)
  seed = Blake2b(key=master_key, message=context, output=32 bytes)

Keypair Generation:
  (private_key, public_key) = Ed25519_KeyGen(seed)
  private_key: 32 bytes (scalar)
  public_key: 32 bytes (point on Edwards curve)

Properties:
  • Deterministic: Same master_key → same keypair
  • User-bound: context includes membershipId
  • Version-specific: context includes protocol version
  • Usage: Sign identity proofs, protocol messages

Public Key Encoding:
  • Format: Compressed Edwards point (y-coordinate + sign bit)
  • Transmission: 32 bytes in protobuf
  • Verification: Standard Ed25519 signature verification
```

**X25519 Encryption Key**:
```
Seed Derivation:
  context = version (4 bytes) || "X25519" || membershipId (UTF-8)
  seed = Blake2b(key=master_key, message=context, output=32 bytes)

Keypair Generation:
  (private_key, public_key) = X25519_KeyGen(seed)
  private_key: 32 bytes (clamped scalar)
  public_key: 32 bytes (Curve25519 point)

Properties:
  • Deterministic: Same master_key → same keypair
  • Long-term: Used for persistent identity encryption
  • DH-capable: Can perform key agreement with any X25519 key
  • Usage: Receive encrypted messages, establish sessions

Public Key Encoding:
  • Format: u-coordinate of Curve25519 point
  • Transmission: 32 bytes in protobuf
  • Key Agreement: X25519(private, peer_public) → shared_secret
```

**Signed Pre-Key (SPK)**:
```
Seed Derivation:
  context = version (4 bytes) || "SPK_X25519" || membershipId (UTF-8)
  seed = Blake2b(key=master_key, message=context, output=32 bytes)

Keypair Generation:
  (spk_private, spk_public) = X25519_KeyGen(seed)
  spk_private: 32 bytes (clamped scalar)
  spk_public: 32 bytes (Curve25519 point)

Signature:
  signature = Ed25519_Sign(identity_private_key, spk_public)
  signature: 64 bytes

Properties:
  • Pre-generated: Derived at registration time
  • Signed: Identity key signs SPK public key
  • Rotatable: Can be refreshed periodically
  • Usage: Asynchronous message encryption (like Signal)

Bundle:
  {
    spk_public: 32 bytes,
    spk_signature: 64 bytes (signed by identity Ed25519 key),
    spk_id: unique identifier,
    timestamp: creation time
  }
```

**Identity Keys Bundle** (Protobuf):
```protobuf
message IdentityKeys {
  bytes ed25519_public = 1;       // 32 bytes (signing key)
  bytes x25519_public = 2;        // 32 bytes (encryption key)
  bytes spk_public = 3;           // 32 bytes (signed pre-key)
  bytes spk_signature = 4;        // 64 bytes (Ed25519 signature)
  uint64 spk_id = 5;              // Pre-key identifier
  uint64 created_at = 6;          // Unix timestamp
}
```

**Blake2b Context Strings**:
```c#
private const string ED25519_CONTEXT = "ED25519";
private const string X25519_CONTEXT = "X25519";
private const string SPK_X25519_CONTEXT = "SPK_X25519";

// Context construction:
context_bytes = BitConverter.GetBytes(1)              // version: 4 bytes
             + Encoding.UTF8.GetBytes(context_string)  // "ED25519" etc.
             + Encoding.UTF8.GetBytes(membershipId)    // user ID string
```

### Root Key Derivation

**Input**: master_key (32 bytes)

```
master_key (32 bytes)
    │
    │ HKDF-SHA256
    │ • IKM: master_key
    │ • Salt: null
    │ • Info: "ecliptix-protocol-root-key"
    │ • Output: 32 bytes
    │
    v
root_key (32 bytes)
    │
    └──> Used for authenticated protocol handshake
```

**Root Key Properties**:
```
Purpose: Initialize authenticated protocol with identity-bound key

Derivation:
  root_key = HKDF-SHA256(
              ikm=master_key (32 bytes),
              salt=null,
              info="ecliptix-protocol-root-key",
              length=32 bytes
            )

Usage:
  1. Replace ephemeral shared_secret in protocol initialization
  2. Derive sending/receiving chain keys
  3. Initialize ratchet with identity-bound key
  4. Provides authentication + forward secrecy

Properties:
  • Deterministic: Same master_key → same root_key
  • User-bound: Derived from identity-bound master_key
  • Session-independent: Can be recomputed for new sessions
  • Forward-secure: Used with ratcheting for PCS

Example (from real logs):
  master_key fingerprint: C46F78CC6AED11B9
  root_key fingerprint:   B17B323E43781063
```

**Implementation**: `MasterKeyService.cs` - `DeriveIdentityKeysAsync()`

### Verification Fingerprints (Example from Real Logs)

These fingerprints allow verification that both client and server derived identical keys:

```
OPAQUE export_key:       F4134992858DAD13
Enhanced Argon2id:       33E3BC651ABC52E4
Enhanced HKDF:           779D9DAEE54C26D5
Enhanced Final:          D5AD0949491A4BBC
Master Key:              C46F78CC6AED11B9
Root Key:                B17B323E43781063
```

All fingerprints are first 16 hex characters of SHA256 hash of the key.

---

## Phase 7: Shamir Secret Sharing

**Purpose**: Split master key into multiple shares for redundancy and security

### Configuration
- **Total Shares**: 5
- **Threshold**: 3 (minimum shares needed to reconstruct)
- **Algorithm**: Shamir's Secret Sharing over GF(256)

### Server Storage (Centralized)

All 5 shares are stored in PostgreSQL database:

```
┌─────────────────────────────────────────────┐
│         PostgreSQL: master_key_shares       │
├─────────────────────────────────────────────┤
│  membership_id  │  share_index  │  share   │
├─────────────────┼───────────────┼──────────┤
│  user-guid-123  │       0       │  [data]  │
│  user-guid-123  │       1       │  [data]  │
│  user-guid-123  │       2       │  [data]  │
│  user-guid-123  │       3       │  [data]  │
│  user-guid-123  │       4       │  [data]  │
└─────────────────┴───────────────┴──────────┘
```

**Properties**:
- Atomic storage (all or nothing)
- Reliable retrieval (always available)
- No platform dependencies

### Client Storage (Distributed)

5 shares distributed across different storage locations:

```
Share 0: Hardware Security
├─ Location: macOS Keychain
├─ Encryption: Secure Enclave (if available)
└─ Reliability: High (if hardware available)

Share 1: Platform Keychain
├─ Location: macOS Keychain
├─ Encryption: Keychain encryption
└─ Reliability: High

Share 2: Platform Keychain
├─ Location: macOS Keychain
├─ Encryption: Keychain encryption
└─ Reliability: High

Share 3: Local Encrypted File
├─ Location: ~/.ecliptix/secure_storage/
├─ Encryption: Double-encrypted (AES + Platform)
└─ Reliability: High

Share 4: Backup Keychain
├─ Location: macOS Keychain (backup prefix)
├─ Encryption: Keychain encryption
└─ Reliability: High
```

### Share Reconstruction

**Server**: Simple database query returns all 5 shares

**Client**: Two-stage fallback mechanism

```
Stage 1: Try Distributed Shares
    │
    ├─> Retrieve from 5 storage locations
    │
    ├─> Check if >= 3 shares found
    │
    ├─> If YES: Reconstruct master key using Shamir
    │   └─> SUCCESS
    │
    └─> If NO: Proceed to Stage 2

Stage 2: Direct Storage Fallback
    │
    ├─> Load full master key from encrypted storage
    │
    └─> SUCCESS (bypass Shamir reconstruction)
```

### Security Properties
- **No single point of failure**: Any 3 of 5 shares can reconstruct
- **Threshold security**: < 3 shares reveal nothing about master key
- **Defense in depth**: Fallback to direct storage if shares fail
- **Platform independence**: Server uses reliable database storage

### Implementation
- **Splitting**: `ShamirSecretSharing.cs` - `SplitKeyAsync()`
- **Reconstruction**: `ShamirSecretSharing.cs` - `ReconstructKeyHandleAsync()`
- **Storage**: `DistributedShareStorage.cs` (client), `MasterKeySharePersistorActor.cs` (server)

---

## Phase 8: Authenticated Session Re-establishment

**Purpose**: Restore authenticated session after app restart

### Process Flow

```
App Starts
    │
    │ Check: Is this a new instance?
    │
    ├─> YES: Establish anonymous session
    │   └─> Go to Phase 4
    │
    └─> NO: Try to restore previous session
        │
        ├─ Step 1: Load protocol state from disk
        │  └─> If failed: Establish new session
        │
        ├─ Step 2: Validate session state
        │  └─> If expired/invalid: Establish new session
        │
        ├─ Step 3: Check for stored identity
        │  │
        │  ├─> NO identity: Anonymous session
        │  │
        │  └─> YES identity: Try master key reconstruction
        │      │
        │      ├─ Attempt 1: Distributed Shares
        │      │  └─> If >= 3 shares: Reconstruct with Shamir
        │      │
        │      ├─ Attempt 2: Direct Storage
        │      │  └─> Load encrypted master key
        │      │
        │      └─ If both fail: Fall back to anonymous
        │
        └─ Step 4: Derive root key from master key
           │
           └─ Step 5: Create authenticated protocol
              │
              └─ Step 6: Perform authenticated handshake
                 │
                 └─> Authenticated Session Established ✓
```

### Session Restoration Details

**Protocol State Storage**:
- **File**: `{connectId}.ecliptix`
- **Location**: `~/.ecliptix/protocol_state/`
- **Contents**:
  ```
  • Chain keys (sending/receiving)
  • Message keys
  • Ratchet state
  • Session metadata
  ```
- **Encryption**: AES-256-GCM with device-specific key

**Master Key Storage**:
- **Distributed**: 5 shares across platform storage
- **Direct**: Encrypted file in `~/.ecliptix/identity_storage/`

### Implementation
- **Client**: `ApplicationInitializer.cs` - `EnsureSecrecyChannelAsync()`

---

## Phase 9: Complete Authentication Flow

### Registration → Login → Authenticated Session

```
═══════════════════════════════════════════════════════════════
                    REGISTRATION FLOW
═══════════════════════════════════════════════════════════════

Client                                    Server
  │
  │  1. Establish Anonymous Session (Phases 1-4)
  ├<════════════════════════════════════════>│
  │
  │  2. OPAQUE Registration
  │     • User enters password
  │     • Generate export_key (64 bytes)
  ├─────────────────────────────────────────>│
  │                                          │  3. Store registration record
  │
  │  4. Derive Enhanced Key from export_key
  │     • Argon2id + HKDF + rounds
  │     = enhanced_key (64 bytes)
  │
  │  5. Derive Master Key
  │     • enhanced_key + membershipId
  │     • Argon2id + Blake2b
  │     = master_key (32 bytes)
  ├─────────────────────────────────────────>│  6. Server does same derivation
  │                                          │     = master_key (32 bytes)
  │  ✓ Verify fingerprints match             │  ✓ Fingerprints match
  │
  │  7. Split master key (Shamir)
  │     • 5 shares, threshold 3
  │     • Store in distributed locations
  │                                          │  8. Split master key (Shamir)
  │                                          │     • 5 shares in PostgreSQL
  │
  │  9. Derive identity keys
  │     • Ed25519 (signing)
  │     • X25519 (encryption)
  │     • Signed pre-key
  ├─────────────────────────────────────────>│  10. Store identity keys
  │
  │  11. Store master key in encrypted storage
  │      (fallback for share reconstruction)
  │
  │  Registration Complete ✓

═══════════════════════════════════════════════════════════════
                        LOGIN FLOW
═══════════════════════════════════════════════════════════════

Client                                    Server
  │
  │  1. Establish Anonymous Session (Phases 1-4)
  ├<════════════════════════════════════════>│
  │
  │  2. OPAQUE Login
  │     • User enters password
  │     • Retrieve export_key (64 bytes)
  ├─────────────────────────────────────────>│
  │                                          │  3. Validate credentials
  │                                          │  4. Retrieve export_key
  │
  │  5. Derive Enhanced Key from export_key
  │     • Argon2id + HKDF + rounds
  │     = enhanced_key (64 bytes)
  │
  │  6. Derive Master Key
  │     • enhanced_key + membershipId
  │     • Argon2id + Blake2b
  │     = master_key (32 bytes)
  ├─────────────────────────────────────────>│  7. Server does same derivation
  │                                          │     = master_key (32 bytes)
  │  ✓ Verify fingerprints match             │  ✓ Fingerprints match
  │                                          │     (must match registration)
  │
  │  8. Derive root_key from master_key
  │     • HKDF-SHA256
  │     = root_key (32 bytes)
  │                                          │  9. Derive root_key
  │                                          │     = root_key (32 bytes)
  │
  │  10. Create authenticated protocol
  │      • Load identity keys
  │      • Initialize with root_key
  │
  │  11. Authenticated Handshake
  ├<════════════════════════════════════════>│  12. Validate identity keys
  │                                          │      Sign with Ed25519
  │
  │  Authenticated Session Established ✓

═══════════════════════════════════════════════════════════════
                    APP RESTART FLOW
═══════════════════════════════════════════════════════════════

Client
  │
  │  1. Load protocol state from disk
  │     • Chain keys, ratchet state
  │
  │  2. Check session validity
  │     ├─> Valid: Restore session
  │     └─> Invalid/Expired: New handshake needed
  │
  │  3. Reconstruct master key
  │     ├─ Option A: Distributed shares (Shamir)
  │     │   • Retrieve >= 3 shares
  │     │   • Reconstruct master_key
  │     │
  │     └─ Option B: Direct storage (Fallback)
  │         • Load encrypted master_key
  │
  │  4. Derive root_key from master_key
  │
  │  5. Create authenticated protocol
  │
  │  6. Perform authenticated handshake
  │
  │  Session Restored ✓
```

---

## Cryptographic Primitives Reference

### Symmetric Encryption
| Algorithm | Key Size | Nonce Size | Tag Size | Usage |
|-----------|----------|------------|----------|-------|
| AES-256-GCM | 32 bytes | 12 bytes | 16 bytes | Message encryption |

### Key Agreement
| Algorithm | Private Key | Public Key | Shared Secret | Usage |
|-----------|-------------|------------|---------------|-------|
| X25519 ECDH | 32 bytes | 32 bytes | 32 bytes | Session key exchange |

### Key Derivation
| Algorithm | Input | Output | Parameters | Usage |
|-----------|-------|--------|------------|-------|
| HKDF-SHA256 | Variable | Variable | Salt, Info | Key hierarchy |
| Argon2id | Variable | Variable | Memory: 256MB, Iter: 4 | Key stretching |
| Blake2b | Variable | 32 bytes | Salt: 16B, Personal: 16B | Master key derivation |

### Digital Signatures
| Algorithm | Private Key | Public Key | Signature | Usage |
|-----------|-------------|------------|-----------|-------|
| Ed25519 | 32 bytes | 32 bytes | 64 bytes | Identity signing |

### Hashing
| Algorithm | Input | Output | Usage |
|-----------|-------|--------|-------|
| SHA-256 | Variable | 32 bytes | Fingerprints, salts |
| SHA-512 | Variable | 64 bytes | HMAC operations |
| HMAC-SHA512 | Variable | 64 bytes | Additional rounds |

### Secret Sharing
| Algorithm | Input | Shares | Threshold | Usage |
|-----------|-------|--------|-----------|-------|
| Shamir (GF256) | 32 bytes | 5 | 3 | Master key splitting |

### Authentication
| Protocol | Output | Usage |
|----------|--------|-------|
| OPAQUE (ristretto255) | 64 bytes | Password-less auth |

### Libraries Used
- **libsodium**: Core cryptographic operations
- **Konscious.Security.Cryptography**: Argon2id implementation
- **.NET Cryptography**: AES-GCM, HKDF, SHA-256/512
- **Ecliptix.Opaque.Protocol**: OPAQUE implementation

---

## Security Properties

### 1. Forward Secrecy
- **X25519 ephemeral keys**: New key pair for each session
- **Message keys derived from chain keys**: Different key for each message
- **Compromise of long-term keys**: Does not reveal past session keys

### 2. Post-Compromise Security
- **Ratcheting mechanism**: Future messages secure after compromise recovery
- **Key refresh**: Regular key rotation during session

### 3. Zero-Knowledge Authentication
- **OPAQUE protocol**: Server never sees user password
- **Export key derivation**: Password-derived key without password transmission
- **Offline attack resistance**: Server-side verifier provides no information

### 4. Defense in Depth
- **Multiple key derivation stages**: Enhanced → Master → Identity/Root
- **Shamir secret sharing**: Redundant key storage with threshold
- **Fallback mechanisms**: Direct storage if distributed shares fail
- **Hardware security**: Secure Enclave/TPM when available

### 5. Authenticated Encryption
- **AES-256-GCM**: Confidentiality + Integrity + Authenticity
- **No malleability**: Tampering detected and rejected
- **Associated data**: Context binding prevents reuse attacks

### 6. Identity Protection
- **Long-term identity keys**: Derived from master key, not transmitted
- **Signed pre-keys**: Enable asynchronous communication
- **Perfect forward secrecy**: Even with identity key compromise

---

## Key Storage Security

### Server Storage
```
PostgreSQL Database
├─ master_key_shares (5 shares per user)
│  ├─ Encrypted at rest (database encryption)
│  └─ Access control (database permissions)
│
├─ identity_keys (Ed25519, X25519 public keys)
│  └─ Public keys (no encryption needed)
│
└─ opaque_records (password verifiers)
   └─ Zero-knowledge verifiers (safe to store)
```

### Client Storage
```
~/.ecliptix/
├─ protocol_state/
│  ├─ {connectId}.ecliptix
│  └─ AES-256-GCM encrypted with device key
│
├─ identity_storage/
│  ├─ master_key (encrypted)
│  └─ identity_keys (encrypted)
│
└─ secure_storage/
   └─ local_share_{membershipId} (double-encrypted)

macOS Keychain:
├─ hw_share_{membershipId}_0 (Secure Enclave)
├─ kc_share_{membershipId}_1
├─ kc_share_{membershipId}_2
└─ backup_kc_share_{membershipId}_4
```

---

## Fingerprint Verification

During development and debugging, fingerprints are logged at each key derivation stage. These fingerprints are the first 16 hex characters of SHA256(key).

### Example Verification (Registration & Login Must Match)

```
Registration Flow:
[CLIENT-OPAQUE]         export_key:    F4134992858DAD13
[SERVER-OPAQUE]         export_key:    F4134992858DAD13 ✓

[CLIENT-ENHANCED-ARGON2ID] stretched:  33E3BC651ABC52E4
[SERVER-ENHANCED-ARGON2ID] stretched:  33E3BC651ABC52E4 ✓

[CLIENT-ENHANCED-HKDF]     expanded:   779D9DAEE54C26D5
[SERVER-ENHANCED-HKDF]     expanded:   779D9DAEE54C26D5 ✓

[CLIENT-ENHANCED-FINAL]    final:      D5AD0949491A4BBC
[SERVER-ENHANCED-FINAL]    final:      D5AD0949491A4BBC ✓

[CLIENT-BLAKE2B-OUTPUT]    master_key: C46F78CC6AED11B9
[SERVER-BLAKE2B-OUTPUT]    master_key: C46F78CC6AED11B9 ✓

[CLIENT-ROOTKEY-DERIVE]    root_key:   B17B323E43781063
[SERVER-ROOTKEY-DERIVE]    root_key:   B17B323E43781063 ✓

Login Flow (Must Match Registration):
[CLIENT-BLAKE2B-OUTPUT]    master_key: C46F78CC6AED11B9 ✓
[SERVER-BLAKE2B-OUTPUT]    master_key: C46F78CC6AED11B9 ✓
```

### Fingerprint Mismatch = Critical Bug
If any fingerprint doesn't match:
- Client and server derived different keys
- Authentication will fail
- Check: GUID byte order, salt/personal parameters, algorithm versions

---

## Troubleshooting

### Issue: Distributed Shares Return Insufficient Count

**Symptoms**:
```
[CLIENT-MASTERKEY-DISTRIBUTED] Retrieved 2 shares. MembershipId: {id}
[CLIENT-MASTERKEY-DISTRIBUTED] RetrieveKeySharesAsync failed. Error: Insufficient shares: found 2, need 3
```

**Cause**:
- Hardware security (Share 0) unavailable
- Keychain access denied (Shares 1, 2)
- App not properly signed/entitled for keychain

**Solution**:
- System falls back to direct storage automatically
- Authentication still succeeds
- Consider: Add logging to identify which shares fail
- Consider: Relax hardware security requirement for Share 0

---

### Issue: Fingerprint Mismatch Between Client/Server

**Symptoms**:
```
[CLIENT-BLAKE2B-OUTPUT] master_key: AAAA...
[SERVER-BLAKE2B-OUTPUT] master_key: BBBB...  ← Different!
```

**Cause**:
- GUID byte order mismatch
- Different salt/personal parameters
- Different algorithm implementations
- Hardware entropy enabled on one side only

**Solution**:
- Verify `Helpers.GuidToByteString()` used consistently
- Check Blake2b salt (16 bytes) and personal (16 bytes) are identical
- Verify Argon2id parameters match
- Ensure `UseHardwareEntropy = false` on both sides

---

### Issue: Session Restoration Fails After App Restart

**Symptoms**:
```
[CLIENT-RESTORE] Session validation failed. ConnectId: {id}
```

**Cause**:
- Session timeout (expected if app closed for long time)
- Protocol state file corrupted
- Chain keys out of sync

**Solution**:
- System automatically falls back to fresh authenticated handshake
- No user action needed
- Session state will be regenerated

---

## Implementation Files Reference

### Client (Desktop)
```
Ecliptix.Core/
├─ Infrastructure/
│  ├─ Network/Core/Providers/
│  │  └─ NetworkProvider.cs (SSL pinning, gRPC setup)
│  │
│  └─ Security/
│     ├─ KeySplitting/
│     │  ├─ HardenedKeyDerivation.cs (Enhanced key derivation)
│     │  ├─ DistributedShareStorage.cs (Share storage/retrieval)
│     │  └─ ShamirSecretSharing.cs (Secret sharing algorithm)
│     │
│     └─ Storage/
│        └─ SecureProtocolStateStorage.cs (Session persistence)
│
├─ Services/
│  ├─ Authentication/
│  │  ├─ OpaqueAuthenticationService.cs (OPAQUE flows)
│  │  └─ IdentityService.cs (Identity key management)
│  │
│  └─ Core/
│     └─ ApplicationInitializer.cs (Session establishment)
│
└─ Protocol.System/
   ├─ Core/
   │  ├─ EcliptixProtocolSystem.cs (Protocol implementation)
   │  └─ MasterKeyDerivation.cs (Master key derivation)
   │
   └─ Sodium/
      └─ SodiumSecureMemoryHandle.cs (Secure memory)
```

### Server (Backend)
```
Ecliptix.Core/
├─ Services/
│  ├─ KeyDerivation/
│  │  ├─ HardenedKeyDerivation.cs (Enhanced key derivation)
│  │  └─ MasterKeyDerivation.cs (Master key derivation)
│  │
│  └─ Security/
│     └─ MasterKeyService.cs (Master key management)
│
└─ Domain/
   └─ Actors/
      ├─ EcliptixProtocolConnectActor.cs (Protocol handling)
      └─ MasterKeySharePersistorActor.cs (Share storage)
```

---

## Protocol Versions

### Current Version: 1.0

**Features**:
- SSL certificate pinning
- X25519 ECDH key agreement
- AES-256-GCM encryption
- OPAQUE authentication
- Shamir secret sharing (5 shares, threshold 3)
- Ed25519 identity signatures
- Forward secrecy
- Post-compromise security

**Compatibility**:
- Client version 1.0 ↔ Server version 1.0

---

## Future Enhancements

### Planned Features
1. **Post-Quantum Cryptography**:
   - Add Kyber key encapsulation
   - Hybrid X25519 + Kyber

2. **Multi-Device Support**:
   - Device-to-device key sharing
   - Cross-device session synchronization

3. **Backup & Recovery**:
   - Encrypted cloud backup of shares
   - Recovery codes for master key

4. **Hardware Security**:
   - YubiKey integration
   - Hardware token support

---

## Conclusion

Ecliptix implements a comprehensive secure communication protocol that combines:
- **Strong authentication** (OPAQUE)
- **End-to-end encryption** (AES-256-GCM)
- **Forward secrecy** (X25519 ephemeral keys)
- **Defense in depth** (Multiple key derivation stages)
- **Redundancy** (Shamir secret sharing)
- **Session persistence** (Encrypted state storage)

The protocol provides military-grade security while maintaining usability through automatic session restoration and fallback mechanisms.

---

**Document Version**: 1.0
**Last Updated**: 2025-01-06
**Status**: Production-Ready ✓

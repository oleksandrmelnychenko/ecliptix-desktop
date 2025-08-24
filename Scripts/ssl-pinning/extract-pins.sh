#!/bin/bash

# Ecliptix SSL Pin Extraction Tool
# Extracts certificate pins in multiple formats for SSL pinning implementation
# Author: Ecliptix Security Team
# Version: 1.0

set -euo pipefail

# Configuration
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CERTS_DIR="${SCRIPT_DIR}/certificates"
OUTPUT_DIR="${SCRIPT_DIR}/generated"
PINS_DIR="${SCRIPT_DIR}/pins"

# Color codes for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Logging functions
log_info() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

log_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

log_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

log_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Function to check if OpenSSL is available
check_dependencies() {
    if ! command -v openssl &> /dev/null; then
        log_error "OpenSSL is not installed or not in PATH"
        exit 1
    fi
    
    if ! command -v base64 &> /dev/null; then
        log_error "base64 is not installed or not in PATH"
        exit 1
    fi
    
    log_info "Dependencies verified"
}

# Function to extract SPKI pin from certificate
extract_spki_pin() {
    local cert_file="$1"
    local pin_name="$2"
    
    if [[ ! -f "$cert_file" ]]; then
        log_warning "Certificate file not found: $cert_file"
        return 1
    fi
    
    log_info "Extracting SPKI pin from: $cert_file"
    
    # Extract public key and compute SHA256 hash
    local spki_pin
    spki_pin=$(openssl x509 -in "$cert_file" -pubkey -noout | \
               openssl pkey -pubin -outform der | \
               openssl dgst -sha256 -binary | \
               base64)
    
    echo "# $pin_name"
    echo "SPKI Pin: sha256/$spki_pin"
    echo "Pin Hash: $spki_pin"
    echo
    
    # Store in variables for C# generation
    eval "${pin_name}_SPKI_PIN=\"$spki_pin\""
    eval "${pin_name}_CERT_FILE=\"$cert_file\""
    
    return 0
}

# Function to extract certificate fingerprint
extract_cert_fingerprint() {
    local cert_file="$1"
    local pin_name="$2"
    
    if [[ ! -f "$cert_file" ]]; then
        return 1
    fi
    
    # SHA256 fingerprint of the entire certificate
    local cert_fingerprint
    cert_fingerprint=$(openssl x509 -in "$cert_file" -outform der | \
                       openssl dgst -sha256 -binary | \
                       base64)
    
    echo "# $pin_name Certificate Fingerprint"
    echo "Cert SHA256: $cert_fingerprint"
    echo
    
    eval "${pin_name}_CERT_FINGERPRINT=\"$cert_fingerprint\""
}

# Function to extract public key in multiple formats
extract_public_key_info() {
    local cert_file="$1"
    local pin_name="$2"
    
    if [[ ! -f "$cert_file" ]]; then
        return 1
    fi
    
    # Extract public key in PEM format
    local public_key_pem
    public_key_pem=$(openssl x509 -in "$cert_file" -pubkey -noout)
    
    # Extract public key in DER format (base64 encoded)
    local public_key_der
    public_key_der=$(openssl x509 -in "$cert_file" -pubkey -noout | \
                     openssl pkey -pubin -outform der | \
                     base64)
    
    echo "# $pin_name Public Key Information"
    echo "Public Key DER (base64): $public_key_der"
    echo
    
    eval "${pin_name}_PUBLIC_KEY_DER=\"$public_key_der\""
}

# Generate C# constants file
generate_csharp_constants() {
    local output_file="${PINS_DIR}/PinnedCertificates.cs"
    
    log_info "Generating C# constants file: $output_file"
    
    cat > "$output_file" << 'EOF'
using System;
using System.Collections.Generic;

namespace Ecliptix.Security.Pinning.Configuration;

/// <summary>
/// SSL certificate pins for Ecliptix infrastructure
/// Generated automatically - DO NOT EDIT MANUALLY
/// </summary>
public static class PinnedCertificates
{
    /// <summary>
    /// Primary certificate pins (SPKI SHA256 hashes)
    /// </summary>
    public static class Primary
    {
EOF

    # Add primary pins
    if [[ -n "${SERVER_RSA_SPKI_PIN:-}" ]]; then
        echo "        public const string ServerRsaSpkiPin = \"${SERVER_RSA_SPKI_PIN}\";" >> "$output_file"
    fi
    
    if [[ -n "${SERVER_EC_SPKI_PIN:-}" ]]; then
        echo "        public const string ServerEcSpkiPin = \"${SERVER_EC_SPKI_PIN}\";" >> "$output_file"
    fi
    
    if [[ -n "${INTERMEDIATE_RSA_SPKI_PIN:-}" ]]; then
        echo "        public const string IntermediateRsaSpkiPin = \"${INTERMEDIATE_RSA_SPKI_PIN}\";" >> "$output_file"
    fi
    
    if [[ -n "${INTERMEDIATE_EC_SPKI_PIN:-}" ]]; then
        echo "        public const string IntermediateEcSpkiPin = \"${INTERMEDIATE_EC_SPKI_PIN}\";" >> "$output_file"
    fi

    cat >> "$output_file" << 'EOF'
    }

    /// <summary>
    /// Backup certificate pins for rotation support
    /// </summary>
    public static class Backup
    {
EOF

    # Add backup pins
    if [[ -n "${BACKUP_SERVER_RSA_SPKI_PIN:-}" ]]; then
        echo "        public const string BackupServerRsaSpkiPin = \"${BACKUP_SERVER_RSA_SPKI_PIN}\";" >> "$output_file"
    fi
    
    if [[ -n "${BACKUP_SERVER_EC_SPKI_PIN:-}" ]]; then
        echo "        public const string BackupServerEcSpkiPin = \"${BACKUP_SERVER_EC_SPKI_PIN}\";" >> "$output_file"
    fi

    cat >> "$output_file" << 'EOF'
    }

    /// <summary>
    /// Root CA pins for ultimate fallback verification
    /// </summary>
    public static class RootCA
    {
EOF

    # Add root CA pins
    if [[ -n "${ROOT_RSA_SPKI_PIN:-}" ]]; then
        echo "        public const string RootRsaSpkiPin = \"${ROOT_RSA_SPKI_PIN}\";" >> "$output_file"
    fi
    
    if [[ -n "${ROOT_EC_SPKI_PIN:-}" ]]; then
        echo "        public const string RootEcSpkiPin = \"${ROOT_EC_SPKI_PIN}\";" >> "$output_file"
    fi

    cat >> "$output_file" << 'EOF'
    }

    /// <summary>
    /// Get all primary pins as a collection
    /// </summary>
    public static IReadOnlyCollection<string> GetPrimaryPins()
    {
        List<string> pins = new();
        
EOF

    # Add primary pin collection logic
    if [[ -n "${SERVER_RSA_SPKI_PIN:-}" ]]; then
        echo "        pins.Add(Primary.ServerRsaSpkiPin);" >> "$output_file"
    fi
    
    if [[ -n "${SERVER_EC_SPKI_PIN:-}" ]]; then
        echo "        pins.Add(Primary.ServerEcSpkiPin);" >> "$output_file"
    fi
    
    if [[ -n "${INTERMEDIATE_RSA_SPKI_PIN:-}" ]]; then
        echo "        pins.Add(Primary.IntermediateRsaSpkiPin);" >> "$output_file"
    fi
    
    if [[ -n "${INTERMEDIATE_EC_SPKI_PIN:-}" ]]; then
        echo "        pins.Add(Primary.IntermediateEcSpkiPin);" >> "$output_file"
    fi

    cat >> "$output_file" << 'EOF'
        
        return pins.AsReadOnly();
    }

    /// <summary>
    /// Get all backup pins as a collection
    /// </summary>
    public static IReadOnlyCollection<string> GetBackupPins()
    {
        List<string> pins = new();
        
EOF

    # Add backup pin collection logic
    if [[ -n "${BACKUP_SERVER_RSA_SPKI_PIN:-}" ]]; then
        echo "        pins.Add(Backup.BackupServerRsaSpkiPin);" >> "$output_file"
    fi
    
    if [[ -n "${BACKUP_SERVER_EC_SPKI_PIN:-}" ]]; then
        echo "        pins.Add(Backup.BackupServerEcSpkiPin);" >> "$output_file"
    fi

    cat >> "$output_file" << 'EOF'
        
        return pins.AsReadOnly();
    }

    /// <summary>
    /// Get all pins (primary + backup + root CA) as a collection
    /// </summary>
    public static IReadOnlyCollection<string> GetAllPins()
    {
        List<string> pins = new();
        pins.AddRange(GetPrimaryPins());
        pins.AddRange(GetBackupPins());
        
EOF

    # Add root CA pins to all pins collection
    if [[ -n "${ROOT_RSA_SPKI_PIN:-}" ]]; then
        echo "        pins.Add(RootCA.RootRsaSpkiPin);" >> "$output_file"
    fi
    
    if [[ -n "${ROOT_EC_SPKI_PIN:-}" ]]; then
        echo "        pins.Add(RootCA.RootEcSpkiPin);" >> "$output_file"
    fi

    cat >> "$output_file" << 'EOF'
        
        return pins.AsReadOnly();
    }

    /// <summary>
    /// Pin generation metadata
    /// </summary>
    public static class Metadata
    {
EOF

    echo "        public static readonly DateTime GeneratedAt = DateTime.Parse(\"$(date -u +"%Y-%m-%dT%H:%M:%SZ")\");" >> "$output_file"
    echo "        public const string Version = \"1.0\";" >> "$output_file"

    cat >> "$output_file" << 'EOF'
        public const string Algorithm = "SHA256";
        public const string PinType = "SPKI";
    }
}
EOF

    log_success "C# constants file generated successfully"
}

# Generate JSON configuration
generate_json_config() {
    local output_file="${PINS_DIR}/pins-config.json"
    
    log_info "Generating JSON configuration: $output_file"
    
    cat > "$output_file" << EOF
{
  "version": "1.0",
  "generated_at": "$(date -u +"%Y-%m-%dT%H:%M:%SZ")",
  "algorithm": "SHA256",
  "pin_type": "SPKI",
  "pins": {
    "primary": {
EOF

    # Add primary pins to JSON
    local first_primary=true
    if [[ -n "${SERVER_RSA_SPKI_PIN:-}" ]]; then
        if [[ "$first_primary" != true ]]; then echo "," >> "$output_file"; fi
        echo "      \"server_rsa\": \"${SERVER_RSA_SPKI_PIN}\"" >> "$output_file"
        first_primary=false
    fi
    
    if [[ -n "${SERVER_EC_SPKI_PIN:-}" ]]; then
        if [[ "$first_primary" != true ]]; then echo "," >> "$output_file"; fi
        echo "      \"server_ec\": \"${SERVER_EC_SPKI_PIN}\"" >> "$output_file"
        first_primary=false
    fi
    
    if [[ -n "${INTERMEDIATE_RSA_SPKI_PIN:-}" ]]; then
        if [[ "$first_primary" != true ]]; then echo "," >> "$output_file"; fi
        echo "      \"intermediate_rsa\": \"${INTERMEDIATE_RSA_SPKI_PIN}\"" >> "$output_file"
        first_primary=false
    fi
    
    if [[ -n "${INTERMEDIATE_EC_SPKI_PIN:-}" ]]; then
        if [[ "$first_primary" != true ]]; then echo "," >> "$output_file"; fi
        echo "      \"intermediate_ec\": \"${INTERMEDIATE_EC_SPKI_PIN}\"" >> "$output_file"
        first_primary=false
    fi

    cat >> "$output_file" << EOF
    },
    "backup": {
EOF

    # Add backup pins to JSON
    local first_backup=true
    if [[ -n "${BACKUP_SERVER_RSA_SPKI_PIN:-}" ]]; then
        if [[ "$first_backup" != true ]]; then echo "," >> "$output_file"; fi
        echo "      \"backup_server_rsa\": \"${BACKUP_SERVER_RSA_SPKI_PIN}\"" >> "$output_file"
        first_backup=false
    fi
    
    if [[ -n "${BACKUP_SERVER_EC_SPKI_PIN:-}" ]]; then
        if [[ "$first_backup" != true ]]; then echo "," >> "$output_file"; fi
        echo "      \"backup_server_ec\": \"${BACKUP_SERVER_EC_SPKI_PIN}\"" >> "$output_file"
        first_backup=false
    fi

    cat >> "$output_file" << EOF
    },
    "root_ca": {
EOF

    # Add root CA pins to JSON
    local first_root=true
    if [[ -n "${ROOT_RSA_SPKI_PIN:-}" ]]; then
        if [[ "$first_root" != true ]]; then echo "," >> "$output_file"; fi
        echo "      \"root_rsa\": \"${ROOT_RSA_SPKI_PIN}\"" >> "$output_file"
        first_root=false
    fi
    
    if [[ -n "${ROOT_EC_SPKI_PIN:-}" ]]; then
        if [[ "$first_root" != true ]]; then echo "," >> "$output_file"; fi
        echo "      \"root_ec\": \"${ROOT_EC_SPKI_PIN}\"" >> "$output_file"
        first_root=false
    fi

    cat >> "$output_file" << EOF
    }
  }
}
EOF

    log_success "JSON configuration generated successfully"
}

# Generate shell script for pin verification
generate_verification_script() {
    local output_file="${PINS_DIR}/verify-pins.sh"
    
    log_info "Generating pin verification script: $output_file"
    
    cat > "$output_file" << 'EOF'
#!/bin/bash

# Ecliptix Pin Verification Script
# Verifies that remote server certificates match expected pins
# Author: Ecliptix Security Team

set -euo pipefail

# Expected pins (generated by extract-pins.sh)
EOF

    # Add pin variables
    if [[ -n "${SERVER_RSA_SPKI_PIN:-}" ]]; then
        echo "EXPECTED_SERVER_RSA_PIN=\"${SERVER_RSA_SPKI_PIN}\"" >> "$output_file"
    fi
    
    if [[ -n "${SERVER_EC_SPKI_PIN:-}" ]]; then
        echo "EXPECTED_SERVER_EC_PIN=\"${SERVER_EC_SPKI_PIN}\"" >> "$output_file"
    fi

    cat >> "$output_file" << 'EOF'

# Function to get SPKI pin from remote server
get_remote_pin() {
    local hostname="$1"
    local port="${2:-443}"
    
    echo | openssl s_client -servername "$hostname" -connect "$hostname:$port" 2>/dev/null | \
    openssl x509 -pubkey -noout | \
    openssl pkey -pubin -outform der | \
    openssl dgst -sha256 -binary | \
    base64
}

# Function to verify pin
verify_pin() {
    local hostname="$1"
    local port="${2:-443}"
    local expected_pin="$3"
    local pin_name="$4"
    
    echo "Verifying $pin_name for $hostname:$port..."
    
    local actual_pin
    actual_pin=$(get_remote_pin "$hostname" "$port")
    
    if [[ "$actual_pin" == "$expected_pin" ]]; then
        echo "✓ Pin verification successful for $pin_name"
        return 0
    else
        echo "✗ Pin verification FAILED for $pin_name"
        echo "  Expected: $expected_pin"
        echo "  Actual:   $actual_pin"
        return 1
    fi
}

# Main verification function
main() {
    local hostname="${1:-api.ecliptix.com}"
    local port="${2:-443}"
    
    echo "Verifying SSL pins for $hostname:$port"
    echo "Generated on: $(date)"
    echo
    
    local success=true
    
EOF

    # Add verification calls
    if [[ -n "${SERVER_RSA_SPKI_PIN:-}" ]]; then
        echo "    if ! verify_pin \"$hostname\" \"$port\" \"\$EXPECTED_SERVER_RSA_PIN\" \"Server RSA\"; then" >> "$output_file"
        echo "        success=false" >> "$output_file"
        echo "    fi" >> "$output_file"
        echo >> "$output_file"
    fi
    
    if [[ -n "${SERVER_EC_SPKI_PIN:-}" ]]; then
        echo "    if ! verify_pin \"$hostname\" \"$port\" \"\$EXPECTED_SERVER_EC_PIN\" \"Server EC\"; then" >> "$output_file"
        echo "        success=false" >> "$output_file"
        echo "    fi" >> "$output_file"
        echo >> "$output_file"
    fi

    cat >> "$output_file" << 'EOF'
    
    if [[ "$success" == true ]]; then
        echo "✓ All pin verifications successful!"
        exit 0
    else
        echo "✗ One or more pin verifications failed!"
        exit 1
    fi
}

# Usage information
usage() {
    echo "Usage: $0 [hostname] [port]"
    echo "  hostname: Target hostname (default: api.ecliptix.com)"
    echo "  port:     Target port (default: 443)"
    echo
    echo "Examples:"
    echo "  $0                           # Verify api.ecliptix.com:443"
    echo "  $0 localhost 8443            # Verify localhost:8443"
    echo "  $0 test-server.local         # Verify test-server.local:443"
}

# Handle command line arguments
if [[ $# -gt 2 ]]; then
    usage
    exit 1
fi

if [[ "${1:-}" == "-h" ]] || [[ "${1:-}" == "--help" ]]; then
    usage
    exit 0
fi

# Run verification
main "$@"
EOF

    chmod +x "$output_file"
    log_success "Pin verification script generated successfully"
}

# Main execution function
main() {
    log_info "Ecliptix SSL Pin Extraction Starting..."
    
    check_dependencies
    
    # Create pins directory if it doesn't exist
    mkdir -p "${PINS_DIR}"
    
    # Check if certificates exist
    if [[ ! -d "$CERTS_DIR" ]]; then
        log_error "Certificates directory not found: $CERTS_DIR"
        log_info "Please run './generate-certs.sh' first to generate certificates"
        exit 1
    fi
    
    log_info "Extracting pins from certificates..."
    echo
    
    # Extract pins from all certificate types
    extract_spki_pin "${CERTS_DIR}/server/server-rsa.crt" "SERVER_RSA"
    extract_spki_pin "${CERTS_DIR}/server/server-ec.crt" "SERVER_EC"
    extract_spki_pin "${CERTS_DIR}/intermediate-ca/intermediate-ca-rsa.crt" "INTERMEDIATE_RSA"
    extract_spki_pin "${CERTS_DIR}/intermediate-ca/intermediate-ca-ec.crt" "INTERMEDIATE_EC"
    extract_spki_pin "${CERTS_DIR}/backup/backup-server-rsa.crt" "BACKUP_SERVER_RSA"
    extract_spki_pin "${CERTS_DIR}/backup/backup-server-ec.crt" "BACKUP_SERVER_EC"
    extract_spki_pin "${CERTS_DIR}/root-ca/root-ca-rsa.crt" "ROOT_RSA"
    extract_spki_pin "${CERTS_DIR}/root-ca/root-ca-ec.crt" "ROOT_EC"
    
    # Generate output files
    generate_csharp_constants
    generate_json_config
    generate_verification_script
    
    log_success "Pin extraction completed successfully!"
    log_info "Generated files:"
    log_info "  - C# constants: ${PINS_DIR}/PinnedCertificates.cs"
    log_info "  - JSON config:  ${PINS_DIR}/pins-config.json"
    log_info "  - Verification: ${PINS_DIR}/verify-pins.sh"
    
    echo
    log_info "Next steps:"
    echo "1. Copy PinnedCertificates.cs to your Ecliptix.Security.Pinning project"
    echo "2. Use verify-pins.sh to test pins against your server"
    echo "3. Integrate pins into your SSL pinning implementation"
}

# Handle command line arguments
case "${1:-}" in
    -h|--help)
        echo "Usage: $0"
        echo "Extracts SSL certificate pins from generated certificates"
        echo "Run ./generate-certs.sh first to create certificates"
        exit 0
        ;;
    *)
        main "$@"
        ;;
esac
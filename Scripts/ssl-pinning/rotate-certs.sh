#!/bin/bash

# Ecliptix SSL Certificate Rotation Script
# Manages certificate rotation with zero-downtime and pin updates
# Author: Ecliptix Security Team
# Version: 1.0

set -euo pipefail

# Configuration
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CERTS_DIR="${SCRIPT_DIR}/certificates"
OUTPUT_DIR="${SCRIPT_DIR}/generated"
PINS_DIR="${SCRIPT_DIR}/pins"
ROTATION_DIR="${SCRIPT_DIR}/rotation"
BACKUP_DIR="${SCRIPT_DIR}/backup-$(date +%Y%m%d_%H%M%S)"

# Rotation phases
PHASE_PREPARE="prepare"
PHASE_ACTIVATE="activate"
PHASE_COMPLETE="complete"
PHASE_ROLLBACK="rollback"

# Color codes for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
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

log_phase() {
    echo -e "${CYAN}[PHASE]${NC} $1"
}

# Function to show usage
show_usage() {
    cat << 'EOF'
Ecliptix SSL Certificate Rotation Script

USAGE:
    ./rotate-certs.sh <phase> [options]

PHASES:
    prepare   - Generate new certificates and prepare rotation
    activate  - Activate new certificates (server-side)
    complete  - Complete rotation and update client pins
    rollback  - Rollback to previous certificates

OPTIONS:
    -h, --help              Show this help message
    -f, --force             Force operation without confirmation
    -d, --dry-run          Show what would be done without executing
    --server-host HOST     Server hostname for verification (default: api.ecliptix.com)
    --server-port PORT     Server port for verification (default: 443)
    --backup-path PATH     Custom backup directory path

EXAMPLES:
    ./rotate-certs.sh prepare
    ./rotate-certs.sh activate --server-host api.ecliptix.com
    ./rotate-certs.sh complete --force
    ./rotate-certs.sh rollback --dry-run

ROTATION WORKFLOW:
    1. prepare  - Generate new backup certificates
    2. activate - Deploy new certificates to server infrastructure
    3. complete - Update client pins and remove old certificates
    4. rollback - Revert to previous certificates (if needed)

EOF
}

# Function to check prerequisites
check_prerequisites() {
    local phase="$1"
    
    log_info "Checking prerequisites for phase: $phase"
    
    # Check if OpenSSL is available
    if ! command -v openssl &> /dev/null; then
        log_error "OpenSSL is not installed or not in PATH"
        exit 1
    fi
    
    # Check if certificates directory exists
    if [[ ! -d "$CERTS_DIR" ]]; then
        log_error "Certificates directory not found: $CERTS_DIR"
        log_info "Please run './generate-certs.sh' first to generate initial certificates"
        exit 1
    fi
    
    # Phase-specific checks
    case "$phase" in
        "$PHASE_PREPARE")
            # Check if current certificates exist
            if [[ ! -f "$CERTS_DIR/server/server-rsa.crt" ]]; then
                log_error "Current server certificates not found"
                exit 1
            fi
            ;;
        "$PHASE_ACTIVATE")
            # Check if new certificates are prepared
            if [[ ! -d "$ROTATION_DIR" ]]; then
                log_error "Rotation directory not found. Run 'prepare' phase first."
                exit 1
            fi
            ;;
        "$PHASE_COMPLETE")
            # Check if activation was successful
            if [[ ! -f "$ROTATION_DIR/activation_complete" ]]; then
                log_warning "Activation phase may not be complete"
            fi
            ;;
        "$PHASE_ROLLBACK")
            # Check if backup exists
            if [[ ! -d "$BACKUP_DIR" && ! -d "$(ls -d backup-* 2>/dev/null | head -1)" ]]; then
                log_error "No backup found for rollback"
                exit 1
            fi
            ;;
    esac
    
    log_success "Prerequisites check passed"
}

# Function to create backup
create_backup() {
    local backup_path="${1:-$BACKUP_DIR}"
    
    log_info "Creating backup in: $backup_path"
    
    mkdir -p "$backup_path"
    
    # Backup current certificates
    cp -r "$CERTS_DIR" "$backup_path/"
    cp -r "$PINS_DIR" "$backup_path/" 2>/dev/null || true
    
    # Create backup metadata
    cat > "$backup_path/backup_metadata.json" << EOF
{
    "created_at": "$(date -u +"%Y-%m-%dT%H:%M:%SZ")",
    "backup_type": "pre_rotation",
    "version": "1.0",
    "certificates": {
        "server_rsa": "$(openssl x509 -in "$CERTS_DIR/server/server-rsa.crt" -fingerprint -sha256 -noout)",
        "server_ec": "$(openssl x509 -in "$CERTS_DIR/server/server-ec.crt" -fingerprint -sha256 -noout)"
    }
}
EOF
    
    log_success "Backup created successfully"
}

# Phase 1: Prepare new certificates
phase_prepare() {
    local force="$1"
    local dry_run="$2"
    
    log_phase "Starting PREPARE phase"
    
    if [[ "$dry_run" == "true" ]]; then
        log_info "[DRY RUN] Would generate new certificates and prepare rotation"
        return 0
    fi
    
    # Create rotation directory
    mkdir -p "$ROTATION_DIR"
    
    # Create backup
    create_backup
    
    # Move current backup certificates to be the new primary certificates
    log_info "Promoting backup certificates to primary..."
    
    # Copy backup certificates as new primary
    cp "$CERTS_DIR/backup/backup-server-rsa.crt" "$ROTATION_DIR/new-server-rsa.crt"
    cp "$CERTS_DIR/backup/backup-server-rsa.key" "$ROTATION_DIR/new-server-rsa.key"
    cp "$CERTS_DIR/backup/backup-server-ec.crt" "$ROTATION_DIR/new-server-ec.crt"
    cp "$CERTS_DIR/backup/backup-server-ec.key" "$ROTATION_DIR/new-server-ec.key"
    
    # Generate new backup certificates
    log_info "Generating new backup certificates..."
    
    # Generate new RSA backup certificate
    openssl genrsa -out "$ROTATION_DIR/new-backup-server-rsa.key" 4096
    chmod 400 "$ROTATION_DIR/new-backup-server-rsa.key"
    
    openssl req -new \
        -key "$ROTATION_DIR/new-backup-server-rsa.key" \
        -out "$ROTATION_DIR/new-backup-server-rsa.csr" \
        -config "$SCRIPT_DIR/configs/server.conf" \
        -subj "/C=US/ST=Secure/L=Private/O=Ecliptix Security/OU=Backup Server/CN=api.ecliptix.com"
    
    openssl x509 -req -days 730 \
        -in "$ROTATION_DIR/new-backup-server-rsa.csr" \
        -CA "$CERTS_DIR/intermediate-ca/intermediate-ca-rsa.crt" \
        -CAkey "$CERTS_DIR/intermediate-ca/intermediate-ca-rsa.key" \
        -CAcreateserial \
        -out "$ROTATION_DIR/new-backup-server-rsa.crt" \
        -extensions server_cert \
        -extfile "$SCRIPT_DIR/configs/server.conf"
    
    # Generate new ECDSA backup certificate
    openssl ecparam -name secp384r1 -genkey -out "$ROTATION_DIR/new-backup-server-ec.key"
    chmod 400 "$ROTATION_DIR/new-backup-server-ec.key"
    
    openssl req -new \
        -key "$ROTATION_DIR/new-backup-server-ec.key" \
        -out "$ROTATION_DIR/new-backup-server-ec.csr" \
        -config "$SCRIPT_DIR/configs/server.conf" \
        -subj "/C=US/ST=Secure/L=Private/O=Ecliptix Security/OU=Backup Server/CN=api.ecliptix.com"
    
    openssl x509 -req -days 730 \
        -in "$ROTATION_DIR/new-backup-server-ec.csr" \
        -CA "$CERTS_DIR/intermediate-ca/intermediate-ca-ec.crt" \
        -CAkey "$CERTS_DIR/intermediate-ca/intermediate-ca-ec.key" \
        -CAcreateserial \
        -out "$ROTATION_DIR/new-backup-server-ec.crt" \
        -extensions server_cert \
        -extfile "$SCRIPT_DIR/configs/server.conf"
    
    # Create certificate chains
    cat "$ROTATION_DIR/new-server-rsa.crt" \
        "$CERTS_DIR/intermediate-ca/intermediate-ca-rsa.crt" \
        "$CERTS_DIR/root-ca/root-ca-rsa.crt" > "$ROTATION_DIR/new-fullchain-rsa.crt"
    
    cat "$ROTATION_DIR/new-server-ec.crt" \
        "$CERTS_DIR/intermediate-ca/intermediate-ca-ec.crt" \
        "$CERTS_DIR/root-ca/root-ca-ec.crt" > "$ROTATION_DIR/new-fullchain-ec.crt"
    
    # Extract new pins
    log_info "Extracting pins from new certificates..."
    
    local new_rsa_pin
    new_rsa_pin=$(openssl x509 -in "$ROTATION_DIR/new-server-rsa.crt" -pubkey -noout | \
                  openssl pkey -pubin -outform der | \
                  openssl dgst -sha256 -binary | \
                  base64)
    
    local new_ec_pin
    new_ec_pin=$(openssl x509 -in "$ROTATION_DIR/new-server-ec.crt" -pubkey -noout | \
                 openssl pkey -pubin -outform der | \
                 openssl dgst -sha256 -binary | \
                 base64)
    
    # Create rotation plan
    cat > "$ROTATION_DIR/rotation_plan.json" << EOF
{
    "phase": "prepared",
    "prepared_at": "$(date -u +"%Y-%m-%dT%H:%M:%SZ")",
    "rotation_id": "$(uuidgen)",
    "certificates": {
        "new_primary": {
            "server_rsa": {
                "fingerprint": "$(openssl x509 -in "$ROTATION_DIR/new-server-rsa.crt" -fingerprint -sha256 -noout)",
                "spki_pin": "$new_rsa_pin",
                "expires": "$(openssl x509 -in "$ROTATION_DIR/new-server-rsa.crt" -enddate -noout | cut -d= -f2)"
            },
            "server_ec": {
                "fingerprint": "$(openssl x509 -in "$ROTATION_DIR/new-server-ec.crt" -fingerprint -sha256 -noout)",
                "spki_pin": "$new_ec_pin",
                "expires": "$(openssl x509 -in "$ROTATION_DIR/new-server-ec.crt" -enddate -noout | cut -d= -f2)"
            }
        }
    },
    "backup_location": "$BACKUP_DIR"
}
EOF
    
    # Create deployment package
    log_info "Creating deployment package..."
    
    tar -czf "$ROTATION_DIR/deployment_package.tar.gz" \
        -C "$ROTATION_DIR" \
        new-server-rsa.crt new-server-rsa.key \
        new-server-ec.crt new-server-ec.key \
        new-fullchain-rsa.crt new-fullchain-ec.crt
    
    # Create phase completion marker
    touch "$ROTATION_DIR/prepare_complete"
    
    log_success "PREPARE phase completed successfully"
    log_info "Deployment package: $ROTATION_DIR/deployment_package.tar.gz"
    log_info "Next step: Deploy certificates to server, then run './rotate-certs.sh activate'"
}

# Phase 2: Activate new certificates
phase_activate() {
    local server_host="$1"
    local server_port="$2"
    local force="$3"
    local dry_run="$4"
    
    log_phase "Starting ACTIVATE phase"
    
    if [[ "$dry_run" == "true" ]]; then
        log_info "[DRY RUN] Would verify server is using new certificates"
        return 0
    fi
    
    # Verify new certificates are deployed on server
    log_info "Verifying new certificates are active on $server_host:$server_port"
    
    local remote_rsa_pin
    remote_rsa_pin=$(echo | openssl s_client -servername "$server_host" -connect "$server_host:$server_port" 2>/dev/null | \
                     openssl x509 -pubkey -noout | \
                     openssl pkey -pubin -outform der | \
                     openssl dgst -sha256 -binary | \
                     base64)
    
    # Get expected pin from rotation plan
    local expected_rsa_pin
    expected_rsa_pin=$(jq -r '.certificates.new_primary.server_rsa.spki_pin' "$ROTATION_DIR/rotation_plan.json")
    
    if [[ "$remote_rsa_pin" == "$expected_rsa_pin" ]]; then
        log_success "New certificates are active on server"
    else
        log_error "Server is not using expected new certificates"
        log_error "Expected pin: $expected_rsa_pin"
        log_error "Actual pin:   $remote_rsa_pin"
        
        if [[ "$force" != "true" ]]; then
            log_error "Use --force to continue anyway"
            exit 1
        else
            log_warning "Continuing with --force flag..."
        fi
    fi
    
    # Update rotation plan
    jq '.phase = "activated" | .activated_at = now | .verified_pins.server_rsa = "'$remote_rsa_pin'"' \
        "$ROTATION_DIR/rotation_plan.json" > "$ROTATION_DIR/rotation_plan_tmp.json"
    mv "$ROTATION_DIR/rotation_plan_tmp.json" "$ROTATION_DIR/rotation_plan.json"
    
    # Create activation marker
    touch "$ROTATION_DIR/activation_complete"
    
    log_success "ACTIVATE phase completed successfully"
    log_info "Next step: Run './rotate-certs.sh complete' to update client pins"
}

# Phase 3: Complete rotation
phase_complete() {
    local force="$3"
    local dry_run="$4"
    
    log_phase "Starting COMPLETE phase"
    
    if [[ "$dry_run" == "true" ]]; then
        log_info "[DRY RUN] Would update certificate storage and generate new pins"
        return 0
    fi
    
    # Move new certificates to primary location
    log_info "Updating primary certificates..."
    
    cp "$ROTATION_DIR/new-server-rsa.crt" "$CERTS_DIR/server/server-rsa.crt"
    cp "$ROTATION_DIR/new-server-rsa.key" "$CERTS_DIR/server/server-rsa.key"
    cp "$ROTATION_DIR/new-server-ec.crt" "$CERTS_DIR/server/server-ec.crt"
    cp "$ROTATION_DIR/new-server-ec.key" "$CERTS_DIR/server/server-ec.key"
    cp "$ROTATION_DIR/new-fullchain-rsa.crt" "$CERTS_DIR/server/fullchain-rsa.crt"
    cp "$ROTATION_DIR/new-fullchain-ec.crt" "$CERTS_DIR/server/fullchain-ec.crt"
    
    # Update backup certificates
    cp "$ROTATION_DIR/new-backup-server-rsa.crt" "$CERTS_DIR/backup/backup-server-rsa.crt"
    cp "$ROTATION_DIR/new-backup-server-rsa.key" "$CERTS_DIR/backup/backup-server-rsa.key"
    cp "$ROTATION_DIR/new-backup-server-ec.crt" "$CERTS_DIR/backup/backup-server-ec.crt"
    cp "$ROTATION_DIR/new-backup-server-ec.key" "$CERTS_DIR/backup/backup-server-ec.key"
    
    # Regenerate pins
    log_info "Regenerating certificate pins..."
    "$SCRIPT_DIR/extract-pins.sh"
    
    # Update rotation plan
    jq '.phase = "completed" | .completed_at = now' \
        "$ROTATION_DIR/rotation_plan.json" > "$ROTATION_DIR/rotation_plan_tmp.json"
    mv "$ROTATION_DIR/rotation_plan_tmp.json" "$ROTATION_DIR/rotation_plan.json"
    
    # Archive rotation directory
    log_info "Archiving rotation data..."
    mv "$ROTATION_DIR" "${ROTATION_DIR}_completed_$(date +%Y%m%d_%H%M%S)"
    
    log_success "COMPLETE phase finished successfully"
    log_info "Certificate rotation completed!"
    log_info "Updated pins are available in: $PINS_DIR/PinnedCertificates.cs"
}

# Phase 4: Rollback
phase_rollback() {
    local backup_path="$1"
    local force="$3"
    local dry_run="$4"
    
    log_phase "Starting ROLLBACK phase"
    
    # Find backup directory if not specified
    if [[ -z "$backup_path" ]]; then
        backup_path=$(ls -d backup-* 2>/dev/null | sort -r | head -1)
        if [[ -z "$backup_path" ]]; then
            log_error "No backup directory found"
            exit 1
        fi
        log_info "Using backup: $backup_path"
    fi
    
    if [[ "$dry_run" == "true" ]]; then
        log_info "[DRY RUN] Would restore certificates from: $backup_path"
        return 0
    fi
    
    if [[ "$force" != "true" ]]; then
        echo
        log_warning "This will restore certificates from backup: $backup_path"
        read -p "Are you sure you want to continue? (y/N): " -r
        if [[ ! $REPLY =~ ^[Yy]$ ]]; then
            log_info "Rollback cancelled"
            exit 0
        fi
    fi
    
    # Restore certificates from backup
    log_info "Restoring certificates from backup..."
    
    if [[ -d "$backup_path/certificates" ]]; then
        cp -r "$backup_path/certificates/"* "$CERTS_DIR/"
    else
        log_error "Invalid backup directory structure"
        exit 1
    fi
    
    # Restore pins if available
    if [[ -d "$backup_path/pins" ]]; then
        cp -r "$backup_path/pins/"* "$PINS_DIR/"
    fi
    
    # Create rollback record
    cat > "$SCRIPT_DIR/last_rollback.json" << EOF
{
    "rolled_back_at": "$(date -u +"%Y-%m-%dT%H:%M:%SZ")",
    "backup_used": "$backup_path",
    "restoration_successful": true
}
EOF
    
    log_success "ROLLBACK phase completed successfully"
    log_info "Certificates have been restored from backup"
    log_warning "Remember to redeploy certificates to your server infrastructure"
}

# Main function
main() {
    local phase=""
    local force=false
    local dry_run=false
    local server_host="api.ecliptix.com"
    local server_port="443"
    local backup_path=""
    
    # Parse command line arguments
    while [[ $# -gt 0 ]]; do
        case $1 in
            prepare|activate|complete|rollback)
                phase="$1"
                shift
                ;;
            -f|--force)
                force=true
                shift
                ;;
            -d|--dry-run)
                dry_run=true
                shift
                ;;
            --server-host)
                server_host="$2"
                shift 2
                ;;
            --server-port)
                server_port="$2"
                shift 2
                ;;
            --backup-path)
                backup_path="$2"
                shift 2
                ;;
            -h|--help)
                show_usage
                exit 0
                ;;
            *)
                log_error "Unknown option: $1"
                show_usage
                exit 1
                ;;
        esac
    done
    
    # Validate phase argument
    if [[ -z "$phase" ]]; then
        log_error "Phase argument is required"
        show_usage
        exit 1
    fi
    
    log_info "Ecliptix SSL Certificate Rotation"
    log_info "Phase: $phase"
    if [[ "$dry_run" == true ]]; then
        log_info "Mode: DRY RUN"
    fi
    echo
    
    # Check prerequisites
    check_prerequisites "$phase"
    
    # Execute phase
    case "$phase" in
        "$PHASE_PREPARE")
            phase_prepare "$force" "$dry_run"
            ;;
        "$PHASE_ACTIVATE")
            phase_activate "$server_host" "$server_port" "$force" "$dry_run"
            ;;
        "$PHASE_COMPLETE")
            phase_complete "$force" "$dry_run"
            ;;
        "$PHASE_ROLLBACK")
            phase_rollback "$backup_path" "$force" "$dry_run"
            ;;
        *)
            log_error "Invalid phase: $phase"
            exit 1
            ;;
    esac
}

# Run main function with all arguments
main "$@"
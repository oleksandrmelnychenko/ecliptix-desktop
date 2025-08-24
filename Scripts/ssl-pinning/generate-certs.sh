#!/bin/bash

# Ecliptix SSL Certificate Generation Script
# Generates a complete PKI infrastructure with multiple security levels
# Author: Ecliptix Security Team
# Version: 1.0

set -euo pipefail

# Configuration
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CERTS_DIR="${SCRIPT_DIR}/certificates"
OUTPUT_DIR="${SCRIPT_DIR}/generated"
PINS_DIR="${SCRIPT_DIR}/pins"
CONFIG_DIR="${SCRIPT_DIR}/configs"

# Certificate validity periods (in days)
ROOT_CA_DAYS=7300      # 20 years
INTERMEDIATE_DAYS=3650  # 10 years
SERVER_DAYS=365        # 1 year
BACKUP_DAYS=730        # 2 years

# Key sizes
RSA_KEY_SIZE=4096
EC_CURVE="secp384r1"

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
check_openssl() {
    if ! command -v openssl &> /dev/null; then
        log_error "OpenSSL is not installed or not in PATH"
        exit 1
    fi
    log_info "OpenSSL version: $(openssl version)"
}

# Create directory structure
create_directories() {
    log_info "Creating directory structure..."
    mkdir -p "${CERTS_DIR}"/{root-ca,intermediate-ca,server,backup}
    mkdir -p "${OUTPUT_DIR}"/{certs,keys,pins}
    mkdir -p "${PINS_DIR}"
    mkdir -p "${CONFIG_DIR}"
    
    # Set secure permissions
    chmod 700 "${CERTS_DIR}" "${OUTPUT_DIR}" "${PINS_DIR}"
}

# Generate OpenSSL configuration files
generate_configs() {
    log_info "Generating OpenSSL configuration files..."
    
    # Root CA config
    cat > "${CONFIG_DIR}/root-ca.conf" << 'EOF'
[ req ]
default_bits = 4096
distinguished_name = req_distinguished_name
string_mask = utf8only
default_md = sha384
x509_extensions = v3_ca

[ req_distinguished_name ]
countryName = Country Name
stateOrProvinceName = State or Province Name
localityName = Locality Name
organizationName = Organization Name
organizationalUnitName = Organizational Unit Name
commonName = Common Name

[ v3_ca ]
subjectKeyIdentifier = hash
authorityKeyIdentifier = keyid:always,issuer
basicConstraints = critical, CA:true
keyUsage = critical, digitalSignature, cRLSign, keyCertSign
EOF

    # Intermediate CA config
    cat > "${CONFIG_DIR}/intermediate-ca.conf" << 'EOF'
[ req ]
default_bits = 4096
distinguished_name = req_distinguished_name
string_mask = utf8only
default_md = sha384
req_extensions = v3_intermediate_ca

[ req_distinguished_name ]
countryName = Country Name
stateOrProvinceName = State or Province Name
localityName = Locality Name
organizationName = Organization Name
organizationalUnitName = Organizational Unit Name
commonName = Common Name

[ v3_intermediate_ca ]
subjectKeyIdentifier = hash
authorityKeyIdentifier = keyid:always,issuer
basicConstraints = critical, CA:true, pathlen:0
keyUsage = critical, digitalSignature, cRLSign, keyCertSign
EOF

    # Server certificate config
    cat > "${CONFIG_DIR}/server.conf" << 'EOF'
[ req ]
default_bits = 4096
distinguished_name = req_distinguished_name
string_mask = utf8only
default_md = sha384
req_extensions = server_cert

[ req_distinguished_name ]
countryName = Country Name
stateOrProvinceName = State or Province Name
localityName = Locality Name
organizationName = Organization Name
organizationalUnitName = Organizational Unit Name
commonName = Common Name

[ server_cert ]
basicConstraints = CA:FALSE
nsCertType = server
nsComment = "Ecliptix Server Certificate"
subjectKeyIdentifier = hash
authorityKeyIdentifier = keyid,issuer:always
keyUsage = critical, digitalSignature, keyEncipherment
extendedKeyUsage = serverAuth, clientAuth
subjectAltName = @alt_names

[ alt_names ]
DNS.1 = localhost
DNS.2 = *.ecliptix.local
DNS.3 = ecliptix.api
DNS.4 = api.ecliptix.com
IP.1 = 127.0.0.1
IP.2 = ::1
EOF
}

# Generate Root CA
generate_root_ca() {
    log_info "Generating Root CA..."
    
    # RSA Root CA
    openssl genrsa -out "${CERTS_DIR}/root-ca/root-ca-rsa.key" ${RSA_KEY_SIZE}
    chmod 400 "${CERTS_DIR}/root-ca/root-ca-rsa.key"
    
    openssl req -new -x509 -days ${ROOT_CA_DAYS} \
        -key "${CERTS_DIR}/root-ca/root-ca-rsa.key" \
        -out "${CERTS_DIR}/root-ca/root-ca-rsa.crt" \
        -config "${CONFIG_DIR}/root-ca.conf" \
        -extensions v3_ca \
        -subj "/C=US/ST=Secure/L=Private/O=Ecliptix Security/OU=Root CA/CN=Ecliptix Root CA RSA"
    
    # ECDSA Root CA
    openssl ecparam -name ${EC_CURVE} -genkey -out "${CERTS_DIR}/root-ca/root-ca-ec.key"
    chmod 400 "${CERTS_DIR}/root-ca/root-ca-ec.key"
    
    openssl req -new -x509 -days ${ROOT_CA_DAYS} \
        -key "${CERTS_DIR}/root-ca/root-ca-ec.key" \
        -out "${CERTS_DIR}/root-ca/root-ca-ec.crt" \
        -config "${CONFIG_DIR}/root-ca.conf" \
        -extensions v3_ca \
        -subj "/C=US/ST=Secure/L=Private/O=Ecliptix Security/OU=Root CA/CN=Ecliptix Root CA ECDSA"
    
    log_success "Root CA certificates generated"
}

# Generate Intermediate CA
generate_intermediate_ca() {
    log_info "Generating Intermediate CA..."
    
    # RSA Intermediate CA
    openssl genrsa -out "${CERTS_DIR}/intermediate-ca/intermediate-ca-rsa.key" ${RSA_KEY_SIZE}
    chmod 400 "${CERTS_DIR}/intermediate-ca/intermediate-ca-rsa.key"
    
    openssl req -new \
        -key "${CERTS_DIR}/intermediate-ca/intermediate-ca-rsa.key" \
        -out "${CERTS_DIR}/intermediate-ca/intermediate-ca-rsa.csr" \
        -config "${CONFIG_DIR}/intermediate-ca.conf" \
        -subj "/C=US/ST=Secure/L=Private/O=Ecliptix Security/OU=Intermediate CA/CN=Ecliptix Intermediate CA RSA"
    
    openssl x509 -req -days ${INTERMEDIATE_DAYS} \
        -in "${CERTS_DIR}/intermediate-ca/intermediate-ca-rsa.csr" \
        -CA "${CERTS_DIR}/root-ca/root-ca-rsa.crt" \
        -CAkey "${CERTS_DIR}/root-ca/root-ca-rsa.key" \
        -CAcreateserial \
        -out "${CERTS_DIR}/intermediate-ca/intermediate-ca-rsa.crt" \
        -extensions v3_intermediate_ca \
        -extfile "${CONFIG_DIR}/intermediate-ca.conf"
    
    # ECDSA Intermediate CA
    openssl ecparam -name ${EC_CURVE} -genkey -out "${CERTS_DIR}/intermediate-ca/intermediate-ca-ec.key"
    chmod 400 "${CERTS_DIR}/intermediate-ca/intermediate-ca-ec.key"
    
    openssl req -new \
        -key "${CERTS_DIR}/intermediate-ca/intermediate-ca-ec.key" \
        -out "${CERTS_DIR}/intermediate-ca/intermediate-ca-ec.csr" \
        -config "${CONFIG_DIR}/intermediate-ca.conf" \
        -subj "/C=US/ST=Secure/L=Private/O=Ecliptix Security/OU=Intermediate CA/CN=Ecliptix Intermediate CA ECDSA"
    
    openssl x509 -req -days ${INTERMEDIATE_DAYS} \
        -in "${CERTS_DIR}/intermediate-ca/intermediate-ca-ec.csr" \
        -CA "${CERTS_DIR}/root-ca/root-ca-ec.crt" \
        -CAkey "${CERTS_DIR}/root-ca/root-ca-ec.key" \
        -CAcreateserial \
        -out "${CERTS_DIR}/intermediate-ca/intermediate-ca-ec.crt" \
        -extensions v3_intermediate_ca \
        -extfile "${CONFIG_DIR}/intermediate-ca.conf"
    
    # Create certificate chains
    cat "${CERTS_DIR}/intermediate-ca/intermediate-ca-rsa.crt" \
        "${CERTS_DIR}/root-ca/root-ca-rsa.crt" > "${CERTS_DIR}/intermediate-ca/chain-rsa.crt"
    
    cat "${CERTS_DIR}/intermediate-ca/intermediate-ca-ec.crt" \
        "${CERTS_DIR}/root-ca/root-ca-ec.crt" > "${CERTS_DIR}/intermediate-ca/chain-ec.crt"
    
    log_success "Intermediate CA certificates generated"
}

# Generate Server Certificates
generate_server_certificates() {
    log_info "Generating server certificates..."
    
    # Primary RSA Server Certificate
    openssl genrsa -out "${CERTS_DIR}/server/server-rsa.key" ${RSA_KEY_SIZE}
    chmod 400 "${CERTS_DIR}/server/server-rsa.key"
    
    openssl req -new \
        -key "${CERTS_DIR}/server/server-rsa.key" \
        -out "${CERTS_DIR}/server/server-rsa.csr" \
        -config "${CONFIG_DIR}/server.conf" \
        -subj "/C=US/ST=Secure/L=Private/O=Ecliptix Security/OU=Server/CN=api.ecliptix.com"
    
    openssl x509 -req -days ${SERVER_DAYS} \
        -in "${CERTS_DIR}/server/server-rsa.csr" \
        -CA "${CERTS_DIR}/intermediate-ca/intermediate-ca-rsa.crt" \
        -CAkey "${CERTS_DIR}/intermediate-ca/intermediate-ca-rsa.key" \
        -CAcreateserial \
        -out "${CERTS_DIR}/server/server-rsa.crt" \
        -extensions server_cert \
        -extfile "${CONFIG_DIR}/server.conf"
    
    # Primary ECDSA Server Certificate
    openssl ecparam -name ${EC_CURVE} -genkey -out "${CERTS_DIR}/server/server-ec.key"
    chmod 400 "${CERTS_DIR}/server/server-ec.key"
    
    openssl req -new \
        -key "${CERTS_DIR}/server/server-ec.key" \
        -out "${CERTS_DIR}/server/server-ec.csr" \
        -config "${CONFIG_DIR}/server.conf" \
        -subj "/C=US/ST=Secure/L=Private/O=Ecliptix Security/OU=Server/CN=api.ecliptix.com"
    
    openssl x509 -req -days ${SERVER_DAYS} \
        -in "${CERTS_DIR}/server/server-ec.csr" \
        -CA "${CERTS_DIR}/intermediate-ca/intermediate-ca-ec.crt" \
        -CAkey "${CERTS_DIR}/intermediate-ca/intermediate-ca-ec.key" \
        -CAcreateserial \
        -out "${CERTS_DIR}/server/server-ec.crt" \
        -extensions server_cert \
        -extfile "${CONFIG_DIR}/server.conf"
    
    # Create server certificate chains
    cat "${CERTS_DIR}/server/server-rsa.crt" \
        "${CERTS_DIR}/intermediate-ca/intermediate-ca-rsa.crt" \
        "${CERTS_DIR}/root-ca/root-ca-rsa.crt" > "${CERTS_DIR}/server/fullchain-rsa.crt"
    
    cat "${CERTS_DIR}/server/server-ec.crt" \
        "${CERTS_DIR}/intermediate-ca/intermediate-ca-ec.crt" \
        "${CERTS_DIR}/root-ca/root-ca-ec.crt" > "${CERTS_DIR}/server/fullchain-ec.crt"
    
    log_success "Server certificates generated"
}

# Generate Backup Certificates (for rotation)
generate_backup_certificates() {
    log_info "Generating backup certificates for rotation..."
    
    # Backup RSA Server Certificate
    openssl genrsa -out "${CERTS_DIR}/backup/backup-server-rsa.key" ${RSA_KEY_SIZE}
    chmod 400 "${CERTS_DIR}/backup/backup-server-rsa.key"
    
    openssl req -new \
        -key "${CERTS_DIR}/backup/backup-server-rsa.key" \
        -out "${CERTS_DIR}/backup/backup-server-rsa.csr" \
        -config "${CONFIG_DIR}/server.conf" \
        -subj "/C=US/ST=Secure/L=Private/O=Ecliptix Security/OU=Backup Server/CN=api.ecliptix.com"
    
    openssl x509 -req -days ${BACKUP_DAYS} \
        -in "${CERTS_DIR}/backup/backup-server-rsa.csr" \
        -CA "${CERTS_DIR}/intermediate-ca/intermediate-ca-rsa.crt" \
        -CAkey "${CERTS_DIR}/intermediate-ca/intermediate-ca-rsa.key" \
        -CAcreateserial \
        -out "${CERTS_DIR}/backup/backup-server-rsa.crt" \
        -extensions server_cert \
        -extfile "${CONFIG_DIR}/server.conf"
    
    # Backup ECDSA Server Certificate
    openssl ecparam -name ${EC_CURVE} -genkey -out "${CERTS_DIR}/backup/backup-server-ec.key"
    chmod 400 "${CERTS_DIR}/backup/backup-server-ec.key"
    
    openssl req -new \
        -key "${CERTS_DIR}/backup/backup-server-ec.key" \
        -out "${CERTS_DIR}/backup/backup-server-ec.csr" \
        -config "${CONFIG_DIR}/server.conf" \
        -subj "/C=US/ST=Secure/L=Private/O=Ecliptix Security/OU=Backup Server/CN=api.ecliptix.com"
    
    openssl x509 -req -days ${BACKUP_DAYS} \
        -in "${CERTS_DIR}/backup/backup-server-ec.csr" \
        -CA "${CERTS_DIR}/intermediate-ca/intermediate-ca-ec.crt" \
        -CAkey "${CERTS_DIR}/intermediate-ca/intermediate-ca-ec.key" \
        -CAcreateserial \
        -out "${CERTS_DIR}/backup/backup-server-ec.crt" \
        -extensions server_cert \
        -extfile "${CONFIG_DIR}/server.conf"
    
    log_success "Backup certificates generated"
}

# Copy certificates to output directory
organize_output() {
    log_info "Organizing certificates in output directory..."
    
    # Copy certificates
    cp "${CERTS_DIR}"/root-ca/*.crt "${OUTPUT_DIR}/certs/"
    cp "${CERTS_DIR}"/intermediate-ca/*.crt "${OUTPUT_DIR}/certs/"
    cp "${CERTS_DIR}"/server/*.crt "${OUTPUT_DIR}/certs/"
    cp "${CERTS_DIR}"/backup/*.crt "${OUTPUT_DIR}/certs/"
    
    # Copy private keys (secure)
    cp "${CERTS_DIR}"/server/server-*.key "${OUTPUT_DIR}/keys/"
    cp "${CERTS_DIR}"/backup/backup-server-*.key "${OUTPUT_DIR}/keys/"
    
    # Set secure permissions
    chmod 600 "${OUTPUT_DIR}/keys/"*
    chmod 644 "${OUTPUT_DIR}/certs/"*
}

# Generate certificate information
generate_cert_info() {
    log_info "Generating certificate information..."
    
    {
        echo "# Ecliptix SSL Certificate Information"
        echo "Generated on: $(date)"
        echo
        
        echo "## Root CA Certificates"
        echo "### RSA Root CA"
        openssl x509 -in "${CERTS_DIR}/root-ca/root-ca-rsa.crt" -text -noout | head -20
        echo
        
        echo "### ECDSA Root CA"
        openssl x509 -in "${CERTS_DIR}/root-ca/root-ca-ec.crt" -text -noout | head -20
        echo
        
        echo "## Server Certificates"
        echo "### RSA Server Certificate"
        openssl x509 -in "${CERTS_DIR}/server/server-rsa.crt" -text -noout | head -20
        echo
        
        echo "### ECDSA Server Certificate"
        openssl x509 -in "${CERTS_DIR}/server/server-ec.crt" -text -noout | head -20
        echo
        
    } > "${OUTPUT_DIR}/certificate-info.txt"
}

# Main execution
main() {
    log_info "Ecliptix SSL Certificate Generation Starting..."
    
    check_openssl
    create_directories
    generate_configs
    generate_root_ca
    generate_intermediate_ca
    generate_server_certificates
    generate_backup_certificates
    organize_output
    generate_cert_info
    
    log_success "Certificate generation completed successfully!"
    log_info "Certificates are stored in: ${OUTPUT_DIR}"
    log_warning "Keep private keys secure and backup root CA safely!"
    
    echo
    log_info "Next steps:"
    echo "1. Run './extract-pins.sh' to generate certificate pins"
    echo "2. Copy server certificates to your server infrastructure"
    echo "3. Integrate pins into your client application"
}

# Run if executed directly
if [[ "${BASH_SOURCE[0]}" == "${0}" ]]; then
    main "$@"
fi
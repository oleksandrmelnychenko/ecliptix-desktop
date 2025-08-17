#!/bin/bash

# Script to run pre-PR validation checks
# Usage: ./scripts/pr-checks.sh

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Function to print colored output
print_info() {
    echo -e "${GREEN}ℹ️  $1${NC}"
}

print_warning() {
    echo -e "${YELLOW}⚠️  $1${NC}"
}

print_error() {
    echo -e "${RED}❌ $1${NC}"
}

print_step() {
    echo -e "${BLUE}🔍 $1${NC}"
}

print_success() {
    echo -e "${GREEN}✅ $1${NC}"
}

# Check if we're in a git repository
if ! git rev-parse --git-dir > /dev/null 2>&1; then
    print_error "Not in a git repository"
    exit 1
fi

# Check if .NET is available
if ! command -v dotnet &> /dev/null; then
    print_error ".NET SDK is not installed or not in PATH"
    exit 1
fi

echo "🚀 Running pre-PR validation checks..."
echo ""

# Initialize error counter
ERRORS=0

# Check 1: Verify all files are staged
print_step "Checking if all files are staged..."
if ! git diff --quiet; then
    print_warning "You have unstaged changes. Consider running 'git add .' first"
    git status --short
fi

# Check 2: Restore dependencies
print_step "Restoring NuGet packages..."
if dotnet restore --verbosity quiet; then
    print_success "Dependencies restored"
else
    print_error "Failed to restore dependencies"
    ((ERRORS++))
fi

# Check 3: Build solution
print_step "Building solution..."
if dotnet build --configuration Release --no-restore --verbosity quiet; then
    print_success "Build successful"
else
    print_error "Build failed"
    ((ERRORS++))
fi

# Check 4: Run tests
print_step "Running tests..."
if dotnet test --configuration Release --no-build --verbosity quiet --logger "console;verbosity=minimal"; then
    print_success "All tests passed"
else
    print_error "Tests failed"
    ((ERRORS++))
fi

# Check 5: Code formatting
print_step "Checking code formatting..."
if dotnet format --verify-no-changes --verbosity quiet; then
    print_success "Code formatting is correct"
else
    print_warning "Code formatting issues found. Run 'dotnet format' to fix them"
    print_info "Running dotnet format automatically..."
    if dotnet format --verbosity quiet; then
        print_success "Code formatting fixed"
        print_warning "Remember to commit the formatting changes"
    else
        print_error "Failed to fix code formatting"
        ((ERRORS++))
    fi
fi

# Check 6: Security checks
print_step "Running security checks..."

# Check for hardcoded secrets
SECRET_CHECK=$(grep -r "password\|secret\|key" --include="*.cs" --exclude-dir=obj --exclude-dir=bin . | grep -v "Password\|Secret\|Key" | grep -v "//" || true)
if [ -n "$SECRET_CHECK" ]; then
    print_error "Potential hardcoded secrets found:"
    echo "$SECRET_CHECK"
    ((ERRORS++))
else
    print_success "No hardcoded secrets detected"
fi

# Check for insecure random usage
RANDOM_CHECK=$(grep -r "Random(" --include="*.cs" --exclude-dir=obj --exclude-dir=bin . | grep -v "RNGCryptoServiceProvider\|RandomNumberGenerator" || true)
if [ -n "$RANDOM_CHECK" ]; then
    print_error "Insecure Random usage found:"
    echo "$RANDOM_CHECK"
    ((ERRORS++))
else
    print_success "No insecure Random usage detected"
fi

# Check for deprecated crypto
CRYPTO_CHECK=$(grep -r "MD5\|SHA1\|DES\|RC4" --include="*.cs" --exclude-dir=obj --exclude-dir=bin . || true)
if [ -n "$CRYPTO_CHECK" ]; then
    print_error "Deprecated cryptographic algorithms found:"
    echo "$CRYPTO_CHECK"
    ((ERRORS++))
else
    print_success "No deprecated crypto algorithms detected"
fi

# Check 7: Vulnerable packages
print_step "Checking for vulnerable packages..."
VULN_OUTPUT=$(dotnet list package --vulnerable --include-transitive 2>/dev/null || true)
if echo "$VULN_OUTPUT" | grep -q "has the following vulnerable packages"; then
    print_error "Vulnerable packages found:"
    echo "$VULN_OUTPUT"
    ((ERRORS++))
else
    print_success "No vulnerable packages detected"
fi

# Check 8: Coding style rules
print_step "Checking coding style rules..."

# Check for var usage
VAR_CHECK=$(grep -r "\bvar\b" --include="*.cs" --exclude-dir=obj --exclude-dir=bin . | grep -v "// var allowed" || true)
if [ -n "$VAR_CHECK" ]; then
    print_error "Found 'var' usage (should use explicit types):"
    echo "$VAR_CHECK"
    ((ERRORS++))
else
    print_success "No 'var' usage found (explicit types used)"
fi

# Check for code comments in methods
COMMENT_CHECK=$(grep -r "^\s*//.*" --include="*.cs" --exclude-dir=obj --exclude-dir=bin . | grep -v "^\s*//" | head -5 || true)
if [ -n "$COMMENT_CHECK" ]; then
    print_warning "Code comments found in files (consider removing per style guide):"
    echo "$COMMENT_CHECK" | head -3
    if [ $(echo "$COMMENT_CHECK" | wc -l) -gt 3 ]; then
        echo "... and $(( $(echo "$COMMENT_CHECK" | wc -l) - 3 )) more"
    fi
fi

echo ""
echo "📊 Validation Summary:"
if [ $ERRORS -eq 0 ]; then
    print_success "All checks passed! Your code is ready for PR 🎉"
    echo ""
    echo "Next steps:"
    echo "  1. git add ."
    echo "  2. git commit -m \"your commit message\""
    echo "  3. git push -u origin \$(git branch --show-current)"
    echo "  4. Create pull request on GitHub"
else
    print_error "Found $ERRORS issue(s) that need to be fixed before creating PR"
    exit 1
fi
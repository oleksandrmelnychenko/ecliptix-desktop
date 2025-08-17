# Pull Request

## Description
Brief description of the changes and their purpose.

## Type of Change
- [ ] Bug fix (non-breaking change which fixes an issue)
- [ ] New feature (non-breaking change which adds functionality)
- [ ] Breaking change (fix or feature that would cause existing functionality to not work as expected)
- [ ] Documentation update
- [ ] Refactoring (no functional changes)
- [ ] Performance improvement
- [ ] Security enhancement

## Component Areas
- [ ] Authentication/OPAQUE protocol
- [ ] Cryptography/Security
- [ ] Network/gRPC
- [ ] UI/Avalonia
- [ ] Data storage
- [ ] Build/Configuration

## Testing
- [ ] I have tested this change locally
- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] Manual testing completed
- [ ] Security testing completed (for crypto/auth changes)

## Security Checklist (if applicable)
- [ ] No hardcoded secrets or keys
- [ ] Cryptographically secure random number generation used
- [ ] No deprecated cryptographic algorithms
- [ ] Input validation implemented
- [ ] Error handling doesn't leak sensitive information

## Code Quality
- [ ] Code follows the project's coding style rules
- [ ] No `var` keywords used (explicit types only)
- [ ] Expression-bodied members used for single-line methods
- [ ] No code comments added within methods/properties
- [ ] Code analysis warnings resolved

## Documentation
- [ ] Code is self-documenting with clear naming
- [ ] README updated (if applicable)
- [ ] CLAUDE.md updated (if applicable)
- [ ] Architecture documentation updated (if applicable)

## Dependencies
- [ ] No new vulnerabilities introduced
- [ ] Package updates reviewed for security
- [ ] MCP server configurations updated (if applicable)

## Additional Notes
Add any additional context, screenshots, or notes for reviewers.

## Related Issues
Closes #(issue number)

---

**Review Guidelines:**
- Ensure all cryptographic changes are reviewed by someone familiar with security protocols
- Verify that coding style rules are followed
- Check that all files are properly added to git (`git add .`)
- Confirm CI checks pass before merging
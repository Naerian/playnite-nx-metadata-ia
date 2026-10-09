# Security Policy

## Supported versions

Security fixes are applied to the latest published release of Metadata AI. Older versions may not receive backports.

## Reporting a vulnerability

Please **do not** open a public GitHub issue for security reports (including credential handling, secret leakage, or unsafe defaults).

Use one of these private channels instead:

1. **GitHub private vulnerability reporting** (preferred): open a security advisory from the repository’s **Security** tab → **Advisories** / **Report a vulnerability**.
2. **Email**: contact the maintainer privately if GitHub reporting is unavailable.

Include, when you can:

- Affected version(s)
- A clear description of the issue and impact
- Steps to reproduce, or file/line references
- A suggested fix or patch, if you have one

You should receive an acknowledgement within a few days. Please keep the report private until a fix is released or we agree on disclosure timing.

## Scope notes

Metadata AI stores API keys and related secrets on the local machine (Playnite extension settings). Reports about how those secrets are protected, logged, copied between provider profiles, or exposed outside the intended local scope are in scope.

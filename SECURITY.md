# Security policy

## Reporting a vulnerability

Report vulnerabilities privately through GitHub's security advisories:
<https://github.com/ransagy/poe-regex/security/advisories/new>.

Please do not open a public issue for a security report. Include the affected
component (engine, CLI, web workbench, data pipeline) and a minimal
reproduction. You can expect an initial response within a few days.

## Scope

- The web workbench ("Renly's Recipes") is a static, serverless Blazor
  WebAssembly app: no backend, no accounts, and state lives only in the
  browser's localStorage. The deployed copy is served by Cloudflare Pages.
- The CLI and the data-build tooling run locally on the user's machine.
- No secrets, tokens, or credentials are stored in this repository.

## Supported versions

The project is developed on `main`; fixes land there. Use the latest commit or
the latest release.

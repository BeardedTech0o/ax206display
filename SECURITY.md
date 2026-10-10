# Security policy

## Reporting a vulnerability

Please report security problems privately, not in a public issue. Use GitHub's
[private vulnerability reporting](https://github.com/BeardedTech0o/ax206display/security/advisories/new)
for this repository.

Include what you found, how to reproduce it, and which version or release you
tested. You will get a reply as soon as the maintainer can manage; this is a
hobby project, so there is no fixed response time.

## Supported versions

Only the latest release gets security fixes.

## Things worth knowing

- The Linux web UI speaks plain HTTP on port 8206 and listens on all
  interfaces by default. Keep it on a trusted network and do not forward the
  port to the internet. See the security notes in
  [docs/raspberry-pi.md](docs/raspberry-pi.md#security-notes) for putting it
  behind an HTTPS reverse proxy.
- Integration passwords are stored encrypted (DPAPI on Windows, AES-256-GCM
  with an owner-only key file on Linux), but anyone who can read both files as
  the service account can decrypt them.

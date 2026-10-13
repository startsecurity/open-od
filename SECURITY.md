# Security Policy

open-od mounts your OneDrive and handles your Microsoft sign-in, so we take security reports seriously.

## Supported versions

open-od is a proof of concept and has no releases yet. Security fixes land on the `main` branch.

## Reporting a vulnerability

**Please do not open a public issue for security problems.**

Report privately through one of these channels:

1. **GitHub private vulnerability reporting** (preferred): go to the [Security tab](https://github.com/startsecurity/open-od/security) and click **Report a vulnerability**.
2. **Email:** bruno.dantas@startsecurity.com.br, with `[open-od security]` in the subject.

Please include:

- what an attacker can do, and under which conditions;
- steps to reproduce, or a proof of concept;
- the open-od commit, your distribution, and your libfuse and .NET versions.

## What to expect

- We acknowledge new reports within **3 business days**.
- We send an initial assessment within **10 business days**.
- We agree on a disclosure date with you, normally within **90 days** of the report, and earlier once a fix is available.
- We credit reporters in the fix announcement, unless you prefer to stay anonymous.

## Scope

In scope:

- leaking or mishandling OAuth tokens (for example, insecure storage or file permissions);
- reading or downloading files without the user's action, or bypassing the thumbnailer/indexer protection;
- exposing one local user's files or tokens to another user on the same machine;
- crashes or memory-safety issues in the FUSE layer reachable by local programs;
- requesting more Microsoft Graph permissions than needed.

Out of scope:

- vulnerabilities in Microsoft services, libfuse, .NET or other dependencies (please report them upstream; let us know if open-od needs a workaround);
- the public client ID in the source code: desktop apps are public clients, and the ID is not a secret;
- attacks that require root or full control of the user's account.

## Safe harbor

We will not take legal action against good-faith research that follows this policy, avoids privacy violations and data destruction, and only uses accounts and data you own.

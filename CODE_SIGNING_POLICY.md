# Code signing policy

**Status:** releases are not signed yet. We intend to apply to the [SignPath Foundation](https://signpath.org) program,
which signs open-source projects for free. Once accepted, releases will carry this notice:

> Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).

## What gets signed

Only `BatteryStrip-<version>.exe` produced by the [release workflow](.github/workflows/release.yml) from a tagged commit
of this repository. Nothing built on a personal computer is signed.

## Team

| Role | Who |
|---|---|
| Authors, reviewers, approvers | The repository owner |

Every release is approved by the repository owner. All team members use multi-factor authentication for GitHub (and
for SignPath, once used).

## Privacy

The program does not transfer any information to other networked systems unless the user turns on the update check;
see [PRIVACY.md](PRIVACY.md).

## Verifying a release today

Each release lists the SHA-256 of the file. Releases built by the release workflow also have a GitHub build attestation:

```
gh attestation verify BatteryStrip-<version>.exe --repo kantemiir/BatteryStrip
```

The first beta, 0.3.0-beta.1, was built on the maintainer's computer because GitHub Actions did not start for this
account at the time; its release notes say so.

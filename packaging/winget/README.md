# winget manifests

Templates for submitting BatteryStrip to [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs) once a stable
(non-beta) release is out. winget runs the same exe with `--install --quiet --start` and checks the file against `InstallerSha256`, which
usually spares users the SmartScreen prompt.

Before submitting:

1. Replace `OWNER` with the GitHub account and `VERSION` with the release version (without `v`).
2. Put the release file's SHA-256 (from `BatteryStrip-VERSION.sha256.txt`) into `InstallerSha256`.
3. Validate: `winget validate --manifest packaging\winget` and test: `winget install --manifest packaging\winget`.
4. Copy the three files to `manifests/b/BatteryStrip/BatteryStrip/VERSION/` in a fork of winget-pkgs and open a pull request.

`ProductCode: BatteryStrip` matches the app's entry under
`HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\BatteryStrip`, so winget recognises installed copies and upgrades them.

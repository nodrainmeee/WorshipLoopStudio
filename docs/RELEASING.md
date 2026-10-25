# Releasing

1. Update `CHANGELOG.md` and, if you want, the default `<Version>` in `Directory.Build.props`.
2. Commit and push to `main`; wait for the **Build Windows** workflow to be green.
3. Tag and push:
   ```powershell
   git tag v1.0.0
   git push origin v1.0.0
   ```
4. The workflow builds version `1.0.0`, runs the tests, creates `WorshipLoopStudio-1.0.0-win-x64.zip` and publishes a GitHub Release with the ZIP attached.

## Code signing (optional)

Unsigned builds trigger a SmartScreen warning. To sign, store the certificate as a base64 repository secret and add a signing step with `signtool`. Never commit certificates or passwords to the repository.

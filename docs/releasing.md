# Releasing a New Version

## How It Works

When you push a git tag starting with `v` (e.g. `v1.0.0`), GitHub Actions automatically:

1. Runs all tests
2. Builds self-contained binaries for Windows, macOS (ARM + Intel), and Linux
3. Packages each into a downloadable archive
4. Creates a GitHub Release with all four archives attached

Users download from the **Releases** page. The link in the README always points to the latest release.

## Publishing a Release

```bash
git tag v1.0.0
git push origin v1.0.0
```

That's it. The workflow handles the rest. Check progress at **Actions** tab on GitHub.

### Version Numbering

Use [semantic versioning](https://semver.org/): `vMAJOR.MINOR.PATCH`

- **MAJOR** — breaking changes (e.g. settings format changes that lose user config)
- **MINOR** — new features (e.g. new visualization mode, new language)
- **PATCH** — bug fixes, small tweaks

Examples: `v1.0.0`, `v1.1.0`, `v1.1.1`

### Pre-release / Testing

To test the workflow without creating a "real" release, use a pre-release tag:

```bash
git tag v0.9.0-beta1
git push origin v0.9.0-beta1
```

You can delete the release and tag afterward from the GitHub UI if needed.

## What Gets Built

| File | Platform | Contents |
|------|----------|----------|
| `SvalboardLayerViz-X.Y.Z-win-x64.zip` | Windows | Self-contained exe + dependencies |
| `SvalboardLayerViz-X.Y.Z-osx-arm64.zip` | macOS Apple Silicon | `.app` bundle (M1/M2/M3/M4) |
| `SvalboardLayerViz-X.Y.Z-osx-x64.zip` | macOS Intel | `.app` bundle (Intel Macs) |
| `SvalboardLayerViz-X.Y.Z-linux-x64.tar.gz` | Linux | Binary + install.sh + desktop file |

All builds are **self-contained** — users do not need .NET installed.

## Workflow File

The CI/CD pipeline lives at `.github/workflows/release.yml`. It uses:

- `ubuntu-latest` runners for Windows and Linux builds (cross-compilation)
- `macos-latest` runners for macOS builds
- `actions/setup-dotnet@v4` to install .NET 10
- `actions/upload-artifact@v4` / `download-artifact@v4` to pass archives between jobs
- `gh release create` with `--generate-notes` to auto-generate changelog from commits

## Version Embedding

The version from the tag is embedded into the assembly automatically. In the csproj, the default version is `0.0.0-dev` (used during local development). The workflow overrides it:

```
dotnet publish /p:Version=1.0.0
```

## Fixing a Failed Release

If the workflow fails (red in the Actions tab):

1. Check the failed job's logs in GitHub Actions
2. Fix the issue locally, commit, and push
3. Delete the tag and recreate it:
   ```bash
   git tag -d v1.0.0
   git push origin :refs/tags/v1.0.0
   git tag v1.0.0
   git push origin v1.0.0
   ```

## Deleting a Release

From the GitHub web UI: go to **Releases**, click the release, click **Delete**. Optionally delete the tag too.

## Unsigned App Warnings

The builds are not code-signed, so users will see warnings on first launch:

- **Windows**: SmartScreen says "Windows protected your PC" — click **More info** then **Run anyway**
- **macOS**: Gatekeeper says the app "can't be opened" — right-click the app, select **Open**, then click **Open** in the dialog. Or run `xattr -cr SvalboardLayerViz.app` in Terminal.
- **Linux**: No warnings.

These are normal for community-distributed apps without paid signing certificates ($99/year Apple, ~$200+/year Windows EV cert).

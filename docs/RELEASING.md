# Releasing Vizor

## TL;DR

```bash
# 1. Bump the version (ONE place only)
#    Edit <VizorVersion> in Directory.Build.props

# 2. Write the changelog entry
#    Edit CHANGELOG.md

# 3. Commit, tag, push
git commit -am "Release 2.2.0"
git tag v2.2.0
git push origin master --tags
```

That is the whole release. GitHub Actions does the rest.

## What the automation does

Pushing a `v*` tag to `cxiliu/VizorGH` runs `.github/workflows/release.yml`, which:

1. **Checks the tag matches `<VizorVersion>`.** If `v2.2.0` is pushed while
   `Directory.Build.props` still says `2.1.0`, the run fails immediately and
   nothing is published.
2. **Builds** `net48`, `net7.0` and `net7.0-windows` in Release.
3. **Packages** the `.yak` file.
4. **Publishes a source snapshot** to `UniStuttgart-ICD/VizorGH`.
5. **Creates the GitHub release** on the public repo with the `.yak` attached.

A tag push does **not** publish to Food4Rhino. See below.

## Food4Rhino is a separate, deliberate step

A Food4Rhino push is permanent - a published version cannot be pulled back. So
it is never automatic. When you are happy with the release, either:

- run the **Release** workflow manually from the Actions tab with
  **"ALSO push the package to Food4Rhino"** ticked, or
- push by hand: `yak push Release/vizor-<version>/vizor-<version>-rh8_0-any.yak`

## The version number lives in one place

`<VizorVersion>` in `Directory.Build.props`. It feeds:

- `AssemblyVersion` / `FileVersion` on `Vizor.gha` (MSBuild generates these -
  do not add them back to `AssemblyInfo.cs`)
- the `version:` field of the yak manifest, stamped at package time
- the CI tag check

`packaging/manifest.yml` deliberately keeps a `0.0.0` placeholder so a stale
hand-edited number can never ship.

## Running the steps by hand

```powershell
# Build the yak package into Release/vizor-<version>/
pwsh packaging/build-yak.ps1

# Preview what would be published to the public repo (nothing is pushed)
pwsh packaging/publish-public.ps1

# Actually publish it
pwsh packaging/publish-public.ps1 -Push
```

`build-yak.ps1` uses the Rhino 8 `Yak.exe` if Rhino is installed, and downloads
McNeel's standalone `yak.exe` if not. That is why CI needs no Rhino.

## What goes public, and what never does

`packaging/publish-public.ps1` works from an **allowlist** of top-level paths,
not from `.gitignore`. An ignore rule only guards files that are not already
tracked; an allowlist can only publish what it names. On top of that a denylist
blocks `CLAUDE.md`, `.claude/`, `.github/`, `WIP/`, `Archived/`, `Icons/`,
`*.3dm`, `*-plan.md` and `*.log`, and the script aborts if any of them turn up
in the staged snapshot.

The public repo gets **one commit per release**, on top of its existing
history, tagged to match. Internal branch history is never exposed.

To change what is published, edit `$Allow` in `packaging/publish-public.ps1`.

## One-time setup

### Secrets on `cxiliu/VizorGH`

| Secret | What it is | How to get it |
|---|---|---|
| `ICD_PUBLIC_REPO_TOKEN` | Push access to `UniStuttgart-ICD/VizorGH` | A fine-grained PAT scoped to that repo with **Contents: read and write**. |
| `YAK_TOKEN` | Food4Rhino auth | Run `yak login` locally once, then read the token out of the file it writes. |

Add them at *Settings → Secrets and variables → Actions → New repository secret*.

### If pushing a workflow file is ever rejected

Plain `git push` of `.github/workflows/*` works with the current credentials.
If you switch to a token that GitHub rejects for workflow changes, add the
scope:

```bash
gh auth refresh -h github.com -s workflow
```

### Where `yak login` stores its token

`yak login` opens a browser and writes a token to disk. Find it with:

- Windows: `%APPDATA%\McNeel\yak.yml`
- macOS: `~/.mcneel/yak.yml`

Only accounts listed as **owner** of the Vizor package can push new versions.
See the [yak owner docs](https://developer.rhino3d.com/guides/yak/yak-cli-reference/#owner)
to add one.

## Checking a release landed

```bash
yak search vizor          # should list the new version
yak list                  # shows where packages are installed locally
```

## Still manual, on purpose

- Choosing the version number and writing the changelog entry.
- Attaching the HoloLens app build to the release (it lives in another repo).

## References

- [Packaging a Grasshopper plugin](https://developer.rhino3d.com/guides/yak/creating-a-grasshopper-plugin-package/)
- [Pushing a package to the server](https://developer.rhino3d.com/guides/yak/pushing-a-package-to-the-server/)
- [yak CLI reference](https://developer.rhino3d.com/guides/yak/yak-cli-reference/)
- [Manifest file reference](https://developer.rhino3d.com/guides/yak/the-package-manifest/)

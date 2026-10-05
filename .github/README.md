# Releases

The **Build release** workflow creates a VPM ZIP, a UnityPackage, the release
manifest, and SHA-256 checksums. The version and filenames come from `package.json`.

1. Share the existing organization secrets `VPM_MANAGEMENT_GITHUB_APP_ID` and
   `VPM_MANAGEMENT_GITHUB_APP_PRIVATE_KEY` with this repository. The app must have
   Contents write access to `32ba/vpm.32ba.net`.
2. Run **Build release** with **operation: validate** to validate the artifacts and
   VPM connection.
3. Run it from the default branch with **operation: publish** to publish the
   version tag and release assets, then update the existing VPM listing.

The first release adds this repository to the listing source after the ZIP is
published; the listing's existing push workflow then rebuilds it. Later releases
send the existing `update-listing` notification. Do not register a repository
with no published releases in advance.

Use a new version for each release; existing tags are rejected. Prerelease
versions are marked as prereleases automatically. If the release succeeds but
the VPM update fails, use **operation: update-vpm** to retry without publishing
the same version again.

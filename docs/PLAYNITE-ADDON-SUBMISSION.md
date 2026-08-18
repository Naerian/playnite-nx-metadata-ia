# Playnite add-on database submission

The repository contains the two artifacts required for publication:

- `installer.yaml`: versioned package metadata consumed by Playnite.
- `playnite-addon.yaml`: the proposed database entry to copy into `addons/generic/ControllerSessionManager.yaml` in the official Playnite add-on database.

Before submitting a pull request, publish the matching GitHub release, verify that `PackageUrl` downloads the `.pext` directly, and validate both YAML files. The add-on ID must remain identical to `extension.yaml`. Future releases add a package entry at the top of `installer.yaml`; published entries must not be rewritten.

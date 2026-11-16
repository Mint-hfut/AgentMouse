# Code signing policy

## Current status

AgentMouse is preparing an application to SignPath Foundation for open-source code signing. The application has not been approved and the current releases are unsigned.

The proposed service is free code signing provided by [SignPath.io](https://about.signpath.io), with a certificate issued to [SignPath Foundation](https://signpath.org). This describes the requested arrangement and becomes effective only after approval and configuration. A digital signature establishes publisher identity and file integrity. Antivirus detections require their own review.

## Project roles

The repository owner [Mint-hfut](https://github.com/Mint-hfut) is the project's maintainer, reviewer and proposed signing approver. External contributions require maintainer review. Each release submitted for signing will require manual approval by the maintainer. GitHub and SignPath accounts used for signing must have multi-factor authentication enabled before signing is activated.

## Build and artifact scope

Signing requests will originate from verifiable GitHub Actions builds of this public repository. The requested artifacts are AgentMouse's Windows controller, command-line helper and native activation guard. The guard includes unmodified MinHook source under its upstream license. The Full edition also bundles Windows-MCP and a Python runtime. Upstream executable files and libraries retain their original provenance and will not be signed as AgentMouse's own binaries.

Before signed releases are enabled, the signing configuration must enforce AgentMouse product metadata and a consistent product version for the build. Signing credentials and private keys must remain outside the source repository. The proposed SignPath service manages the signing key.

## Runtime behavior

AgentMouse performs computer use requested by an operator through their chosen agent client. Native background operation uses Windows UI-thread hooks and a protection DLL loaded into the target application. These mechanisms preserve independent pointer queries and protect foreground and focus behavior during bounded input operations. They are disclosed in the architecture and compatibility documentation.

Qianxin has detected the unsigned CLI executable as `Trojan/MSIL.Win32.Inject.06f`. This detection is disclosed for the signing review. Vendor adjudication is pending. Code signing approval must not be represented as an antivirus clearance.

## Privacy and removal

The [privacy policy](privacy-policy.md) describes screenshots, tool results, local browser pairing and network activity. Native plugins can be removed through the selected agent host's plugin manager. Installation backups and removal instructions are documented in the [README](../README.md#configuration-and-removal).


# Privacy policy

Updated November 16, 2026.

## Operator-directed computer use

AgentMouse is a local Windows automation service connected to an operator's chosen MCP client. Requested screenshots, window and process metadata, accessibility information, clipboard contents and system-tool results are returned to that client. These results can contain private information. If the chosen client uses a cloud model or stores conversation logs, its provider's privacy policy and the operator's configuration govern that processing.

Windows input activity is monitored locally to coordinate with the operator. AgentMouse uses input timing and pointer activity for this purpose. It does not provide an independent keyboard-input tool or store typed key sequences in its input tracker.

## Network activity

AgentMouse does not operate a separate telemetry or advertising collection service. The Full adapter disables the upstream Windows-MCP analytics instance and sets `ANONYMIZED_TELEMETRY=false`. This policy describes AgentMouse's adapter, rather than independent use of the upstream application.

The Browser Bridge exchanges page images and input commands through an authenticated loopback service. Browser actions and explicitly requested HTTP scraping can contact websites selected by the operator. Those websites apply their own privacy policies. Installation and source-build workflows can contact GitHub, package registries and runtime distributors to obtain requested software and dependencies.

## Local storage

AgentMouse installation writes runtime files, selected host configuration, skills and configuration backups. Browser pairing stores a local authentication token under the user's AgentMouse directory. Captures are returned to the requesting client, which may retain them. Development and acceptance logs on a maintainer's machine are separate from the distributed runtime.

Repository test reports exclude private account identifiers, mail contents and personal screenshots. Users should review tool output and client logging settings before using the service with sensitive applications.

## Signing and removal

If a signing application is submitted, the maintainer's application contact details are processed by SignPath under [its privacy policy](https://signpath.io/privacy-policy). Application contact details are not added to this repository.

Remove native plugins through the host's plugin manager. For MCP-only installations, remove the AgentMouse server registration and AgentMouse skills from the chosen host before deleting the runtime. Retain unrelated host settings. Stop the Browser Bridge and remove its browser extension before deleting its runtime and pairing files. See the [configuration and removal instructions](../README.md#configuration-and-removal). Requests concerning project policy can be raised through the repository's issues without posting confidential data.


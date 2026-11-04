# Third-party software

AgentMouse Full includes `vendor/windows-mcp`: Windows-MCP 0.8.8, copied from the user-provided local source on 2026-11-04. AgentMouse Core does not bundle Windows-MCP or Python. Upstream: https://github.com/CursorTouch/Windows-MCP.

Copyright (c) 2025 JEOMON GEORGE. Distributed under the MIT license, preserved in `vendor/windows-mcp/LICENSE.md`. The AgentMouse adapter is separate; the vendored upstream source is unchanged. AgentMouse's Apache-2.0 license does not replace the upstream license.

Full's CPython license is preserved at `python-runtime/LICENSE.txt`. Python dependencies are resolved from the preserved upstream `uv.lock`; their license files remain in the bundled site-packages. Shared Node dependencies retain their licenses in node_modules.

# Python add-in provenance

The files in `Fusion360MCP/` were copied from the `addon/` directory of
https://github.com/faust-machines/fusion360-mcp-server at commit
`19b9f1e9519235ae4a0f5e8dc6b029c08b87ff5f` (MIT license; see
`LICENSE.upstream`). The copy was made from Git `HEAD`, excluding local
uncommitted changes in the development reference clone.

Project-specific changes after import are maintained in this repository.
The upstream `execute_code` dispatch and implementation were removed from
this package: the C# server exposes only the 92 typed tools, and the raw TCP
listener must not accept an undocumented code-execution command.
To update, review the upstream diff from the commit above, copy only the
intended tracked files, rerun Python and C# contract tests, and update this
commit ID. Installation and release builds must use this checked-in copy;
they do not clone upstream at runtime.

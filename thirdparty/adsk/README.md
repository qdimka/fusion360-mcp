# thirdparty/adsk

adsk .NET API assemblies for the Fusion 360 AddIn (ADR-0003).

Expected contents (copied from a **Windows** Fusion 360 install, e.g.
`C:\Program Files\Autodesk\Autodesk Fusion 360\api\`):

- `adsknet.dll`
- `adsknetcore.dll`
- `adsknetconnector.dll`
- `adsknetwrapper.dll`
- (other adsk*.dll shipped with the API package)

They are committed to the repo on purpose (small files, adsk .NET API is
MIT-licensed) so CI can compile the AddIn without a Fusion install.

## Status

`adsknet.dll` here is a **compile-time stub** (source: `stubs/AdskCoreStubs.cs`,
compiled to `stubs/adsknet.csproj`'s output and copied to this directory). It
provides the documented C# adsk surface used by the add-in; every member
throws `NotSupportedException` at runtime. It is excluded from the add-in
build output (`CopyLocal=false`) and never ships into the AddIns folder — at
runtime the add-in resolves the real adsknet in-process from the Fusion
runtime. When the genuine adsk assemblies are dropped into this directory
(overwriting the stub), the AddIn compiles against them unchanged.

The development machine is macOS:
`~/Library/Application Support/Autodesk/Autodesk Fusion 360/API/` contains
only Python bindings (`Python/defs/adsk`), C++ headers/libs and scripts —
**no adsk .NET assemblies** (the .NET add-in stack ships with Windows Fusion).

Consequences until the DLLs are dropped into this directory:

- `src/Fusion360.Mcp.AddIn` builds without adsk: its csproj references
  `thirdparty/adsk/*.dll` through a glob, which is a no-op when the files
  are absent.
- `tests/Fusion360.Mcp.AddIn.Tests` and the CI `addin` job run only when
  `*.dll` are present here.
- The AddIn code (T3+) carries a TODO pointing at this directory.

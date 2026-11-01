# fusion360-mcp MCP server (stdio) — multi-stage image.
#
# Build:  mcr SDK 10 (only the net10 MCP server is built; the net48 Add-in
#         and the adsk stub DLL are NOT part of this image).
# Runtime: mcr aspnet 10. The server is a stdio process — no inbound ports;
#         it talks to the Fusion 360 add-in OUTBOUND via FUSION_MCP_HOST:PORT
#         (default 127.0.0.1:9876; on Docker Desktop use host.docker.internal).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/Fusion360.Mcp.Core/Fusion360.Mcp.Core.csproj src/Fusion360.Mcp.Core/
COPY src/Fusion360.Mcp.Server/Fusion360.Mcp.Server.csproj src/Fusion360.Mcp.Server/
RUN dotnet restore src/Fusion360.Mcp.Server
COPY src/Fusion360.Mcp.Core src/Fusion360.Mcp.Core
COPY src/Fusion360.Mcp.Server src/Fusion360.Mcp.Server
RUN dotnet publish -c Release src/Fusion360.Mcp.Server -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish /app
# stdin/stdout carry the JSON-RPC stream; the .NET console respects
# Console.OpenStandardInput() through the container's -i flag.
ENTRYPOINT ["dotnet", "/app/Fusion360.Mcp.Server.dll"]

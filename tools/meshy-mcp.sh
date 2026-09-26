#!/bin/zsh
# Launches the Meshy MCP server with the key from ../.env (keeps the key out of .mcp.json and git).
set -a; source "${0:A:h}/../.env"; set +a
exec npx -y @meshy-ai/meshy-mcp-server

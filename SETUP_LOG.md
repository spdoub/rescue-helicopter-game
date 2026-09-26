# Setup log

- 2026-09-25: macOS 26.6.2 (arm64). Already had: Homebrew, git 2.52, Node 22, Python 3.14, gh, Claude Code. 53 GB free.
- 2026-09-25: Installed git-lfs, Unity Hub, Blender 5.2.2 (brew). Unity 6000.3.25f1 LTS (arm64) + Windows Mono + Mac IL2CPP build support.
- Blender MCP add-on v1.0.3 installed, autostart on (port 9876); server in tools/blender-mcp. Unity MCP server cloned to tools/unity-mcp-server. Meshy MCP via tools/meshy-mcp.sh (reads .env). All registered in .mcp.json.
- Note: don't sign in to Unity Hub while a headless Hub install is running — the login callback goes to the hidden instance.

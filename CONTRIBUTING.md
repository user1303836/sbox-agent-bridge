# Contributing

Thanks for helping make agent-assisted s&box development less haunted by guesswork.

## Development Priorities

This project values:

- small, explicit editor actions over broad unsafe escape hatches;
- live editor read-back after every mutation;
- docs and capability matrix updates alongside code;
- s&box API verification before using unfamiliar editor/game APIs;
- narrow MCP tool surfaces with clear action enums.

## Local Checks

```bash
cd mcp-server
npm ci
npm run check
npm run build
```

The GitHub Actions workflow runs these checks plus JSON metadata validation.

## Live Editor Checks

Changes to `editor/` should be smoke-tested in a real s&box project when possible:

1. Copy `editor/` into `YourProject/Libraries/sbox_agent_bridge`.
2. Open the project in s&box.
3. Let the bridge library compile; optionally open the **Agent Bridge** dock to view status.
4. Run a direct bridge request or MCP tool.
5. Confirm mutations are visible in the scene and can be undone.

Record the result in [docs/capability-matrix.md](docs/capability-matrix.md).

## Pull Requests

For new bridge capabilities, include:

- the editor handler implementation;
- the MCP tool mapping;
- an entry in the capability matrix;
- a live verification note or a clear reason it could not be verified;
- docs updates when behavior or setup changes.

## License

By contributing, you agree that your contributions are licensed under the MIT License.

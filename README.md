# Husnain Unity AI

AI-powered Unity Editor assistant. Chat with Claude directly inside Unity.

## Status

**v0.1 — Phase A:** non-streaming chat. No tools yet. The agent can talk, but not yet read or modify your project.

Roadmap:

- **Phase B:** Tool loop with `read_file`, `write_file`, `edit`, `list`, `glob`, `grep`, `compile_check`. Claude can read your project, write C# scripts, edit scenes/materials/prefabs/shaders via direct file manipulation, and verify it compiles.
- **Phase C:** Streaming responses (token-by-token rendering in the editor window).
- **Phase D:** Image and PDF attachments.

## Requirements

- Unity 2022.3 or later
- Anthropic API key from <https://console.anthropic.com>
- API credit balance (per-token billing — separate from any Claude Max / Claude Code subscription)

## Install

Add to your project's `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.husnain.unity-ai": "file:/path/to/com.husnain.unity-ai"
  }
}
```

Or via the Unity Package Manager → "Add package from git URL".

## Use

1. **Window → Husnain AI** to open the chat panel
2. Click **Set API Key** and paste your `sk-ant-api03-...` key (stored in `EditorPrefs`)
3. Type a message, hit Send

The default model is `claude-opus-4-7`. Change it from the settings header.

## Notes

The API key is stored in `EditorPrefs` on your machine. It is sent only to `api.anthropic.com`. This is a personal-use tool; don't commit your key, don't ship the package with a key embedded.

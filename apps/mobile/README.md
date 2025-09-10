# Mobile (Expo)

Scripts:

- pnpm dev: Starts Expo Dev Server (port 8082). Terminal remains interactive.
- pnpm dev:ios: Starts Expo Dev Server (port 8082) and opens the iOS simulator.

Usage:

- Start normally:

```
pnpm dev
```

- Start iOS (recommended via workspace filter from repo root):

```
pnpm -F @appostolic/mobile run dev:ios
```

- Run on a physical device:
  - Open Expo Go and scan the QR code from Dev Tools.

Tips:

- Clear cache if needed:

```
expo start -c --port 8082
```

- If pressing `i` doesn’t open simulator, ensure Xcode Command Line Tools are installed.
- Keep mobile in its own terminal to interact with Expo (QR codes, simulator commands).
- If Expo Go version mismatches the SDK, upgrade the SDK and align deps:

```
npx expo upgrade 54
npx expo install --fix
```

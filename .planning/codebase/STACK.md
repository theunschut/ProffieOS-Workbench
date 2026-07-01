# Technology Stack

**Analysis Date:** 2026-07-01

## Languages

**Primary:**
- C# (.NET 10.0) - Blazor WebAssembly application (`ProffieOS.Workbench/`)
- JavaScript (ES6+) - WebUSB and WebBLE interop layer (`wwwroot/js/*.js`)
- HTML/CSS - UI markup and styling

**Secondary:**
- JavaScript (vanilla ES5/ES6) - Legacy application (`old/app.html`), Service Worker implementations

## Runtime

**Environment:**
- .NET 10.0 (Blazor WebAssembly)
- Browser runtime (modern browsers with WebUSB/WebBLE support)

**Package Manager:**
- NuGet - .NET package management
- npm - Not used (no npm dependencies in new app)

## Frameworks

**Core:**
- Microsoft.AspNetCore.Components.WebAssembly 10.0.4 - Blazor WASM framework for client-side .NET
- MudBlazor 9.1.0 - Material Design component library for Blazor
- Microsoft.NET.Sdk.BlazorWebAssembly - Build SDK for WASM compilation

**Testing:**
- None detected in current codebase

**Build/Dev:**
- Microsoft.AspNetCore.Components.WebAssembly.DevServer 10.0.4 - Development server
- .NET CLI (dotnet publish, dotnet build)
- GitHub Actions - CI/CD pipeline

## Key Dependencies

**Critical:**
- MudBlazor 9.1.0 - Provides Material Design UI components, icons, and styling
- Microsoft.AspNetCore.Components.WebAssembly 10.0.4 - Core Blazor WASM runtime and component system

**Hardware Communication:**
- No external packages - WebUSB and WebBLE are native browser APIs used via JavaScript interop
  - `window.UsbInterop` - Custom JS wrapper around WebUSB API (`wwwroot/js/usb.js`)
  - `window.BluetoothInterop` - Custom JS wrapper around WebBLE API (`wwwroot/js/bluetooth.js`)
  - Called from C# via `IJSRuntime` (`Microsoft.JSInterop`)

**Styling:**
- Google Fonts (Roboto) - Loaded via CDN at runtime

## Configuration

**Environment:**
- Base href configured dynamically for GitHub Pages deployment (sed injection in CI/CD)
- LocalStorage flag for Service Worker enablement: `pwa-sw-enabled`
- No .env file usage detected

**Build:**
- `ProffieOS.Workbench.csproj` - .NET project file with build configuration
- `ProffieOS.Workbench.slnx` - .NET solution file
- Publish output: Release mode, produces WASM + JavaScript bundles

## Platform Requirements

**Development:**
- .NET SDK 10.0.x
- Any text editor (VS Code, Visual Studio recommended)
- Git for version control

**Production (Deployment):**
- GitHub Pages (deployed via GitHub Actions)
- Modern browser with WebUSB API support (Chrome/Edge/Opera 61+)
- Modern browser with WebBLE API support (Chrome 56+, Edge 79+, Opera 43+, Android Chrome)
- HTTPS required for WebUSB/WebBLE access

**Runtime Constraints:**
- Single-threaded event loop (browser/WASM)
- Service Worker optional (PWA support disabled by default, opt-in via localStorage)

---

## Legacy Stack (Reference - `/old/`)

**Old Application:**
- Vanilla JavaScript (HTML5 + CSS3)
- Service Worker API for offline caching
- Direct WebUSB and WebBLE API usage (embedded in HTML)
- No build process (static files)
- Deployed as standalone static assets

**Key Libraries (Legacy):**
- None - pure vanilla JS, no npm dependencies

*Stack analysis: 2026-07-01*

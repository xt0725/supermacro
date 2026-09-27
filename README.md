# SuperMacroV1

Windows auto clicker by XT.07. The interface is available in English (default), French and Spanish. The language choice is saved with your settings.

## Features

- Adjustable click speed from 1 to 100 clicks per second
- Left, right or middle mouse button
- Customizable toggle key with optional Ctrl, Alt and Shift modifiers
- Optional start on launch
- **Filter clients** option to click only while a recognized client has focus
- Global F12 emergency stop
- Persistent settings

The **Filter clients** checkbox can be left off to use the auto clicker with any foreground application. The shortcut always toggles clicking on or off; there is no activation mode selector.

## Download

Open the repository's **Actions** tab, select **Build Windows Installer**, and download the `SuperMacroV1-Installer` artifact from the latest successful run.

## Build locally

Requires .NET 8 SDK and Inno Setup 6. The supplied icon is already included in the project.

```powershell
dotnet publish src/SuperMacroV1/SuperMacroV1.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist/app
& "$env:ProgramFiles(x86)\Inno Setup 6\ISCC.exe" installer/SuperMacroV1.iss
```

SuperMacroV1 sends mouse clicks through the standard Windows `SendInput` API.

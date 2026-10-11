# Microsoft Store submission

Packaging is `build-msstore-package.ps1` plus `PharmaBill.App/Package.appxmanifest`. That path does not run `build-installer.ps1` or Inno Setup.

## Build

```powershell
.\build-msstore-package.ps1 -Publisher "CN=<paste from Partner Center>"
```

If Partner Center reserved a package name other than `SAER.PharmaBill`:

```powershell
.\build-msstore-package.ps1 `
  -Publisher "CN=<paste from Partner Center>" `
  -PackageName "<Package/Identity/Name>"
```

Output in `artifacts\msstore\`:

- `PharmaBill_<version>_x64.msix`
- `PharmaBill_<version>_x64.msixupload` — upload this file

The script publishes Release `win-x64`, self-contained, with `-p:MasterAdminBuild=false`, and stops if `MASTER_ADMIN_BUILD` is present. Package version is `Major.Minor.Build.0` (default: csproj `<Version>` plus `.0`). Leave the package unsigned; Partner Center signs it. `-CertPath` is only for local sideload, and the PFX subject must equal `-Publisher`.

## Copy these from Partner Center

Open the app in Partner Center → Product identity. The manifest already has the name and publisher display name. Only the publisher CN is still a placeholder (`CN=REPLACE-WITH-PARTNER-CENTER-CN`).

| Partner Center field | Where it goes | Value in this branch |
|---|---|---|
| Package/Identity/Publisher | `-Publisher` (manifest `Identity@Publisher`) | `CN=REPLACE-WITH-PARTNER-CENTER-CN` until you paste the live CN |
| Package/Identity/Name | `-PackageName` (manifest `Identity@Name`) | `SAER.PharmaBill` |
| Package/Properties/PublisherDisplayName | manifest `PublisherDisplayName` | `S.A.E.R.` (as shown in Partner Center; must match exactly) |
| Reserved product name | manifest `DisplayName` | `PharmaBill` |

The publisher string must match exactly, including the `CN=` prefix. A mismatch is rejected at upload.

## Package contract

- Device family: `Windows.Desktop`, MinVersion `10.0.17763.0`, MaxVersionTested `10.0.26100.0`
- Launches `PharmaBill.App.exe` (`EntryPoint` `Windows.FullTrustApplication`)
- `runFullTrust` so the desktop process is not in an AppContainer (hardware ID extraction, WMI, local files)
- `unvirtualizedResources` with filesystem write virtualization disabled, and a Windows 11 exclusion for `$(KnownFolder:LocalAppData)\PharmaBill`, so SQLite stays on the real `%LocalAppData%\PharmaBill` path

## Tiles

In `PharmaBill.App\Assets\Store\`, referenced by the manifest as `Assets\Store\...`:

| File | Size |
|---|---|
| Square44x44Logo.png | 44×44 |
| Square150x150Logo.png | 150×150 |
| StoreLogo.png | 50×50 |
| Wide310x150Logo.png | 310×150 |
| SplashScreen.png | 620×300 |

## Submission checklist

- [ ] `-Publisher` pasted from Product identity; package name is `SAER.PharmaBill` unless Partner Center shows a different name
- [ ] Pricing and markets
- [ ] Age rating (IARC) and category (Business or Medical)
- [ ] System requirements: Windows 10 version 1809 (build 17763) or later, x64
- [ ] Privacy policy URL (pharmacy customer data and a hardware-based machine ID)
- [ ] Support contact and website
- [ ] Description, what's new, keywords, copyright
- [ ] At least one screenshot, 1366×768 or larger PNG
- [ ] Store listing logo 300×300 PNG
- [ ] Certification notes: offline-first WPF app; `runFullTrust` and `unvirtualizedResources` (SQLite under `%LocalAppData%\PharmaBill`, hardware ID); first-run test login if required; how the trial and licence work
- [ ] Upload the unsigned `.msixupload`

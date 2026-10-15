# PharmaBill — Local Pharmacy Installation & Deployment Guide

## Overview
This guide provides step-by-step instructions for IT administrators and pharmacy staff to deploy PharmaBill as a standalone, offline-ready Windows application to local pharmacies.

## Key Features

✅ **Fully Self-Contained**
- No .NET runtime pre-installation required
- All dependencies bundled (SQLite, printer drivers, libraries)
- Works on Windows 10/11 without external downloads

✅ **Offline-Ready**
- Complete operation without internet access
- Local SQLite database with encryption (SQLCipher)
- Optional Wi-Fi sync with Android Mobile Bridge (local network only)

✅ **Secure & Privacy-Focused**
- Encrypted local database
- No cloud uploads (patient data stays on-premises)
- Antivirus settings are never modified

✅ **Pharmacy-Friendly**
- Thermal printer support (ESC/POS protocol)
- Automatic backup to local folders
- Simple 3-step installation

---

## Deployment Options

### Option 1: Direct Installation (Recommended for 1–10 Pharmacies)

**Requirements:**
- Windows 10 or later (x64)
- 500 MB free disk space
- Administrator access to open firewall

**Steps:**

1. **Copy Installer**
   ```
   Distribution File: artifacts/installer/PharmaBill_Setup.exe
   Target Folder: USB Drive or Network Share
   ```

2. **Deliver to Pharmacy**
   - Provide the `.exe` file and `README_PHARMACY_INSTALL.txt`
   - Include pharmacy contact info for support

3. **On-Site Installation**
   - Pharmacy staff runs: `PharmaBill_Setup.exe`
   - Follows the 3-step wizard (Next → Install → Finish)
   - Automatic firewall rule opens for Wi-Fi sync (port 5184)

4. **Verification**
   - Check Desktop icon or Start Menu entry
   - Launch PharmaBill
   - Confirm pharmacy data imports successfully

---

### Option 2: Network/Group Policy Deployment (For 10+ Pharmacies or IT-Managed)

**Requirements:**
- Active Directory or Group Policy Object (GPO) management
- Shared network folder or WSUS deployment

**Steps:**

1. **Publish to Network Share**
   ```powershell
   # Copy installer to a shared network location
   Copy-Item "artifacts\installer\PharmaBill_Setup.exe" `
             "\\server\pharmacy-deployments\PharmaBill_Setup.exe"
   ```

2. **Create GPO Deployment**
   - Use GPMC (Group Policy Management Console)
   - Assign application to: Computer Configuration → Software Installation
   - Link to OU containing pharmacy workstations

3. **Firewall Configuration**
   - Deploy Group Policy for Windows Firewall rules (see below)
   - Allow inbound TCP port 5184 for Wi-Fi sync

4. **Monitoring**
   - Use WSUS or SCCM for deployment tracking
   - Monitor installation logs in: `C:\Windows\Logs\SoftwareCenter`

---

### Option 3: Bulk USB Distribution (For Offline/Remote Pharmacies)

**Requirements:**
- USB drives (8GB+ recommended for batch copying)
- `README_PHARMACY_INSTALL.txt` printed or PDF on USB

**Steps:**

1. **Prepare USB Media**
   ```powershell
   # Copy to each USB drive
   robocopy "artifacts\installer" "D:\PharmaBill" /MIR
   robocopy "." "D:\PharmaBill\Docs" README_PHARMACY_INSTALL.txt
   ```

2. **Label Each USB**
   - "PharmaBill v1.0.1 — Pharmacy Installer"
   - Include date of distribution

3. **Deliver to Pharmacy**
   - Include printed installation guide
   - Provide IT contact number for support

4. **On-Site Installation**
   - Plug USB into laptop
   - Run `PharmaBill_Setup.exe` from USB
   - Installation copies to local `Program Files`

---

## Post-Installation Configuration

### 1. Printer Setup (If Applicable)
```
In PharmaBill:
1. Settings → Printer Configuration
2. Select USB Thermal Printer from list
3. Test print to verify connection
```

### 2. Wi-Fi Sync (Android Mobile Bridge)
```
1. Settings → Wi-Fi Sync
2. Note the IP address (e.g., 192.168.1.100)
3. On Android app, enter this IP + Port 5184
4. Both devices must be on same local Wi-Fi
```

### 3. Database Backup
```
1. Settings → Backup & Restore
2. Create manual backup before opening for production
3. Store backups on external drive or network share
```

### 4. Medicine Catalog Import
```
1. Settings → Medicine Catalog
2. If needed, upload CSV with your medicine list
3. Default catalog already includes common medicines
```

---

## Firewall Configuration

The installer runs per-user without elevation and does **not** change firewall or antivirus settings.
On first launch, when PharmaBill starts listening on TCP 5184, Windows shows its native
"Allow PharmaBill to communicate on these networks" prompt. Tick **Private networks** and click **Allow**
(Windows asks for admin approval at that point). IT can pre-create the rule with
`Deploy-PharmaBill.ps1 -ConfigureFirewall` run as Administrator.

### Manual Verification (If Needed)
```powershell
# Verify firewall rule exists
Get-NetFirewallRule -DisplayName "PharmaBill*" | Format-List Name, Enabled

# Re-add if missing
netsh advfirewall firewall add rule `
  name="PharmaBill Wi-Fi Sync" `
  dir=in action=allow protocol=TCP localport=5184 `
  program="C:\Users\<user>\AppData\Local\Programs\PharmaBill\PharmaBill.App.exe"
```

---

## User Data & Backup Locations

All pharmacy data is stored in:
```
C:\Users\<username>\AppData\Local\PharmaBill\
```

Contents:
- `PharmaBill.db` — Encrypted SQLite database
- `backups/` — Daily automatic backups
- `logs/` — Application logs (troubleshooting)
- `attachments/` — Any uploaded documents

**⚠️ IMPORTANT:** Uninstalling PharmaBill does NOT delete this folder. User data is preserved.

---

## Troubleshooting

### Issue: "Windows SmartScreen" Blocking Installer
**Solution:**
1. Click "More info" on warning
2. Click "Run anyway"

*Note: For code-signed installers, contact system administrator.*

### Issue: Wi-Fi Sync Port 5184 Not Working
**Solution:**
1. Check firewall rule: `netsh advfirewall firewall show rule name="PharmaBill*"`
2. Verify both devices on same network
3. Re-add rule if missing (see Firewall Configuration above)

### Issue: Printer Not Detected
**Solution:**
1. Ensure USB thermal printer is connected
2. In PharmaBill: Settings → Printer Configuration → Refresh
3. Verify USB drivers are installed on Windows

### Issue: Data Missing After Uninstall
**All data is preserved** in `%LocalAppData%\PharmaBill\`
1. Reinstall PharmaBill_Setup.exe
2. On restart, all previous data and bills will be there

---

## Security Considerations

✅ **Data Protection**
- Local SQLite database encrypted with SQLCipher
- No data uploaded to cloud
- Patient/prescription data stays on-premises

✅ **Network Isolation**
- Wi-Fi sync uses local network only (no internet required)
- Port 5184 firewall rule restricted to TCP (no UDP)

✅ **Backup Strategy**
- Automatic daily backups stored locally
- Manual backups via Settings → Backup & Restore
- Store offsite backups on external drives

⚠️ **Considerations for IT Administrators**
- Monitor `%LocalAppData%\PharmaBill\logs` for errors
- Ensure regular backups (suggest external drive or NAS)
- Test disaster recovery by restoring from backup quarterly

---

## Version Updates

### Updating to a Newer Version

1. **Backup Current Data** (Always)
   - Settings → Backup & Restore → Create Backup
   - Save backup file to external location

2. **Uninstall Current Version**
   - Control Panel → Programs → Uninstall PharmaBill
   - Click "Yes" (your data will remain intact)

3. **Install New Version**
   - Run new `PharmaBill_Setup.exe`
   - Complete the wizard
   - Your data automatically loads on first launch

---

## Support & Contact

**For Installation Issues:**
- Contact: support@pharma-bill.local
- Include: Backup file from `%LocalAppData%\PharmaBill\backups`
- Include: Logs from `%LocalAppData%\PharmaBill\logs`

**For Deployment at Scale:**
- IT Admin Contact: [IT Support Number]
- Network/WSUS Configuration: [IT Email]

---

**Document Version:** 1.0  
**Last Updated:** 2026-10-14  
**PharmaBill Version:** 1.0.1  

================================================================================
                    PHARMA BILL — INSTALLATION GUIDE
                    For Offline Windows Pharmacy Setup
================================================================================

OVERVIEW
--------
PharmaBill is a standalone pharmacy billing and inventory management system
designed for local pharmacies. This package is self-contained and does NOT
require internet access to run.

SYSTEM REQUIREMENTS
-------------------
• Windows 10 or later (Home, Pro, or Enterprise)
• x64 processor (Intel or AMD)
• At least 2 GB RAM
• 500 MB free disk space
• Administrator or standard user account (elevated privileges required to
  open firewall for Wi-Fi sync)

WHAT'S INCLUDED
---------------
✓ PharmaBill application (self-contained, no .NET runtime needed)
✓ Embedded SQLite database (local, encrypted)
✓ Thermal printer support (ESC/POS compatible)
✓ Wi-Fi sync support for Android Mobile Bridge (local network, port 5184)
✓ All dependencies bundled (no additional downloads required)

================================================================================
3-STEP INSTALLATION PROCESS
================================================================================

STEP 1: DOWNLOAD & EXTRACT (2 minutes)
--------------------------------------
1. Save the PharmaBill_Setup.exe file to your Windows laptop
2. You do NOT need to extract it — the installer handles everything
3. Close any running pharmacy applications before proceeding


STEP 2: RUN THE INSTALLER (3 minutes)
--------------------------------------
1. Double-click PharmaBill_Setup.exe
2. A setup wizard will appear. Follow these steps:

   a. Click "Next" on the Welcome screen

   b. Review the installation location:
      - Default: C:\Users\<YourName>\AppData\Local\Programs\PharmaBill
      - You can change this if needed, or keep the default

   c. Optional: Check "Create a desktop icon" to add a shortcut to your
      desktop (recommended for quick access)

   d. Click "Next" and then "Install"

   e. Wait for the installation to complete (this may take 30 seconds)

   f. CHECK THE CHECKBOX: "PharmaBill" to launch the app immediately

   g. Click "Finish"


STEP 3: FIRST-TIME LAUNCH & CONFIGURATION (5 minutes)
------------------------------------------------------
1. PharmaBill will launch automatically or when you click the desktop icon
2. On first run, you will:

   a. See the login/welcome screen

   b. Set up your pharmacy profile:
      - Pharmacy Name
      - Owner Name
      - License Number (if applicable)
      - Contact Information

   c. Configure your thermal printer (if you have one):
      - Go to: Settings → Printer Configuration
      - Select your USB thermal printer from the list
      - Test print to verify connection
      - If you don't have a printer, you can print bills as PDFs instead

   d. Import your initial medicine catalog (optional):
      - PharmaBill comes with a default catalog
      - You can upload your own CSV list via: Settings → Medicine Catalog

   e. That's it! You're ready to start using PharmaBill


AFTER INSTALLATION
-------------------

Wi-Fi Sync (Android Mobile Bridge)
~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
If you want to use the PharmaBill Android app to scan bills and sync with
this PC:

1. Install the PharmaBill Android app on your mobile device from:
   [Android App Store Link]

2. On this PC, go to: Settings → Wi-Fi Sync
   - You will see your PC's IP address (e.g., 192.168.1.100)
   - Port: 5184

3. Enter this IP address in the Android app's Wi-Fi sync settings

4. Both devices must be on the same Wi-Fi network

5. The first time PharmaBill opens, Windows asks "Allow PharmaBill to communicate
   on these networks?" - tick Private networks and click Allow


Database Backups
~~~~~~~~~~~~~~~~
Your pharmacy data is stored locally in:
  C:\Users\<YourName>\AppData\Local\PharmaBill\

This folder contains:
  • PharmaBill.db (your encrypted database)
  • Backup files (automatic daily backups)
  • Logs (troubleshooting information)

To back up your data:
1. Go to: Settings → Backup & Restore
2. Click "Create Backup" to manually save a backup file
3. Keep backup files in a safe location (external drive, cloud, etc.)


TROUBLESHOOTING
---------------

Issue: "Windows SmartScreen" warning when running installer
--------
Solution:
  1. Click "More info" on the warning dialog
  2. Click "Run anyway"
  This is normal for unsigned third-party software. If you require a
  code-signed installer, contact your system administrator.

Issue: Printer not detected after installation
--------
Solution:
  1. Ensure your thermal printer is connected via USB
  2. In PharmaBill: Settings → Printer Configuration
  3. Click "Refresh" to rescan for connected printers
  4. If still not found, restart PharmaBill and try again

Issue: "Access Denied" error during installation
--------
Solution:
  1. Right-click PharmaBill_Setup.exe
  2. Select "Run as Administrator"
  3. Click "Yes" when prompted by User Account Control

Issue: Wi-Fi sync not working
--------
Solution:
  1. Verify both this PC and mobile device are on the same network
  2. On this PC, go to: Settings → Wi-Fi Sync
  3. Note the IP address displayed
  4. Ensure Windows Firewall is not blocking port 5184:
     - Control Panel → Windows Defender Firewall → Allow an app through firewall
     - Look for "PharmaBill" or "PharmaBill.App.exe" and ensure it's allowed

Issue: App crashes or data loss
--------
Solution:
  1. Your data is safely stored in %LocalAppData%\PharmaBill (never deleted)
  2. Create a backup before uninstalling: Settings → Backup & Restore
  3. Contact support with your backup file and logs from:
     C:\Users\<YourName>\AppData\Local\PharmaBill\Logs\


UNINSTALLATION
---------------
If you need to remove PharmaBill:

1. Go to: Control Panel → Programs → Programs and Features
2. Find "PharmaBill" in the list
3. Click it and select "Uninstall"
4. Click "Yes" to confirm

⚠️  IMPORTANT: Your pharmacy data (database, backups, bills, inventory) will
    NOT be deleted. They remain in C:\Users\<YourName>\AppData\Local\PharmaBill\
    even after uninstall. You can re-install PharmaBill and your data will be
    there waiting for you.


OFFLINE OPERATION
------------------
PharmaBill works completely offline. You do NOT need:
  ✗ Internet connection
  ✗ Pre-installed .NET Framework
  ✗ Microsoft Store
  ✗ Any external servers
  ✗ Cloud account or login

Everything is bundled in the installer and runs locally on your laptop.

To enable Wi-Fi sync with the Android app:
  ✓ Both devices on the same local Wi-Fi network
  ✓ No internet required
  ✓ Allow PharmaBill when Windows Firewall asks on first launch


CONTACT & SUPPORT
------------------
If you have questions or need help:
  Email: support@pharma-bill.local
  Phone: [Pharmacy Support Number]
  Visit: [Support Portal]

Always include your backup file and logs when reporting issues.

================================================================================
Version: 1.0.1 (Release)
Last Updated: 2026-10-14
================================================================================

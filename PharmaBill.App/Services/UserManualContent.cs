using System.Collections.Generic;

namespace PharmaBill.App.Services;

public static class UserManualContent
{
	public static IReadOnlyList<UserManualChapter> Chapters { get; } = new _003C_003Ez__ReadOnlyArray<UserManualChapter>(new UserManualChapter[6]
	{
		new UserManualChapter("quick-start", "Quick Start & Workstation Setup", "setup first run licence license initial pharmacy email mode retail wholesale production", new _003C_003Ez__ReadOnlyArray<GuideBlock>(new GuideBlock[11]
		{
			new GuideBlock(GuideBlockKind.Heading, "Welcome to PharmaBill"),
			new GuideBlock(GuideBlockKind.Paragraph, "PharmaBill is an offline-first pharmacy billing system for India (Retail Chemist, Wholesaler, or Both). Complete Initial Setup once, then use Settings to refine firm profile, UPI, printers, and sync."),
			new GuideBlock(GuideBlockKind.Heading, "First-run checklist"),
			new GuideBlock(GuideBlockKind.Bullet, "Install on the Windows PC that stays on the pharmacy Wi-Fi."),
			new GuideBlock(GuideBlockKind.Bullet, "Complete Initial Setup: firm name, owner, pharmacy email, business mode, and drug licences."),
			new GuideBlock(GuideBlockKind.Bullet, "Sign in with admin PIN/password (or linked Google account)."),
			new GuideBlock(GuideBlockKind.Bullet, "Confirm Settings → General & Profile → Business mode matches how you trade."),
			new GuideBlock(GuideBlockKind.Bullet, "Set Pharmacy / Billing Email under Firm Profile (used for the user manual mailer)."),
			new GuideBlock(GuideBlockKind.Bullet, "Open Sync and start the Sync Station listener on port 5055."),
			new GuideBlock(GuideBlockKind.Tip, "Full licence", "This production build is fully unlocked for dispensing. Keep Microsoft Store updates enabled when installed from the Store."),
			new GuideBlock(GuideBlockKind.Warning, "Licences", "If the active mode’s drug licence has expired, billing is read-only until licences are renewed in Settings / profile.")
		})),
		new UserManualChapter("retail", "Retail Billing, Substitutes (F5) & FEFO Batches", "retail billing substitute f5 fefo batch patient schedule h1 ndps barcode", new _003C_003Ez__ReadOnlyArray<GuideBlock>(new GuideBlock[10]
		{
			new GuideBlock(GuideBlockKind.Heading, "Retail counter flow"),
			new GuideBlock(GuideBlockKind.Bullet, "Start a bill with F2, then enter patient name and phone."),
			new GuideBlock(GuideBlockKind.Bullet, "For Schedule H1 / X / NDPS (and habit-forming catalogue drugs), capture address plus prescriber name and registration number."),
			new GuideBlock(GuideBlockKind.Bullet, "Search or scan items (F3). Batches are picked FEFO — earliest unexpired expiry first."),
			new GuideBlock(GuideBlockKind.Bullet, "Press F5 to open substitutes when the preferred brand is out of stock."),
			new GuideBlock(GuideBlockKind.Bullet, "Take payment with F9, then F10 to save and print."),
			new GuideBlock(GuideBlockKind.Bullet, "Use F8 Hold to park a bill; reopen from Recent bills."),
			new GuideBlock(GuideBlockKind.Heading, "FEFO & stock safety"),
			new GuideBlock(GuideBlockKind.Paragraph, "Stock quantities are movement-derived. Never sell expired batches. Near-expiry sales may ask for confirmation. Selling price must not exceed MRP. Free/scheme qty deducts stock with zero taxable value."),
			new GuideBlock(GuideBlockKind.Tip, "Tip", "Look for the blue circular i helpers next to important fields for short in-app guidance.")
		})),
		new UserManualChapter("wholesale", "Wholesale Invoicing & Credit Ledger", "wholesale invoice credit ledger buyer licence form 20 21 tax b2b outstanding", new _003C_003Ez__ReadOnlyArray<GuideBlock>(new GuideBlock[8]
		{
			new GuideBlock(GuideBlockKind.Heading, "Wholesale tax invoices"),
			new GuideBlock(GuideBlockKind.Paragraph, "Wholesale sales are only to Active customers with at least one valid, unexpired drug licence allowed for that buyer type. Patients and walk-ins are not allowed."),
			new GuideBlock(GuideBlockKind.Bullet, "Open Wholesale tax invoice and select an active licensed buyer."),
			new GuideBlock(GuideBlockKind.Bullet, "Pick medicine/batch (FEFO), Qty, optional Free qty, Rate (≤ MRP), and discount."),
			new GuideBlock(GuideBlockKind.Bullet, "Review taxable total and estimated GST."),
			new GuideBlock(GuideBlockKind.Bullet, "If over credit limit or overdue, an owner may use Owner override with an audited reason."),
			new GuideBlock(GuideBlockKind.Bullet, "Enter payment and transport/vehicle fields when goods move (e-way readiness). IRN/e-way numbers are entered manually."),
			new GuideBlock(GuideBlockKind.Tip, "Credit ledger", "Track outstanding and dunning from wholesale accounts / reconciliation screens. Corrections use credit notes or cancellations with reason — invoices stay append-only.")
		})),
		new UserManualChapter("sync", "Mobile Sync (Port 5055) & QR Pairing", "sync android mobile port 5055 qr mdns wifi firewall google drive", new _003C_003Ez__ReadOnlyArray<GuideBlock>(new GuideBlock[7]
		{
			new GuideBlock(GuideBlockKind.Heading, "LAN Sync Station"),
			new GuideBlock(GuideBlockKind.Paragraph, "Windows advertises _pharmabill-sync._tcp on the local network. Default TCP port is 5055. Keep PC and Android on the same Wi-Fi (avoid guest/client isolation)."),
			new GuideBlock(GuideBlockKind.Bullet, "Open Sync → Offline device sync and confirm Listener: Listening."),
			new GuideBlock(GuideBlockKind.Bullet, "Note the PC Wi-Fi IP and Port 5055."),
			new GuideBlock(GuideBlockKind.Bullet, "On Android, scan the QR code (or wait for mDNS discovery)."),
			new GuideBlock(GuideBlockKind.Bullet, "Use 1-tap sync after stock counts or mobile sales."),
			new GuideBlock(GuideBlockKind.Warning, "Firewall", "Allow PharmaBill on Private networks, or run scripts/allow-firewall-port5055.ps1 as Administrator. If LAN is blocked, encrypted change logs can fall back to Google Drive PharmaBill_Sync.")
		})),
		new UserManualChapter("reports", "Reports & GST Summaries (Party, B.No, Brand-wise)", "reports gst party brand bill number summary sales purchase stock", new _003C_003Ez__ReadOnlyArray<GuideBlock>(new GuideBlock[6]
		{
			new GuideBlock(GuideBlockKind.Heading, "Reports & GST"),
			new GuideBlock(GuideBlockKind.Paragraph, "Use the Reports and GST returns screens for party-wise, bill-number, and brand-wise summaries. Export to Excel/PDF when you need accountant-ready packs."),
			new GuideBlock(GuideBlockKind.Bullet, "Party-wise: filter by customer/supplier for outstanding and sale history."),
			new GuideBlock(GuideBlockKind.Bullet, "B.No: locate a specific retail/wholesale document for reprint or audit."),
			new GuideBlock(GuideBlockKind.Bullet, "Brand-wise: review movement and value by catalogue brand."),
			new GuideBlock(GuideBlockKind.Tip, "Month-end", "Sync Android stock before running GST summaries so desktop figures match the floor. Statutory registers remain append-only and cannot be edited or deleted.")
		})),
		new UserManualChapter("shortcuts", "Keyboard Shortcuts Cheat Sheet", "keyboard shortcuts f1 f2 f3 f4 f5 f8 f9 f10 esc help manual", new _003C_003Ez__ReadOnlyArray<GuideBlock>(new GuideBlock[12]
		{
			new GuideBlock(GuideBlockKind.Heading, "Global & billing shortcuts"),
			new GuideBlock(GuideBlockKind.Shortcut, "F1", "Open this User Manual & Quick Guides hub"),
			new GuideBlock(GuideBlockKind.Shortcut, "F2", "New bill (retail or wholesale desk)"),
			new GuideBlock(GuideBlockKind.Shortcut, "F3", "Focus item / medicine search"),
			new GuideBlock(GuideBlockKind.Shortcut, "F4", "Focus patient / customer fields"),
			new GuideBlock(GuideBlockKind.Shortcut, "F5", "Show substitutes for the current item"),
			new GuideBlock(GuideBlockKind.Shortcut, "F8", "Hold current bill"),
			new GuideBlock(GuideBlockKind.Shortcut, "F9", "Focus payment"),
			new GuideBlock(GuideBlockKind.Shortcut, "F10", "Save and print"),
			new GuideBlock(GuideBlockKind.Shortcut, "Esc", "Cancel / close flyout"),
			new GuideBlock(GuideBlockKind.Shortcut, "Ctrl+F", "Focus Settings search (on Settings)"),
			new GuideBlock(GuideBlockKind.Tip, "Keyboard-first", "Tab through fields in order; Enter advances search picks. PharmaBill is designed for counter speed without the mouse.")
		}))
	});
}

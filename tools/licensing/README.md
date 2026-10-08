PharmaBill offline licensing keys

- pharmabill-license-public.pem is embedded in the client (safe to commit).
- pharmabill-license-private.pem must NEVER ship or commit. Used by Debug/MASTER_ADMIN_BUILD Ctrl+Shift+K and Generate-PharmaBill-Key.ps1.

Client builds verify RSA-SHA256 signatures with the public key only.

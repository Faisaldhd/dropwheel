# Lock badge previews

`padlock-256.png` / `padlock.ico` are the only shipped Lock artwork. The displayed icons are composed at
lock time from the **system's own Explorer icon** for the item, with the padlock on the bottom-left.

- `pdf-{white,dark}-{48,256}.png`: a real Windows PDF icon (empty `sample.pdf`) + padlock.
- `jpg-{white,dark}-{48,256}.png`: a real Windows JPG icon + padlock.
- `folder-{white,dark}-{48,256}.png`: the real Windows folder icon + padlock.

Generated with `tools/lock-icons/generate.ps1` for the padlock and a one-off shell-icon preview step
(SHCreateItemFromParsingName / IShellItemImageFactory) for the base icons. The padlock is #8224D4 with
a #B48AE0 highlight, bottom-left, about 43% of the icon height, no blur or drop shadow; it is readable
on both white and dark Explorer backgrounds.

The `.dwenc`/`.dwdir` HKCU icons are composed from the system's generic file/folder icons. Windows allows
one DefaultIcon per extension; a per-file Explorer icon would require a shell icon handler, which this
tool deliberately does not install. The per-item composed icon is used by the Lock window and the
per-type cache; the real original type is shown there.

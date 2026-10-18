import os
p = r"C:\Users\FaisalSaeedMohammedD\Desktop\DropWheel_1\DropWheel\CHANGES.md"
s = open(p, encoding="utf-8").read()
old = """- Projects, namespaces, assemblies and folders: `DropWheel.Core`, `DropWheel.App`, `DropWheel.Linux`, `DropWheel.Tests`,
  `DropWheel.sln`."""
new = """- User-visible identity only: the product, exe, windows, tray, installer and README are **DropWheel**. Internal
  project/namespace names stay `FileAction.*` by owner rule (renaming them adds risk without user value)."""
if old in s:
    s = s.replace(old, new, 1)
    open(p, "w", encoding="utf-8").write(s)
    print("R-3 corrected")
else:
    print("anchor not found")

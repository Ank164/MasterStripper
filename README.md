# Master Stripper

A small Windows utility for removing one master dependency from ordinary Skyrim
compatibility patches.

## Use

1. Launch `MasterStripper.exe`.
2. Drag one or more patch `.esp` files into the window.
3. Search by any part of the filename and choose a master shared by those patches.
4. Click **Strip selected master**.

By default, the original plugin is replaced and preserved beside it as a
`.backup` file. Check **Write cleaned copies instead of replacing originals**
if you want a separate `.stripped.esp` output.

The utility first removes matching overrides directly from the plugin's binary
record tree, so stale records whose source no longer exists do not block the job.
It then deletes every remaining complete record that contains a FormID reference
to the selected master, removes the master from the header, and writes a new plugin.

If another stale record prevents structured loading, the utility identifies its
FormID, removes that unresolved record (or its containing patch record) in the raw
pass, and retries automatically.

This deliberately aggressive behavior is intended for small compatibility patches.
It is not appropriate for general-purpose mods containing records that combine
unrelated edits.

# Master Stripper for Skyrim

A small Windows utility for removing one master dependency from ordinary Skyrim
compatibility patches.

## Use

1. Launch `MasterStripper.exe`.
2. Drag one or more patch `.esp` files into the window.
3. Search by any part of the filename and select one or more masters shared by those patches.
4. Click **Strip selected master(s)**.

The patch and master lists support standard Windows multi-selection: hold Ctrl to
select individual entries or Shift to select a range. **Remove selected** removes
all selected patches from the list.

Select one or more already-compacted `.esp` files and click **Flag selected
light** to set their ESL flag. This action does not compact FormIDs. It creates a
`.backup` copy before replacing each changed plugin and skips files that are
already light flagged.

Select one or more `.esp` files and click **Flag selected master** to set their
ESM/master flag without renaming them. This action also creates a `.backup` copy
before replacing each changed plugin and skips files that are already master
flagged.

By default, the original plugin is replaced and preserved beside it as a
`.backup` file. Check **Write cleaned copies instead of replacing originals**
if you want a separate `.stripped.esp` output.

The utility first removes matching overrides directly from the plugin's binary
record tree, so stale records whose source no longer exists do not block the job.
It then deletes every remaining complete record that contains a FormID reference
to the selected master, removes the master from the header, and writes a new plugin.

For master-flagged `.esm` and `.esl` outputs, the utility rebuilds the ONAM
overridden-record list from the records that remain. This prevents deleted FormIDs
from surviving in the plugin header and blocking the write.

If another stale record prevents structured loading, the utility identifies its
FormID, removes that unresolved record (or its containing patch record) in the raw
pass, and retries automatically.

This deliberately aggressive behavior is intended for small compatibility patches.
It is not appropriate for general-purpose mods containing records that combine
unrelated edits.

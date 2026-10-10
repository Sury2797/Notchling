# Getting started with Notchling

Notchling keeps media, focus, notes and everyday controls in a compact notch at the top of your desktop. All 21 tools are free for everyone during public testing; purchasing is paused. Local tools need no application account. Optional connections still need their own player, credential, imported file or configured service.

The qualified download is listed in the [README](../README.md#try-notchling). The v0.4.7 candidate adds the contextual help and formatted installer described below; its own release qualification is still pending. Check your installed version in Settings.

## Open and move around

Launch **Notchling** from the Windows Start menu. Hover over or click the compact strip to open it. Choose a tool in the dock, or use **All tools** for the complete catalog. Pin keeps the current panel expanded. Leaving an unpinned panel collapses it after any active interaction has finished.

| Action | Control |
| --- | --- |
| Open the notch | `Ctrl + Shift + Space`, Start menu or tray → Open Notchling |
| Open Settings while the app has focus | `F2` |
| Move through controls | `Tab` / `Shift + Tab` |
| Collapse | `Esc`, when no dialog is open |
| Keep the panel open | Pin / Keep expanded |
| Save and quit | Tray → Quit Notchling |

Settings lets you choose a display, adjust placement, switch hover navigation and reduce motion. The app also respects Windows animation preferences. Home is an overview: choose a card to open its full controls.

## Use the local tools first

| Tool | First useful action |
| --- | --- |
| Media | Start playback in a player that shares Windows media controls, then use Notchling's available transport controls. Unsupported seeking or absent output stays unavailable |
| Focus | Start a Pomodoro. Pause retains the time left; Reset uses the saved interval. Countdown and Stopwatch maintain their own times |
| Notes / Scratchpad | Capture text locally. Settings offers Save now, Export notebook and Restore export |
| Shelf in v0.4.7 source | Drop a file onto the compact notch or use Choose files. Leave Save file copies off to retain a reference; use Paste image for a copied bitmap |
| Clipboard | Enable capture deliberately in Settings. Up to 50 plain-text entries remain in memory; disabling capture or quitting clears them |
| System | Inspect device CPU, memory and battery. Volume controls the current Windows audio output |
| Screen time | Inspect active time for this Notchling session. It starts over at relaunch and keeps no history of window titles or screenshots |

Notes and exports are plaintext on your device. Keep independent backups. File shortcuts and Shelf references leave the original files in place. See [Privacy](privacy.md) for storage and request details.

## Keep files and images on Shelf

The v0.4.7 candidate opens Shelf when a supported file/image drag reaches the compact notch. Drop onto the panel, or select **Choose files**. Local files and folders become references by default; Notchling never moves their originals. **Save file copies** opts into an independent local file. Folder contents are not copied.

Images supplied as bitmaps and virtual files without a persistent path are saved under `%LOCALAPPDATA%\Notch\shelf-captures`. If a browser supplies only an image link, choose **Copy image** in the browser and then **Paste image** in Shelf. Notchling does not download an image URL. Pasting does not turn on clipboard-history capture.

Each saved file is limited to **50 MiB**. The capture folder is limited to **250 MiB and 100 files**, and Shelf has a separate maximum of 100 entries. Large local files can still be references. Saved-copy labels distinguish captures from file/folder references; missing originals show an unavailable state.

**Open** uses the item's file, and **Reveal** shows its location. **Remove** takes the entry off Shelf and keeps its file or saved copy. Use **Reveal saved copies** to manage retained captures and free space; completed files can remain there after a later failed operation. Notebook exports retain item paths without embedding file contents, so back up captures separately. These changes need their own native release tests; they are not included in the current v0.3.4 download.

## Understand source icons

In the candidate, compact Media shows the player/source logo and expanded Media keeps the artwork or video thumbnail separate. Windows may identify only Chrome, Edge or Firefox. In that case, the arrow beside the source name lets you choose YouTube or another supported provider for the current track.

This is an explicit label choice: it does not change playback or inspect browser tabs. Its tooltip identifies your selection, and a new track or native session clears it. Album-art or timeline updates within the same track preserve the choice.

## Connect optional sources

Use **Settings → Provider connections** for your restricted Stripe reporting key or analytics endpoint/token. Save stores the setup; **Connection status → Check connections** determines whether a real read succeeds. Saving a key alone does not establish connectivity.

Calendar and Coding import files you choose. Revenue reports captured payment totals after refunds, rather than automatically calculating subscription MRR. Analytics displays the endpoint's latest snapshot, rather than collecting continuous tracking itself. Weather requires the owner's configured licensed service and an authenticated service session; that backend is currently absent.

Missing setup stays visible as an empty or unavailable state. It does not become sample data. Sample-data preview is an explicit session choice in Settings; **Exit preview** returns to real sources. Preview does not start audio or perform real release checks.

## Read help and check updates

The v0.4.7 candidate adds small **?** controls beside selected labels. Hover gives a short preview; click or keyboard activation opens the full local explanation. Long explanations scroll, and **Esc** closes them. Home and Settings both offer the Notchling quick guide. Help does not send a network request; opening the separate provider setup link uses your browser.

**Settings → Updates and troubleshooting → Check for updates** reads official release metadata in the candidate. Evaluation updates remain manual downloads from **Release page**. **Check for updates daily** is off by default and checks only while Notchling is running. Updates do not install automatically or replace the panel you are using.

If Setup or the app fails to open, use [Troubleshooting](troubleshooting.md). Preserve your notebook; deleting app data is not an installation repair.

# When does the Fit3 open its Bluetooth serial (SPP) port?

Firmware R390XXU0AZA3. Confidence is marked: **[app]** read in the Fit3 App source, **[fw]** static reading of the firmware (not run), **[hw]** observed on a real band.

## Short answer

The serial service (SPP, RFCOMM channel 29, UUID `db764ac8-4b08-7f25-aafe-59d03c27bae3`) is **always registered** at boot **[fw]**. What changes is whether the band is *connectable* on classic Bluetooth (the BT "access mode"). Stock firmware sets it to connectable only in a few moments:

| # | Moment | Who triggers it | Evidence |
|---|--------|-----------------|----------|
| 1 | First-time setup (OOBE) / right after pairing and configuring | The phone app over BLE (SAP protocol) | **[hw]** observed: classic BT only pairs after connecting and configuring the band; **[fw]** OOBE finish/skip handlers (`oobe finish send gm cap req`, `oobe_skip_event_cb`) post the setup-complete events |
| 2 | Firmware update (FOTA, feature 21) | App sends `FILE_INFO` then `BT_OPEN` over BLE | **[app]** Fit3-App flasher sequence; **[fw]** PROTO_FILETRANS/FOTA handlers call `dm_set_accessmode` (mode 2, with a retry counter of up to 3 attempts) |
| 3 | Media/file transfers: watch-face OTA, file manager, media files | Same `BT_OPEN` request with another feature | **[app]** |
| 4 | Engineering/factory command `AT^BT_ACCESS_SET` (prints `BT OPEN OK`) | AT command channel | **[fw]** |

After the transfer the band goes back to non-connectable (it is a window, not a permanent state) **[fw]**; only one RFCOMM client is accepted at a time (`rfcomm find same dlc`), and one operation per connection.

## The "longcheer factory" SPP is not a separate mode

Checked in the firmware **[fw]**: the "Init longcheer factory bt spp profile" function (`0x2c0c3c0c`, called unconditionally from the BT init at `0x2c0c43d4`) registers the SPP server `0x2c229038` -> `0x2c227f58`, which binds **RFCOMM channel 0x1d (29)**, the same port the tools use. Events `0x1001` / `0x1002` are just that port's **connect / disconnect** notifications ("Fac SPP connect/disconnect, go to service"; they set the "SPP connected" flag at `0x20107bc1`; the BLE twin sets `0x20107bc0`), and `0x1003` is a received frame (`s_bt_spp_lc_proto_rx`, up to 0x400 bytes). So there is no factory switch to trigger: the factory SPP **is** the serial port, and the only gate is the access mode above.

## What the patches change

- `--spp-access`: the central access-mode setter (`0x2c233820`) always uses mode 3 (general accessible), so the port is connectable at all times. **[hw]** confirmed working.
- `--spp-multi`: allows a second RFCOMM client on the port.
- Neither needs the BLE `BT_OPEN` step any more; stock firmware does.

## Complete call list (exhaustive scan, 2026-10-12)

A full instruction-level scan of the firmware image for every `bl`/`b.w` to the central `dm_set_accessmode` thunk (`0x2c0c6c76`) found exactly **8 call sites** — this supersedes the table above as the authoritative list. **[fw]**, static only.

| Call site | Mode | What calls it |
|---|---|---|
| `0x2c0e6bde` | **0 (close)** | Inside the "phoneinfo" sync handler (logs `oobestatus`/`oobeScreenOfftime`); reached from both the OOBE-finish path (`0x2c10e83c`, "oobe finish send gm cap req") and the OOBE-skip path (`0x2c1cb688`, `oobe_skip_event_cb`) |
| `0x2c129ffe` | **2 (open)** | FILETRANS chunk-timer retry path (`"send chunk timer msg fail"`) |
| `0x2c12a21a` | **2 (open)** | FILETRANS/FOTA `BT_OPEN` confirmation (the one `BT_OPEN` from the app reaches) |
| `0x2c12a23c` | **2 (open)** | Same handler's retry counter (≤3 attempts) |
| `0x2c1465b8` | **3 (open, general)** | Standalone handler, not reached by any direct `bl` (likely an event-table entry, not identified); posts event `0x502` right after |
| `0x2c146634` | **0 (close)** | Standalone handler (same call shape as above, not reached by direct `bl`); touches the same global (`0x2010c9ec`) as the mode-3 handler above — looks like its close counterpart |
| `0x2c146816` | **argument** | `AT^BT_ACCESS_SET` engineering command |
| `0x2c1b1ea0` | **0 (close), conditional** | Also reached from the OOBE finish/skip UI handler (`0x2c1c48c8`), but only on one branch (a session-count check); the other branch calls a sibling thunk (`0x2c0c43d0`) instead, not traced |

**Revises the table above:** the OOBE finish/skip paths that I assumed *opened* access turn out to call **mode 0 (close)** at these points, not open. The only code paths that demonstrably set an *open* mode (2 or 3) are: the app's `BT_OPEN` request (3 call sites, mode 2), the engineering AT command (arbitrary mode), and one still-unidentified standalone handler at `0x2c1465b8` (mode 3). What actually opens the port right after first-time setup/pairing, confirmed only **[hw]** by observation, has not been located in code — it may be the same `BT_OPEN` request sent by the phone app during OOBE itself (which the app source does support, feature-wise), rather than something the watch firmware opens on its own.

## Not verified

- What specifically calls the mode-3 handler at `0x2c1465b8` and the paired mode-0 handler at `0x2c146634` (no direct `bl` found; likely reached through an event-dispatch table not yet located).
- The sibling thunk `0x2c0c43d0` reached from the other OOBE branch — not traced, may or may not affect access mode.
- Exact condition that closes the window again after a transfer.
- All of the "[fw]" rows come from disassembly only; nothing above was run on a band except the `--spp-access` patch result.

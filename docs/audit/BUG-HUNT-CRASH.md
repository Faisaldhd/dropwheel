# Bug hunt — crashes and unhandled exceptions on hostile input

Time-boxed hunt in the security lane, using the 72-case hostile-input suite as the map and code reading for the
gaps. **No src edits, no builds, no exe runs.** Evidence is the merged tree (`origin/main` @ `8b28758`), CI logs,
and the owner's sample pack read in place.

Severity: **P0** = kills a release gate or the app on a normal path; **P1** = user-visible crash/failed window
on a hostile but realistic input; **P2** = latent/unobserved or caught-late.

| ID | Sev | Trigger | Repro / CI evidence | file:line | Suggested fix | Owner |
| --- | --- | --- | --- | --- | --- | --- |
| **CR-01** | **P0** | Any exception on the UI thread inside a surface the shipped `--self-test` builds. The self-test host creates `new Application` **without** the app's `DispatcherUnhandledException` handler, so one bad window takes the whole gate down instead of failing that surface. | **Proven:** run [39380479876](https://github.com/Faisaldhd/dropwheel/actions/runs/39380479876), head `bc204c4` — `FAILED: System.ArgumentException` in `LockIcons.Compose` → `self-contained --self-test failed with 1`; the job never reached the unit `Test` step, so no test result existed for that SHA. The *instance* was fenced at `8b28758` (`LockIcons.cs:47-52`), **the class is not**: there is still no handler in the self-test host. | `SelfTest.cs:28-34` (host), contrast `App.cs:100` and `App.cs:480-485` (`e.Handled = true`) | Register the same guarded handler (or `CrashGuard`) in the self-test host and convert a caught exception into a `FAILED <surface>` line + non-zero exit, so one surface cannot erase the whole run. | App shell / selftest lane |
| **CR-02** | **P1** | A corrupt or truncated PDF opened in **Organize Pages**, or any `pdf.to.*` conversion. PDFium failures are not wrapped, unlike every PDFsharp path. | Source read + the owner's `pack/hostile/truncated.pdf` (1,562 B, `%PDF-1.3` prefix) and a 0-byte PDF. `PdfBackend.PageCount` throws the raw decoder exception; the tool awaits it on the UI thread (`PdfOrganizeTool.cs:215`, caught at `:217-221` → localized status) but the **conversion** path has no such catch, so the job card / redact-style previews show the raw decoder text instead of `error.pdf.cannotOpen`. | `src/FileAction.Core/Backends/PdfBackend.cs:13-17` (`PageCount`), `:22-25` (`RenderAsync`); the wrapped model is at `:255-264` (`Open`) | Wrap `PDFtoImage.Conversion.GetPageCount` in `PageCount`/`RenderAsync` as `ConversionException(T("error.pdf.cannotOpen", name, message))`. | Core (unassigned) |
| **CR-03** | P2 | Open Organize Pages on a corrupt PDF, then close the window before activation. `_pageCount` is started in the constructor and awaited only on activation. | Source read; no crash observed (the task is caught at the await). The fault is simply unobserved and the window dies. | `PdfOrganizeTool.cs:110`, `:215-221` | Start the probe lazily in `ActivateAsync`, or observe it in a `finally`/`ContinueWith` that clears the field. | UI (unassigned) |
| **CR-04** | P2 | Missing, renamed or crash-on-start `ffmpeg`/`pandoc` **mid-run** (helper deleted while a job is queued). | **Verified negative — no crash.** Start failures are wrapped as `error.process.cannotStart` and non-zero exits as `error.process.exitCode`. | `src/FileAction.Core/ProcessRunner.cs:69-71`, `:93-99` | none needed; recorded as a checked surface | — |
| **CR-05** | P2 | Fire-and-forget render on a hostile image in Redact. | **Verified negative — no crash.** The discarded task is fully guarded (outer `try` `:717` and inner `try` around the codec call `:731-738`), so the unobserved-task risk is closed by construction. | `RedactView.cs:712`, `:715-747` | none needed | — |
| **CR-06** | P2 | Six `async void` handlers if any of them is ever invoked by a host without the app's global handler (see CR-01). | Source read: `JobRunner.Start` (×2), `LockCli.Open`, `AddBackgroundView.PickImage`, `UpdateBanner.CheckNow`/`DownloadNow`. In the shipped app every one of them lands on `App.OnUnhandled`, which sets `e.Handled = true`, so the user gets a toast, not a crash. | `JobRunner.cs:24,27`; `LockCli.cs:94`; `AddBackgroundView.cs:595`; `UpdateBanner.cs:304,349` | not a defect in the app; only CR-01's host is unprotected | — |

## Not reproduced, and why

* **No crash was found in the image/archive/lock families** on the pack's hostile shapes: the 72-case suite
  already asserts “throws, no partial output, source unchanged” for 0-byte, 1-byte, text-as-PNG, truncated
  PNG/JPEG, empty/corrupt PDF, empty ZIP and zip-slip entries, and the lock/zip paths return typed
  `PasswordProtectionException`/`ConversionException`.
* **H-03…H-06** (output-name clamp, batch message text, PDFium wrapping, `PackAsync` partial) are **not**
  re-listed except CR-02, where H-05 gains a new consequence: a crash-dialog/toast surface on a hostile PDF
  rather than merely an untidy message.
* **Cannot verify headlessly:** whether a faulted UI surface leaves the app usable after the toast (needs a
  desktop), and whether the `LockIcons` fallback is visually acceptable.

## Limits

Static reading + CI logs only. Line numbers are `8b28758`. This is a crash-surface map, not a proof of absence:
the two negatives (CR-04, CR-05) are the only places I read the full guard chain.

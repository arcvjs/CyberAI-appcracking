# Local trial demo

This build intentionally demonstrates an insecure local trial implementation. It is not a production licensing control.

Without a subscription activation, first launch creates `%LOCALAPPDATA%\Prism\Config\trial.json` with a 14-day trial:

```json
{
  "version": 1,
  "startedAtUtc": "2026-09-08T00:00:00+00:00",
  "expiresAtUtc": "2026-09-22T00:00:00+00:00"
}
```

To demonstrate the bug, change `expiresAtUtc` to a past date and attempt an edit: editing becomes locked. Change it to a future date such as `2030-09-22T00:00:00+00:00` and try again: editing becomes available. This can be repeated without a maximum extension. The JSON is re-read at each editing gate and periodic license check. Reopen the subscription dialog after a check to see the updated expiry.

The defect is trusting an unsigned local date without a server-side trial record or a maximum duration check. Deleting the config also starts a fresh trial. Invalid JSON leaves editing locked without affecting document viewing, saving, or exporting.

Existing subscription activations take precedence. Use a Windows profile without a subscription activation, or deactivate the device through Prism, to demonstrate the trial. The current Python protocol uses unsigned responses; the older signed protocol is retained separately in source/tests.

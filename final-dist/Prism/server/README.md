# Licensing servers

Both scripts use Python 3 with no extra dependencies and the same POST /validate JSON protocol.

## VPS: real key validation

Copy `license_server.py` to the VPS. Issue a subscription locally on the VPS:

```sh
python3 license_server.py --issue "Customer name" --days 30
python3 license_server.py --host 0.0.0.0 --port 47831
```

The first command prints a random key and saves it in `licenses.json` beside the script. Keep that file on the server. Unknown, disabled, expired, missing-record, and malformed-record keys are rejected. Successful checks return the stored expiry; they do not extend the subscription. Set `enabled` to false to revoke a key, or edit `expiresAt` to renew it. Records are reloaded on each request. Stop the server before issuing keys or making concurrent record edits.

Allow inbound TCP 47831 in the VPS firewall and Azure network rules if needed. This update has not been deployed to the VPS automatically; replace any earlier accept-all server before using it remotely.

## Localhost: accept-any-key demo

Keep both Python scripts in the same folder and run:

```sh
python local_demo_server.py
```

This script binds only to 127.0.0.1 and accepts every nonempty key. It grants 30 days on every check and does not read the real key list.

Prism defaults to `http://70.153.24.7:47831`. To switch, edit `%LOCALAPPDATA%\Prism\Config\licensing.json` after first launch, then restart Prism:

```json
{"serverUrl":"http://127.0.0.1:47831"}
```

Set the URL back to `http://70.153.24.7:47831` to use the VPS. Activations are cached separately per server; local demo activation does not activate the VPS configuration.

## Protocol

Request: `POST /validate`, JSON `{"key":"PRISM-...","deviceId":"device-hash"}`.

Accepted: `{"valid":true,"customer":"Customer name","expiresAt":"2030-01-01T00:00:00+00:00"}`.

Rejected: `{"valid":false,"message":"Invalid or disabled license key."}`.

The client checks every 12 hours and allows up to seven days offline, capped by subscription expiry. A rejection on a check blocks the cached activation immediately. Offline devices can continue until their cached lease expires. Deactivation clears the local cache; there are no server-side device limits. Previous signed-protocol activations are not used. Responses remain unsigned and HTTP is allowed, as requested for this demo. The editable trial remains available without activation.

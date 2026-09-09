"""Prism VPS server: validates keys against a local subscription file."""
import argparse
import json
import secrets
from pathlib import Path
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer


class Handler(BaseHTTPRequestHandler):
    def read_json(self):
        # HttpClient's streaming JSON content uses HTTP/1.1 chunked framing.
        if self.headers.get("Transfer-Encoding", "").lower() == "chunked":
            body = bytearray()
            while True:
                line = self.rfile.readline(128)
                if not line.endswith(b"\r\n"):
                    raise ValueError()
                size = int(line.split(b";", 1)[0], 16)
                if size < 0 or len(body) + size > 8192:
                    raise ValueError()
                if size == 0:
                    for _ in range(32):
                        trailer = self.rfile.readline(1024)
                        if trailer == b"\r\n":
                            return json.loads(body)
                        if not trailer.endswith(b"\r\n"):
                            raise ValueError()
                    raise ValueError()
                chunk = self.rfile.read(size)
                if len(chunk) != size or self.rfile.read(2) != b"\r\n":
                    raise ValueError()
                body.extend(chunk)
        if self.headers.get("Transfer-Encoding"):
            raise ValueError()
        size = int(self.headers.get("Content-Length", "0"))
        if not 0 < size <= 8192:
            raise ValueError()
        return json.loads(self.rfile.read(size))

    def validate(self, key):
        try:
            licenses = json.loads(self.server.licenses_path.read_text(encoding="utf-8"))
            entry = licenses.get(key)
            if not isinstance(entry, dict) or entry.get("enabled") is not True:
                return {"valid": False, "message": "Invalid or disabled license key."}
            expiry = datetime.fromisoformat(entry["expiresAt"])
            if expiry.tzinfo is None or expiry <= datetime.now(timezone.utc):
                return {"valid": False, "message": "Subscription expired."}
            return {"valid": True, "customer": entry.get("customer", "Prism user"),
                    "expiresAt": expiry.isoformat()}
        except (OSError, ValueError, KeyError, TypeError, AttributeError):
            return {"valid": False, "message": "License records unavailable."}

    def reply(self, status, data):
        body = json.dumps(data).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        self.reply(200 if self.path == "/health" else 404,
                   {"ok": self.path == "/health"})

    def do_POST(self):
        if self.path != "/validate":
            return self.reply(404, {"valid": False})
        try:
            data = self.read_json()
            key = data.get("key") if isinstance(data, dict) else None
            if not isinstance(key, str) or not key.strip():
                return self.reply(200, {"valid": False, "message": "Enter a license key."})
        except (ValueError, UnicodeDecodeError):
            return self.reply(400, {"valid": False, "message": "Expected JSON with a key."})
        self.reply(200, self.validate(key.strip()))


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=47831)
    parser.add_argument("--licenses", type=Path, default=Path(__file__).with_name("licenses.json"))
    parser.add_argument("--issue", metavar="CUSTOMER", help="Create a key and exit (stop server before issuing).")
    parser.add_argument("--days", type=int, default=30)
    args = parser.parse_args()
    if args.issue:
        if args.days < 1:
            parser.error("--days must be positive")
        records = json.loads(args.licenses.read_text(encoding="utf-8")) if args.licenses.exists() else {}
        if not isinstance(records, dict):
            parser.error("License records must be a JSON object")
        key = "PRISM-" + secrets.token_hex(16).upper()
        records[key] = {"customer": args.issue, "enabled": True,
                        "expiresAt": (datetime.now(timezone.utc) + timedelta(days=args.days)).isoformat()}
        temporary = args.licenses.with_suffix(".tmp")
        temporary.write_text(json.dumps(records, indent=2), encoding="utf-8")
        temporary.replace(args.licenses)
        print(key)
        raise SystemExit(0)
    print(f"Prism licensing: http://{args.host}:{args.port}", flush=True)
    server = ThreadingHTTPServer((args.host, args.port), Handler)
    server.licenses_path = args.licenses
    server.serve_forever()

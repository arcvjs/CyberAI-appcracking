import argparse
from datetime import datetime, timedelta, timezone
from http.server import ThreadingHTTPServer
from license_server import Handler


class DemoHandler(Handler):
    def validate(self, key):
        return {"valid": True, "customer": "Local demo",
                "expiresAt": (datetime.now(timezone.utc) + timedelta(days=30)).isoformat()}


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, default=47831)
    args = parser.parse_args()
    print(f"Local demo licensing: http://127.0.0.1:{args.port}", flush=True)
    ThreadingHTTPServer(("127.0.0.1", args.port), DemoHandler).serve_forever()

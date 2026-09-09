import json
import http.client
import threading
import unittest
import tempfile
from pathlib import Path
from datetime import datetime, timezone, timedelta
from urllib.request import Request, urlopen
from urllib.error import HTTPError
from http.server import ThreadingHTTPServer
from license_server import Handler as StrictHandler
from local_demo_server import DemoHandler as Handler


class ProtocolTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        cls.thread = threading.Thread(target=cls.server.serve_forever, daemon=True)
        cls.thread.start()
        cls.url = f"http://127.0.0.1:{cls.server.server_port}"

    @classmethod
    def tearDownClass(cls):
        cls.server.shutdown()
        cls.server.server_close()
        cls.thread.join()

    def post(self, data):
        request = Request(self.url + "/validate", json.dumps(data).encode(),
                          {"Content-Type": "application/json"})
        with urlopen(request, timeout=3) as response:
            return json.load(response)

    def test_arbitrary_keys_and_renewal(self):
        for key in ["DEMO", "random-key-123", "another key"]:
            for _ in range(2):
                result = self.post({"key": key, "deviceId": "test-device"})
                self.assertTrue(result["valid"])
                remaining = datetime.fromisoformat(result["expiresAt"]) - datetime.now(timezone.utc)
                self.assertGreater(remaining.total_seconds(), 29 * 86400)

    def test_empty_keys_rejected(self):
        for key in ["", "   ", None, 123]:
            self.assertFalse(self.post({"key": key})["valid"])

    def test_bad_json(self):
        with self.assertRaises(HTTPError) as error:
            urlopen(Request(self.url + "/validate", b"not-json"), timeout=3)
        self.assertEqual(error.exception.code, 400)

    def test_chunked_client_request(self):
        connection = http.client.HTTPConnection("127.0.0.1", self.server.server_port, timeout=3)
        try:
            connection.request("POST", "/validate", iter([b'{"key":', b'"REAL-KEY"}']),
                               {"Content-Type": "application/json"}, encode_chunked=True)
            response = connection.getresponse()
            self.assertEqual(response.status, 200)
            self.assertTrue(json.loads(response.read())["valid"])
        finally:
            connection.close()

    def test_health(self):
        with urlopen(self.url + "/health", timeout=3) as response:
            self.assertTrue(json.load(response)["ok"])


class VpsTests(ProtocolTests):
    @classmethod
    def setUpClass(cls):
        cls.temp = tempfile.TemporaryDirectory()
        cls.path = Path(cls.temp.name) / "licenses.json"
        cls.expiry = (datetime.now(timezone.utc) + timedelta(days=30)).isoformat()
        cls.records = {
            "REAL-KEY": {"enabled": True, "customer": "Test", "expiresAt": cls.expiry},
            "DISABLED": {"enabled": False, "expiresAt": cls.expiry},
            "EXPIRED": {"enabled": True, "expiresAt": "2020-01-01T00:00:00+00:00"},
            "BAD-DATE": {"enabled": True, "expiresAt": "not-a-date"},
        }
        cls.path.write_text(json.dumps(cls.records), encoding="utf-8")
        cls.server = ThreadingHTTPServer(("127.0.0.1", 0), StrictHandler)
        cls.server.licenses_path = cls.path
        cls.thread = threading.Thread(target=cls.server.serve_forever, daemon=True)
        cls.thread.start()
        cls.url = f"http://127.0.0.1:{cls.server.server_port}"

    @classmethod
    def tearDownClass(cls):
        super().tearDownClass()
        cls.temp.cleanup()

    def test_arbitrary_keys_and_renewal(self):
        for key in ["DEMO", "random-key", "DISABLED", "EXPIRED", "BAD-DATE"]:
            self.assertFalse(self.post({"key": key})["valid"])
        for _ in range(2):
            result = self.post({"key": "REAL-KEY"})
            self.assertTrue(result["valid"])
            self.assertEqual(result["expiresAt"], self.expiry)

    def test_record_changes_and_fail_closed(self):
        try:
            self.path.write_text("{}", encoding="utf-8")
            self.assertFalse(self.post({"key": "REAL-KEY"})["valid"])
            self.path.write_text("broken", encoding="utf-8")
            self.assertFalse(self.post({"key": "REAL-KEY"})["valid"])
            self.path.unlink()
            self.assertFalse(self.post({"key": "REAL-KEY"})["valid"])
        finally:
            self.path.write_text(json.dumps(self.records), encoding="utf-8")


if __name__ == "__main__":
    unittest.main()

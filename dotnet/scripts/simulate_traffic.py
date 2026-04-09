#!/usr/bin/env python3
"""
Generate HTTP traffic against the lab Container App to produce traces and logs.

Windows (PowerShell), from repo root (azure/aci/dotnet):
  python .\scripts\simulate_traffic.py --base-url https://YOUR_APP.azurecontainerapps.io

Linux/macOS:
  python3 ./scripts/simulate_traffic.py --base-url https://YOUR_APP.azurecontainerapps.io
"""

from __future__ import annotations

import argparse
import random
import sys
import time
import urllib.error
import urllib.request


def main() -> int:
    parser = argparse.ArgumentParser(description="Generate sample load for AcaOtelLab.")
    parser.add_argument(
        "--base-url",
        required=True,
        help="HTTPS base URL of the Container App (no trailing slash required).",
    )
    parser.add_argument("--requests", type=int, default=40, help="Total HTTP calls.")
    parser.add_argument("--sleep-ms", type=int, default=150, help="Pause between calls.")
    args = parser.parse_args()

    base = args.base_url.rstrip("/")
    # Weight /work higher so Splunk shows multi-span traces often.
    paths = ["/", "/healthz", "/work", "/work", "/work"]

    for i in range(args.requests):
        path = random.choice(paths)
        url = base + path
        try:
            req = urllib.request.Request(url, headers={"User-Agent": "aca-otel-lab-simulator/1.0"})
            with urllib.request.urlopen(req, timeout=120) as response:
                body = response.read().decode("utf-8", errors="replace")
                snippet = body.replace("\n", " ")[:160]
                print(f"{i + 1:04d}  {response.status}  {path}  {snippet}")
        except urllib.error.HTTPError as e:
            print(f"{i + 1:04d}  HTTP {e.code}  {path}  {e.reason}")
        except urllib.error.URLError as e:
            print(f"{i + 1:04d}  ERROR  {path}  {e.reason}")
        time.sleep(max(0, args.sleep_ms) / 1000.0)

    print("Done. In Splunk, search for service.name / deployment.environment and trace_id fields.")
    return 0


if __name__ == "__main__":
    sys.exit(main())

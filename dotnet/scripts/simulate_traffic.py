#!/usr/bin/env python3
r"""
Generate HTTP traffic against the lab Container App to produce traces and logs.

From repo root (azure/aci/dotnet) using Terraform output:
  python3 ./scripts/simulate_traffic.py --from-terraform

From the scripts/ folder (same as above; resolves ../terraform automatically):
  python3 simulate_traffic.py --from-terraform

With an explicit public URL:
  python3 ./scripts/simulate_traffic.py --base-url https://YOUR_APP.azurecontainerapps.io

If TLS verification fails (e.g. corporate SSL inspection), add --insecure for lab use only.
"""

from __future__ import annotations

import argparse
import random
import ssl
import subprocess
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path


def default_terraform_dir() -> Path:
    """Lab layout: dotnet/scripts/this.py -> dotnet/terraform/."""
    return Path(__file__).resolve().parent.parent / "terraform"


def resolve_base_url_from_terraform(terraform_dir: Path) -> str:
    if not terraform_dir.is_dir():
        raise SystemExit(
            f"Terraform directory not found: {terraform_dir}\n"
            "Pass --terraform-dir PATH, or run from the dotnet lab root "
            "(folder that contains terraform/ and scripts/)."
        )
    try:
        proc = subprocess.run(
            ["terraform", "output", "-raw", "public_url"],
            cwd=str(terraform_dir),
            check=True,
            capture_output=True,
            text=True,
        )
    except FileNotFoundError:
        raise SystemExit("terraform CLI not found on PATH.") from None
    except subprocess.CalledProcessError as e:
        raise SystemExit(
            "terraform output failed. Applied the stack at least once?\n"
            f"  {e.stderr or e.stdout or e}"
        ) from e
    url = (proc.stdout or "").strip()
    if not url:
        raise SystemExit(
            "terraform returned an empty public_url. Run terraform apply in "
            f"{terraform_dir} first."
        )
    return url.rstrip("/")


def require_absolute_http_url(base: str, *, source: str) -> str:
    base = base.strip().rstrip("/")
    if not base:
        raise SystemExit(
            f"Empty --base-url ({source}).\n"
            "If you use shell substitution, run from the dotnet root and use:\n"
            '  terraform -chdir=terraform output -raw public_url\n'
            "Or run this script with --from-terraform (works from scripts/ too)."
        )
    lower = base.lower()
    if not (lower.startswith("http://") or lower.startswith("https://")):
        raise SystemExit(
            f"Invalid --base-url ({source!r}): must start with http:// or https://\n"
            f"Got: {base!r}\n"
            "Tip: use --from-terraform instead of a broken subshell."
        )
    return base


def main() -> int:
    parser = argparse.ArgumentParser(description="Generate sample load for AcaOtelLab.")
    src = parser.add_mutually_exclusive_group(required=True)
    src.add_argument(
        "--base-url",
        metavar="URL",
        help="HTTPS base URL of the Container App (no trailing slash required).",
    )
    src.add_argument(
        "--from-terraform",
        action="store_true",
        help="Set base URL from `terraform output -raw public_url` (default dir: ../terraform next to this script).",
    )
    parser.add_argument(
        "--terraform-dir",
        type=Path,
        default=None,
        help="Terraform folder when using --from-terraform (default: lab's terraform/).",
    )
    parser.add_argument("--requests", type=int, default=40, help="Total HTTP calls.")
    parser.add_argument("--sleep-ms", type=int, default=150, help="Pause between calls.")
    parser.add_argument(
        "--insecure",
        action="store_true",
        help="Skip TLS certificate verification (lab / broken local trust store only).",
    )
    args = parser.parse_args()

    if args.from_terraform:
        tf_dir = args.terraform_dir or default_terraform_dir()
        tf_dir = tf_dir.resolve()
        base = require_absolute_http_url(
            resolve_base_url_from_terraform(tf_dir),
            source="terraform output public_url",
        )
    else:
        base = require_absolute_http_url(args.base_url or "", source="--base-url")

    ssl_ctx: ssl.SSLContext | None = None
    if args.insecure:
        ssl_ctx = ssl.create_default_context()
        ssl_ctx.check_hostname = False
        ssl_ctx.verify_mode = ssl.CERT_NONE

    paths = ["/", "/healthz", "/work", "/work", "/work"]

    for i in range(args.requests):
        path = random.choice(paths)
        url = base + path
        try:
            req = urllib.request.Request(url, headers={"User-Agent": "aca-otel-lab-simulator/1.0"})
            with urllib.request.urlopen(req, timeout=120, context=ssl_ctx) as response:
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

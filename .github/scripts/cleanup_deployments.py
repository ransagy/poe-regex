#!/usr/bin/env python3
"""Prune stale Cloudflare Pages deployments and temporary preview branches.

The scheduled workflow keeps the canonical production deployment and the
latest preview for every open pull request. Everything else is removed once it
is outside the retention window. The script is also safe to run manually with
DRY_RUN=true.
"""

from __future__ import annotations

import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime, timedelta, timezone

CF_API = "https://api.cloudflare.com/client/v4"
GITHUB_API = "https://api.github.com"
PAGES_PAGE_SIZE = 25
GITHUB_PAGE_SIZE = 100


def request_json(method: str, url: str, token: str, extra_headers: dict[str, str] | None = None):
    headers = {
        "Authorization": f"Bearer {token}",
        "Accept": "application/json",
        "Content-Type": "application/json",
        **(extra_headers or {}),
    }
    data = None
    req = urllib.request.Request(url, data=data, method=method, headers=headers)
    try:
        with urllib.request.urlopen(req, timeout=30) as response:
            body = response.read()
            return json.loads(body) if body else None
    except urllib.error.HTTPError as error:
        detail = error.read().decode(errors="replace")
        raise SystemExit(f"{method} {url} failed: HTTP {error.code}: {detail}") from error


def cf_get(path: str, token: str):
    return request_json("GET", f"{CF_API}{path}", token)


def gh_get(path: str, token: str):
    return request_json(
        "GET",
        f"{GITHUB_API}{path}",
        token,
        {"Accept": "application/vnd.github+json", "X-GitHub-Api-Version": "2022-11-28"},
    )


def gh_delete(path: str, token: str):
    request_json(
        "DELETE",
        f"{GITHUB_API}{path}",
        token,
        {"Accept": "application/vnd.github+json", "X-GitHub-Api-Version": "2022-11-28"},
    )


def pages_deployments(account_id: str, project: str, token: str):
    deployments = []
    page = 1
    while True:
        data = cf_get(
            f"/accounts/{account_id}/pages/projects/{project}/deployments"
            f"?per_page={PAGES_PAGE_SIZE}&page={page}",
            token,
        )
        batch = data.get("result") or []
        deployments.extend(batch)
        if len(batch) < PAGES_PAGE_SIZE:
            return deployments
        page += 1


def open_pull_requests(repo: str, token: str):
    pulls = []
    page = 1
    while True:
        batch = gh_get(
            f"/repos/{repo}/pulls?state=open&per_page={GITHUB_PAGE_SIZE}&page={page}",
            token,
        )
        pulls.extend(batch or [])
        if len(batch or []) < GITHUB_PAGE_SIZE:
            return pulls
        page += 1


def parse_time(value: str | None):
    return datetime.fromisoformat(value.replace("Z", "+00:00")) if value else None


def deployment_branch(deployment: dict) -> str:
    return (
        (deployment.get("deployment_trigger") or {})
        .get("metadata", {})
        .get("branch")
        or ""
    )


def deployment_status(deployment: dict) -> str:
    return ((deployment.get("latest_stage") or {}).get("status") or "").lower()


def plan_deployment_cleanup(
    deployments: list[dict],
    canonical_id: str,
    pulls: list[dict],
    retention_days: int,
    now: datetime,
):
    """Return (delete, keep, reasons) for Pages deployments.

    Production: keep only the canonical deployment.
    Preview, open PR branch: keep the newest deployment and the newest
    successful deployment, so a queued build does not remove the alias target.
    Preview, everything else: keep until it is older than retention_days.
    Dependabot previews are never wanted.
    """
    open_branches = {pull["head"]["ref"] for pull in pulls if pull.get("head", {}).get("ref")}
    open_contrib = {f"contrib-pr-{pull['number']}" for pull in pulls if pull.get("number")}

    previews_by_branch: dict[str, list[dict]] = {}
    for deployment in deployments:
        if deployment.get("environment") == "preview":
            previews_by_branch.setdefault(deployment_branch(deployment), []).append(deployment)

    keep: dict[str, str] = {canonical_id: "canonical production"}
    delete: list[tuple[dict, str]] = []

    for deployment in deployments:
        deployment_id = deployment.get("id") or ""
        environment = deployment.get("environment")
        if environment == "production":
            if deployment_id != canonical_id:
                delete.append((deployment, "old production"))
            continue
        if environment != "preview":
            continue

        branch = deployment_branch(deployment)
        if branch.startswith("dependabot/"):
            delete.append((deployment, "dependabot preview"))
            continue
        if branch in open_branches or branch in open_contrib:
            continue
        created = parse_time(deployment.get("created_on"))
        if created is None:
            keep[deployment_id] = "unknown age, keeping"
        elif created >= now - timedelta(days=retention_days):
            keep[deployment_id] = "inside retention window"
        else:
            delete.append((deployment, "orphan preview"))

    for branch, branch_deployments in previews_by_branch.items():
        if branch not in open_branches and branch not in open_contrib:
            continue
        ordered = sorted(
            branch_deployments,
            key=lambda d: parse_time(d.get("created_on")) or datetime.min.replace(tzinfo=timezone.utc),
            reverse=True,
        )
        for deployment in ordered[:1]:
            keep[deployment["id"]] = "newest preview for open PR"
        successful = [d for d in ordered if deployment_status(d) == "success"]
        if successful:
            keep[successful[0]["id"]] = "newest successful preview for open PR"
        for deployment in ordered:
            if deployment["id"] not in keep:
                delete.append((deployment, "older preview for open PR"))

    return delete, keep


def list_branches(repo: str, token: str):
    branches = []
    page = 1
    while True:
        batch = gh_get(
            f"/repos/{repo}/branches?per_page={GITHUB_PAGE_SIZE}&page={page}",
            token,
        )
        branches.extend(batch or [])
        if len(batch or []) < GITHUB_PAGE_SIZE:
            return branches
        page += 1


def plan_branch_cleanup(branches: list[dict], pulls: list[dict]):
    open_numbers = {pull["number"] for pull in pulls if pull.get("number")}
    doomed = []
    for branch in branches:
        name = branch.get("name") or ""
        if not name.startswith("contrib-pr-"):
            continue
        try:
            number = int(name.removeprefix("contrib-pr-"))
        except ValueError:
            continue
        if number not in open_numbers:
            doomed.append(name)
    return doomed


def main() -> int:
    mode = os.environ.get("CLEANUP_MODE", "all").lower()
    if mode not in {"all", "deployments", "branches"}:
        raise SystemExit("CLEANUP_MODE must be all, deployments, or branches")
    cf_token = os.environ.get("CF_API_TOKEN", "")
    account_id = os.environ.get("CF_ACCOUNT_ID", "")
    project = os.environ.get("CF_PROJECT", "")
    gh_token = os.environ.get("GH_TOKEN", "")
    repo = os.environ.get("REPO", "")
    dry_run = os.environ.get("DRY_RUN", "true").lower() == "true"
    try:
        retention_days = int(os.environ.get("RETENTION_DAYS", "7"))
    except ValueError:
        raise SystemExit("RETENTION_DAYS must be an integer")

    required = [("GH_TOKEN", gh_token), ("REPO", repo)]
    if mode in {"all", "deployments"}:
        required.extend(
            [
                ("CF_API_TOKEN", cf_token),
                ("CF_ACCOUNT_ID", account_id),
                ("CF_PROJECT", project),
            ]
        )
    missing = [name for name, value in required if not value]
    if missing:
        raise SystemExit(f"missing required environment variables: {', '.join(missing)}")
    if retention_days < 0:
        raise SystemExit("RETENTION_DAYS must be zero or greater")

    pulls = open_pull_requests(repo, gh_token)
    print(f"Mode: {mode}")
    print(f"Open pull requests: {len(pulls)}")

    if mode in {"all", "deployments"}:
        project_data = (cf_get(f"/accounts/{account_id}/pages/projects/{project}", cf_token) or {}).get("result") or {}
        deployments = pages_deployments(account_id, project, cf_token)
        deployment_ids = {deployment.get("id") for deployment in deployments}
        canonical_id = (project_data.get("canonical_deployment") or {}).get("id")
        if not canonical_id:
            production = [
                d
                for d in deployments
                if d.get("environment") == "production" and deployment_status(d) == "success"
            ]
            production.sort(key=lambda d: parse_time(d.get("created_on")) or datetime.min.replace(tzinfo=timezone.utc))
            canonical_id = production[-1]["id"] if production else None
        if not canonical_id or canonical_id not in deployment_ids:
            raise SystemExit("could not identify the canonical production deployment; refusing to delete anything")

        now = datetime.now(timezone.utc)
        delete, keep = plan_deployment_cleanup(
            deployments, canonical_id, pulls, retention_days, now
        )

        print(f"Pages project: {project}")
        print(f"Deployments: {len(deployments)}; keeping {len(keep)}; deleting {len(delete)}")
        for deployment, reason in delete:
            branch = deployment_branch(deployment) or "-"
            print(f"  DELETE {deployment.get('short_id') or deployment.get('id')} [{reason}] branch={branch}")

        if dry_run:
            print("dry run: no Pages deployments deleted")
        else:
            for deployment, reason in delete:
                deployment_id = deployment.get("id")
                if not deployment_id:
                    continue
                request_json(
                    "DELETE",
                    f"{CF_API}/accounts/{account_id}/pages/projects/{project}/deployments/{deployment_id}?force=true",
                    cf_token,
                )
                print(f"  deleted {deployment.get('short_id') or deployment_id} [{reason}]")

    if mode in {"all", "branches"}:
        branches = list_branches(repo, gh_token)
        doomed_branches = plan_branch_cleanup(branches, pulls)
        print(f"Temporary contributor branches: {len(doomed_branches)} to delete")
        for branch in doomed_branches:
            print(f"  DELETE branch {branch}")
        if dry_run:
            print("dry run: no contributor branches deleted")
        else:
            for branch in doomed_branches:
                gh_delete(f"/repos/{repo}/git/refs/heads/{urllib.parse.quote(branch, safe='')}", gh_token)
                print(f"  deleted branch {branch}")

    print("cleanup complete")
    return 0


if __name__ == "__main__":
    sys.exit(main())

"""Register a published package or notify the existing VPM listing workflow."""

import argparse
import base64
import json
import os
import re
import subprocess
from urllib.parse import quote

from build_release import VERSION


class ApiError(RuntimeError):
    def __init__(self, status, endpoint):
        self.status = status
        super().__init__(f"GitHub request failed for {endpoint} (HTTP {status})")


class GitHub:
    def __init__(self, token=None):
        self.token = token

    def request(self, method, endpoint, data=None):
        arguments = ["gh", "api", endpoint, "--method", method]
        if data is not None:
            arguments.extend(["--input", "-"])
        result = subprocess.run(
            arguments, input=json.dumps(data) if data is not None else None,
            text=True, capture_output=True, check=False,
            env={**os.environ, "GH_TOKEN": self.token} if self.token else None,
        )
        if result.returncode:
            match = re.search(r"HTTP (\d+)", result.stderr)
            raise ApiError(int(match.group(1)) if match else None, endpoint)
        return json.loads(result.stdout) if result.stdout.strip() else None


def update_listing(api, listing_repository, package_repository, version, package_name, check=False, package_api=None):
    for repository in (listing_repository, package_repository):
        if not re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", repository):
            raise ValueError("Repository names must have the form owner/name")
    if not VERSION.fullmatch(version):
        raise ValueError("A valid package version is required")
    if not re.fullmatch(r"[a-z0-9][a-z0-9.-]*", package_name):
        raise ValueError("A valid package name is required")
    if not check:
        release = (package_api or api).request(
            "GET", f"repos/{package_repository}/releases/tags/{quote(version, safe='')}"
        )
        zip_name = f"{package_name}-{version}.zip"
        if release.get("draft") or not any(
            asset.get("name") == zip_name and asset.get("state") == "uploaded"
            for asset in release.get("assets", [])
        ):
            raise ValueError("Publish the package ZIP before registering or updating VPM")

    repository = api.request("GET", f"repos/{listing_repository}")
    branch = repository["default_branch"]
    endpoint = f"repos/{listing_repository}/contents/source.json"
    for attempt in range(3):
        file = api.request("GET", f"{endpoint}?ref={quote(branch, safe='')}")
        source = json.loads(base64.b64decode(file["content"]))
        repositories = source.get("githubRepos")
        if not isinstance(repositories, list) or not all(isinstance(item, str) for item in repositories):
            raise ValueError("VPM source.json must contain a githubRepos list")
        registered = package_repository.casefold() in {item.casefold() for item in repositories}
        if check:
            return {"registered": registered, "would_register": not registered, "notification": "none"}
        if registered:
            api.request("POST", f"repos/{listing_repository}/dispatches", {
                "event_type": "update-listing", "client_payload": {},
            })
            return {"registered": True, "added": False, "notification": "repository_dispatch"}

        repositories.append(package_repository)
        content = json.dumps(source, indent=4, ensure_ascii=False) + "\n"
        try:
            api.request("PUT", endpoint, {
                "message": f"Add {package_repository} package source",
                "branch": branch, "sha": file["sha"],
                "content": base64.b64encode(content.encode("utf-8")).decode("ascii"),
            })
        except ApiError as error:
            if error.status != 409 or attempt == 2:
                raise
            continue
        # The existing listing workflow builds on pushes to its default branch.
        return {"registered": True, "added": True, "notification": "push"}
    raise RuntimeError("Could not update the VPM source")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--listing-repository", required=True)
    parser.add_argument("--package-repository", required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--package-name", required=True)
    parser.add_argument("--check", action="store_true")
    arguments = parser.parse_args()
    package_token = os.environ.get("PACKAGE_GITHUB_TOKEN")
    if not arguments.check and not package_token:
        parser.error("PACKAGE_GITHUB_TOKEN is required to verify the package release")
    result = update_listing(
        GitHub(), arguments.listing_repository, arguments.package_repository,
        arguments.version, arguments.package_name, arguments.check,
        GitHub(package_token) if package_token else None,
    )
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()

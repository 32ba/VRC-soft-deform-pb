import base64
import copy
import json
import unittest
from unittest.mock import Mock

from update_vpm import ApiError, update_listing


PACKAGE_REPOSITORY = "32ba/VRC-soft-deform-pb"
LISTING_REPOSITORY = "32ba/vpm.32ba.net"
PACKAGE_NAME = "net.32ba.soft-deform-pb"


class ListingAPI:
    def __init__(self, registered=False):
        self.source = {"id": "net.32ba.vpm", "name": "32ba VPM repository", "githubRepos": ["32ba/existing-package"]}
        if registered:
            self.source["githubRepos"].append(PACKAGE_REPOSITORY)
        self.sha = "initial"
        self.writes = []
        self.notifications = []
        self.conflict_once = False
        self.release = {"draft": False, "assets": [{"name": f"{PACKAGE_NAME}-0.0.1.zip", "state": "uploaded"}]}

    def request(self, method, endpoint, data=None):
        if method == "GET" and "/releases/tags/" in endpoint:
            return copy.deepcopy(self.release)
        if method == "GET" and endpoint == f"repos/{LISTING_REPOSITORY}":
            return {"default_branch": "main"}
        if method == "GET" and "/contents/source.json?ref=main" in endpoint:
            return {"sha": self.sha, "content": base64.b64encode(json.dumps(self.source).encode()).decode()}
        if method == "PUT":
            if self.conflict_once:
                self.conflict_once = False
                self.source["githubRepos"].append("32ba/concurrent-package")
                self.sha = "updated"
                raise ApiError(409, endpoint)
            assert data["sha"] == self.sha and data["branch"] == "main"
            self.writes.append(data)
            self.source = json.loads(base64.b64decode(data["content"]))
            return {"commit": {"sha": "new"}}
        if method == "POST":
            self.notifications.append(data)
            return None
        raise AssertionError(f"Unexpected API call: {method} {endpoint}")


class VPMUpdateTests(unittest.TestCase):
    def run_update(self, api, check=False):
        return update_listing(api, LISTING_REPOSITORY, PACKAGE_REPOSITORY, "0.0.1", PACKAGE_NAME, check)

    def test_first_release_registers_once_and_preserves_listing_metadata(self):
        api = ListingAPI()
        result = self.run_update(api)
        self.assertEqual(["32ba/existing-package", PACKAGE_REPOSITORY], api.source["githubRepos"])
        self.assertEqual("net.32ba.vpm", api.source["id"])
        self.assertEqual(1, len(api.writes))
        self.assertFalse(api.notifications)
        self.assertEqual("push", result["notification"])

    def test_existing_registration_only_dispatches_an_update(self):
        api = ListingAPI(registered=True)
        result = self.run_update(api)
        self.assertFalse(api.writes)
        self.assertEqual([{"event_type": "update-listing", "client_payload": {}}], api.notifications)
        self.assertEqual("repository_dispatch", result["notification"])

    def test_package_verification_uses_its_own_scoped_client(self):
        api = ListingAPI(registered=True)
        package_api = Mock()
        package_api.request.return_value = copy.deepcopy(api.release)
        api.release = {"draft": True, "assets": []}
        update_listing(
            api, LISTING_REPOSITORY, PACKAGE_REPOSITORY, "0.0.1", PACKAGE_NAME,
            package_api=package_api,
        )
        package_api.request.assert_called_once_with(
            "GET", f"repos/{PACKAGE_REPOSITORY}/releases/tags/0.0.1",
        )
        self.assertEqual(1, len(api.notifications))

    def test_dry_run_has_no_writes_or_notifications(self):
        api = ListingAPI()
        api.release = {"draft": True, "assets": []}
        result = self.run_update(api, check=True)
        self.assertTrue(result["would_register"])
        self.assertFalse(api.writes or api.notifications)

    def test_concurrent_source_edit_is_preserved(self):
        api = ListingAPI()
        api.conflict_once = True
        self.run_update(api)
        self.assertEqual(
            ["32ba/existing-package", "32ba/concurrent-package", PACKAGE_REPOSITORY],
            api.source["githubRepos"],
        )
        self.assertEqual(1, len(api.writes))

    def test_unpublished_or_incomplete_release_cannot_change_listing(self):
        for release in ({"draft": True, "assets": []}, {"draft": False, "assets": []}):
            api = ListingAPI()
            api.release = release
            with self.assertRaisesRegex(ValueError, "Publish the package ZIP"):
                self.run_update(api)
            self.assertFalse(api.writes or api.notifications)


if __name__ == "__main__":
    unittest.main()

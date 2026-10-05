import copy
import json
from pathlib import Path
import subprocess
import tarfile
import tempfile
import unittest
import zipfile

from build_release import build, release_info


class ReleasePackagingTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name) / "package"
        self.root.mkdir()
        self.manifest = {
            "name": "net.32ba.soft-deform-pb", "displayName": "Soft Deform PB",
            "version": "0.0.1", "author": {"name": "32ba", "email": "dev@32ba.net"},
            "vpmDependencies": {"com.vrchat.avatars": ">=3.10.4 <4.0.0"},
        }
        contents = {
            "package.json": json.dumps(self.manifest),
            "README.md": "# Soft Deform PB\n", "LICENSE": "MIT\n",
            "Editor/Setup.cs": "// editor\n", "Runtime/Setup.cs": "// runtime\n",
            "Docs~/motion-tuning.md": "# Tuning\n",
            "Tests/Editor/Test.cs": "// development only\n",
            ".github/workflows/check.yml": "name: Check\n",
            "Docs~/Validation/record.json": "{}\n",
        }
        for relative, text in contents.items():
            path = self.root / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(text, encoding="utf-8", newline="\n")
        for index, relative in enumerate((
            "Editor", "Runtime", "package.json", "README.md", "LICENSE",
            "Editor/Setup.cs", "Runtime/Setup.cs",
        ), 1):
            (self.root / (relative + ".meta")).write_text(
                f"fileFormatVersion: 2\nguid: {index:032x}\n", encoding="utf-8", newline="\r\n"
            )
        subprocess.run(["git", "init", "-q"], cwd=self.root, check=True)
        subprocess.run(["git", "add", "-A"], cwd=self.root, check=True, capture_output=True)
        self.output = Path(self.temporary.name) / "output"

    def make_release(self):
        return build(self.root, self.output, "32ba/VRC-soft-deform-pb")

    def test_zip_has_root_manifest_and_excludes_development_files(self):
        info = self.make_release()
        with zipfile.ZipFile(self.output / info["zip_file"]) as archive:
            names = archive.namelist()
            manifest = json.loads(archive.read("package.json"))
            self.assertIn("Runtime/Setup.cs.meta", names)
            self.assertIn("Docs~/motion-tuning.md", names)
            self.assertNotIn("Tests/Editor/Test.cs", names)
            self.assertNotIn(".github/workflows/check.yml", names)
            self.assertNotIn("Docs~/Validation/record.json", names)
            self.assertEqual(info["url"], manifest["url"])
            self.assertEqual(self.manifest["vpmDependencies"], manifest["vpmDependencies"])
            self.assertNotIn("zipSHA256", manifest)

    def test_unitypackage_preserves_guids_and_uses_package_paths(self):
        info = self.make_release()
        with tarfile.open(self.output / info["unitypackage_file"], "r:gz") as archive:
            guid = f"{6:032x}"
            self.assertEqual(
                b"Packages/net.32ba.soft-deform-pb/Editor/Setup.cs",
                archive.extractfile(f"{guid}/pathname").read(),
            )
            self.assertEqual(
                (self.root / "Editor/Setup.cs.meta").read_bytes(),
                archive.extractfile(f"{guid}/asset.meta").read(),
            )
            self.assertEqual(b"// editor\n", archive.extractfile(f"{guid}/asset").read())
            package_guid = f"{3:032x}"
            manifest = json.load(archive.extractfile(f"{package_guid}/asset"))
            self.assertEqual(info["url"], manifest["url"])

    def test_archives_are_reproducible_and_checksums_match(self):
        import hashlib
        info = self.make_release()
        second = Path(self.temporary.name) / "second"
        build(self.root, second, "32ba/VRC-soft-deform-pb")
        for filename in (info["zip_file"], info["unitypackage_file"], "package.json"):
            self.assertEqual((self.output / filename).read_bytes(), (second / filename).read_bytes())
        for line in (self.output / "SHA256SUMS").read_text().splitlines():
            checksum, filename = line.split("  ")
            self.assertEqual(checksum, hashlib.sha256((self.output / filename).read_bytes()).hexdigest())

    def test_missing_unity_metadata_is_rejected(self):
        subprocess.run(["git", "rm", "-f", "-q", "Runtime/Setup.cs.meta"], cwd=self.root, check=True)
        with self.assertRaisesRegex(ValueError, "Unity metadata is missing"):
            self.make_release()

    def test_duplicate_guids_are_rejected(self):
        (self.root / "Runtime/Setup.cs.meta").write_bytes((self.root / "Editor/Setup.cs.meta").read_bytes())
        with self.assertRaisesRegex(ValueError, "Duplicate Unity GUID"):
            self.make_release()

    def test_invalid_versions_and_missing_author_email_are_rejected(self):
        for version in ("01.0.0", "0.0.1-alpha.01", "0.0.1\n", "v0.0.1"):
            manifest = copy.deepcopy(self.manifest)
            manifest["version"] = version
            with self.subTest(version=version), self.assertRaises(ValueError):
                release_info(manifest, "32ba/VRC-soft-deform-pb")
        self.manifest["author"].pop("email")
        with self.assertRaisesRegex(ValueError, "name and email"):
            release_info(self.manifest, "32ba/VRC-soft-deform-pb")

    def test_prerelease_flag_uses_prerelease_section_only(self):
        self.manifest["version"] = "0.0.1-alpha.1+build"
        self.assertTrue(release_info(self.manifest, "32ba/VRC-soft-deform-pb")["prerelease"])
        self.manifest["version"] = "0.0.1+build-with-hyphen"
        info = release_info(self.manifest, "32ba/VRC-soft-deform-pb")
        self.assertFalse(info["prerelease"])
        self.assertIn("%2B", info["url"])


if __name__ == "__main__":
    unittest.main()

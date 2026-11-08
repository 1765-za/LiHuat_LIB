#!/usr/bin/env python3
"""Static + real MSBuild fingerprint tests; never compile game-reference fixtures."""
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "Source"
DOTNET = shutil.which("dotnet")


class ProjectContract(unittest.TestCase):
    def test_exact_references_are_external_and_not_copied(self):
        project = ET.parse(SOURCE / "lihuat_lib.csproj").getroot()
        references = project.findall("./ItemGroup/Reference")
        self.assertEqual({r.attrib["Include"] for r in references},
                         {"Assembly-CSharp", "UnityStubs", "Waylith", "0Harmony"})
        self.assertTrue(all(r.findtext("Private") == "false" for r in references))
        self.assertEqual(project.findtext("./PropertyGroup/TargetFramework"), "net10.0")
        for filename in ("lihuat_lib.csproj", "AnalysisPolicy.targets"):
            tree = ET.parse(SOURCE / filename).getroot()
            self.assertEqual(tree.findall(".//Exec"), [])
        self.assertIn("native ABI 11 unvalidated", (SOURCE / "lihuat_lib.csproj").read_text())

    def test_all_reference_bytes_are_fingerprinted(self):
        tree = ET.parse(SOURCE / "AnalysisPolicy.targets").getroot()
        names = {x.findtext("LogicalName") for x in tree.findall("./ItemGroup/AnalysisPolicyReference")}
        self.assertEqual(names, {"references/" + x for x in
                                ["Assembly-CSharp.dll", "UnityStubs.dll", "Waylith.dll", "0Harmony.dll"]})
        task = tree.find("./Target[@Name='FingerprintAnalysisPolicy']")
        self.assertEqual(task.attrib["DependsOnTargets"], "ValidateManagedReferences")
        self.assertEqual(task.attrib["BeforeTargets"], "GetAssemblyAttributes")


@unittest.skipUnless(DOTNET, "requires .NET 10 SDK; static checks still run")
class MSBuildFingerprint(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="lihuat metadata ")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name) / "first checkout"
        self.source = self.root / "Source"
        (self.source / "Mechanisms").mkdir(parents=True)
        for name in ["lihuat_lib.csproj", "AnalysisPolicy.targets"]:
            shutil.copyfile(SOURCE / name, self.source / name)
        shutil.copyfile(SOURCE / "Mechanisms/LifeAnalysisDiskCache.cs",
                        self.source / "Mechanisms/LifeAnalysisDiskCache.cs")
        self.refs = self.root / "external refs"
        self.refs.mkdir()
        # Opaque byte fixtures ONLY for the hash target, not replacement assemblies.
        # No compilation or assembly resolution is attempted in these tests.
        for name in ["Assembly-CSharp.dll", "UnityStubs.dll", "Waylith.dll", "0Harmony.dll"]:
            (self.refs / name).write_bytes(b"fingerprint-only fixture: " + name.encode())

    def run_target(self, root=None, extra=(), succeeds=True, dirs=True):
        root = root or self.root
        command = [DOTNET, "msbuild", str(root / "Source/lihuat_lib.csproj"),
                   "-nologo", "-verbosity:quiet", "-t:FingerprintAnalysisPolicy",
                   "-getProperty:LifeAnalysisPolicyDigest"]
        if dirs:
            command += ["-p:RimWorldManaged=" + str(root / "external refs"),
                        "-p:HarmonyDir=" + str(root / "external refs")]
        command += list(extra)
        env = dict(os.environ)
        env.pop("LIHUAT_GAME_MANAGED", None)
        env.pop("LIHUAT_HARMONY_DIR", None)
        result = subprocess.run(command, text=True, capture_output=True, env=env)
        output = result.stdout + result.stderr
        if succeeds:
            self.assertEqual(result.returncode, 0, output)
            digest = result.stdout.strip()
            self.assertRegex(digest, r"^[a-f0-9]{64}$")
            return digest
        self.assertNotEqual(result.returncode, 0, output)
        return output

    def test_reproducible_after_repeat_and_relocation(self):
        first = self.run_target()
        self.assertEqual(first, self.run_target())
        other = Path(self.temp.name) / "relocated checkout"
        shutil.copytree(self.root, other)
        self.assertEqual(first, self.run_target(other))
        manifests = list(self.root.rglob("analysis-policy.txt"))
        self.assertEqual(len(manifests), 1)
        text = manifests[0].read_text()
        self.assertNotIn(str(self.root), text)
        self.assertIn("file:references/Waylith.dll=", text)

    def test_each_reference_and_source_change_invalidates(self):
        baseline = self.run_target()
        for path in sorted(self.refs.iterdir()) + [self.source / "Mechanisms/LifeAnalysisDiskCache.cs"]:
            original = path.read_bytes()
            path.write_bytes(original + b"\n// changed\n")
            self.assertNotEqual(baseline, self.run_target(), str(path))
            path.write_bytes(original)
        self.assertEqual(baseline, self.run_target())
        self.assertNotEqual(baseline, self.run_target(extra=["-p:Optimize=false"]))
        self.assertNotEqual(baseline, self.run_target(extra=["-p:DefineConstants=POLICY_TEST"]))

    def test_missing_reference_is_actionable(self):
        for path in sorted(self.refs.iterdir()):
            content = path.read_bytes()
            path.unlink()
            output = self.run_target(succeeds=False)
            self.assertIn("Missing required managed reference", output)
            self.assertIn(path.name, output)
            path.write_bytes(content)

    def test_missing_directories_and_core_source_fail(self):
        self.assertIn("LIHUAT_GAME_MANAGED", self.run_target(dirs=False, succeeds=False))
        (self.source / "Mechanisms/LifeAnalysisDiskCache.cs").unlink()
        self.assertIn("Missing required analysis source", self.run_target(succeeds=False))

    def test_new_source_invalidates_and_creation_order_is_irrelevant(self):
        baseline = self.run_target()
        (self.source / "Z.cs").write_text("// z\n")
        (self.source / "A.cs").write_text("// a\n")
        added = self.run_target()
        self.assertNotEqual(baseline, added)
        (self.source / "Z.cs").unlink()
        (self.source / "Z.cs").write_text("// z\n")
        self.assertEqual(added, self.run_target())


if __name__ == "__main__":
    unittest.main(verbosity=2)

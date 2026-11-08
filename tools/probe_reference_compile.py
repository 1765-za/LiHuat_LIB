#!/usr/bin/env python3
"""Bounded compile probe. Never deploys, runs game code, or certifies runtime compatibility."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[1]
SDK = "10.0.100"
PACKAGES = (
    {"id": "Krafs.Rimworld.Ref", "version": "1.7.4984-unstable", "prefix": "ref/net10.0/",
     "sha256": "e9754db5577f3699aafd4423e73842eaa715442360f08f978bb9e68d7407de82",
     "expected_count": 67, "destination": "game-references"},
    {"id": "Lib.Harmony", "version": "3.0.0-rc.3", "prefix": "lib/net10.0/",
     "sha256": "08a9e59119bae8d4b05d395db5b5be2cf4867900f7159a336840f8ec27d71193",
     "expected_count": 1, "destination": "harmony-reference"},
)


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def acquire(package, work):
    identity, version = package["id"].lower(), package["version"]
    url = f"https://api.nuget.org/v3-flatcontainer/{identity}/{version}/{identity}.{version}.nupkg"
    archive = work / "downloads" / f"{identity}.{version}.nupkg"
    archive.parent.mkdir(parents=True, exist_ok=True)
    if not archive.exists():
        temporary = archive.with_suffix(".partial")
        with urllib.request.urlopen(url, timeout=60) as response, temporary.open("wb") as output:
            shutil.copyfileobj(response, output)
        if digest(temporary) != package["sha256"]:
            raise RuntimeError("Checksum mismatch: " + package["id"])
        temporary.replace(archive)
    if digest(archive) != package["sha256"]:
        raise RuntimeError("Cached package checksum mismatch: " + str(archive))
    destination = work / package["destination"]
    destination.mkdir(parents=True, exist_ok=True)
    selected = []
    with zipfile.ZipFile(archive) as bundle:
        for info in bundle.infolist():
            if not info.filename.startswith(package["prefix"]):
                continue
            filename = info.filename[len(package["prefix"]):]
            if not filename.endswith(".dll") or "/" in filename or "\\" in filename:
                continue
            selected.append((info, filename))
        if len(selected) != package["expected_count"]:
            raise RuntimeError("Unexpected framework asset set: " + package["id"])
        expected_names = {filename for _, filename in selected}
        unexpected = {entry.name for entry in destination.iterdir()} - expected_names
        if unexpected:
            raise RuntimeError("Unexpected cached reference assets; use a fresh work directory: " + str(destination))
        for info, filename in selected:
            # Extract selected assemblies only, never NuGet targets, tasks or scripts.
            (destination / filename).write_bytes(bundle.read(info))
    return destination, {"id": package["id"], "version": version, "url": url,
                         "sha256": package["sha256"], "selected_assemblies": len(selected)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default="dotnet", help="Path to a host with SDK 10.0.100 installed")
    parser.add_argument("--work-dir", type=Path, default=ROOT / "build/reference-compile")
    args = parser.parse_args()
    dotnet = shutil.which(args.dotnet)
    if not dotnet:
        raise RuntimeError("Install the official .NET SDK 10.0.100 or pass --dotnet /path/to/dotnet")
    work = args.work_dir.resolve()
    if work == ROOT or work == ROOT / "Source":
        raise RuntimeError("Use a separate probe work directory")
    work.mkdir(parents=True, exist_ok=True)
    (work / "global.json").write_text(json.dumps({"sdk": {"version": SDK, "rollForward": "disable"}}) + "\n")
    env = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1",
               DOTNET_GENERATE_ASPNET_CERTIFICATE="false", MSBUILDDISABLENODEREUSE="1",
               DOTNET_CLI_HOME=str(work / "cli-home"), NUGET_PACKAGES=str(work / "nuget-cache"))
    version = subprocess.check_output([dotnet, "--version"], cwd=work, env=env, text=True).strip()
    if version != SDK:
        raise RuntimeError(f"Probe requires SDK {SDK}; got {version}")
    paths, packages = [], []
    for package in PACKAGES:
        path, metadata = acquire(package, work)
        paths.append(path)
        packages.append(metadata)
    for filename in ("Assembly-CSharp.dll", "UnityStubs.dll", "Waylith.dll"):
        if not (paths[0] / filename).is_file():
            raise RuntimeError("Missing game reference: " + filename)
    if not (paths[1] / "0Harmony.dll").is_file():
        raise RuntimeError("Missing Harmony asset")
    common = [str(ROOT / "Source/lihuat_lib.csproj"),
              "-p:UseSharedCompilation=false", "-p:RimWorldManaged=" + str(paths[0]),
              "-p:HarmonyDir=" + str(paths[1]), "-p:OutputPath=" + str(work / "output") + "/",
              "-p:BaseIntermediateOutputPath=" + str(work / "obj") + "/"]
    command = [dotnet, "build", *common, "-c", "Release", "--nologo", "-v:minimal"]
    completed = subprocess.run(command, cwd=work, env=env, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    (work / "build.log").write_text(completed.stdout)
    print(completed.stdout, end="")
    items = subprocess.check_output([dotnet, "msbuild", *common, "-nologo", "-getItem:Compile"], cwd=work, env=env, text=True)
    count = len(json.loads(items)["Items"]["Compile"])
    source = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    source_dirty = bool(subprocess.check_output(["git", "status", "--porcelain", "--", "Source"], cwd=ROOT, text=True).strip())
    output = work / "output/lihuat_lib.dll"
    report = {"scope": "provisional compile only; no execution, deployment or native ABI certification",
              "source_commit": source, "source_tree_dirty": source_dirty, "sdk": version, "packages": packages, "compile_items": count,
              "exit_code": completed.returncode, "output_sha256": digest(output) if completed.returncode == 0 else None,
              "unverified": ["target game/Harmony versions", "Ludeon customized runtime APIs and private members",
                             "reflection/UnsafeAccessor contracts", "full native ABI 11", "game behavior and defenses"]}
    (work / "report.json").write_text(json.dumps(report, indent=2) + "\n")
    print(f"Probe report: {work / 'report.json'} ({count} source items)")
    print("NOT a game-ready build: do not deploy the output or substitute these packages for the target host.")
    return completed.returncode


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, ValueError, RuntimeError, subprocess.CalledProcessError) as error:
        print("Reference compile probe failed: " + str(error), file=sys.stderr)
        sys.exit(1)

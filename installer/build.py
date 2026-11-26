"""Publish framework-dependent TraySuite packages and assemble release manifests."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import urllib.request
import zipfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
PUBLIC = ROOT / "public"
CATALOG = PUBLIC / "Hub" / "catalog.json"
ARTIFACTS = ROOT / "artifacts"
RUNTIMES = ("win-x64", "win-arm64")
SETTINGS = {
    "TRAYSUITE_STORE_ID": "TraySuiteStoreId",
    "TRAYSUITE_PRODUCT_ID": "TraySuiteProductId",
    "TRAYSUITE_CHECKOUT_URL": "TraySuiteCheckoutUrl",
    "TRAYSUITE_DOWNLOADS_URL": "TraySuiteDownloadsUrl",
}
RELEASES = "https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json"


def load(path):
    return json.loads(path.read_text(encoding="utf-8"))


def save(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def digest(path, algorithm="sha256"):
    with path.open("rb") as source:
        return hashlib.file_digest(source, algorithm).hexdigest()


def run(*command):
    subprocess.run(command, cwd=ROOT, check=True)


def version():
    return ET.parse(PUBLIC / "Directory.Build.props").findtext("./PropertyGroup/Version")


def publish(project, directory, runtime, app_version, configuration):
    dotnet = os.environ.get("DOTNET_EXE", "dotnet")
    settings = [
        f"-p:{property_name}={os.environ[environment]}"
        for environment, property_name in SETTINGS.items()
        if os.environ.get(environment)
    ]
    if os.environ.get("TRAYSUITE_OFFLINE_BUILD") == "1":
        settings += ["-p:NuGetAudit=false"]
    run(dotnet, "publish", str(project), "-c", configuration, "-r", runtime,
        "--self-contained", "false", "-p:PublishSingleFile=false", "-p:DebugType=none",
        f"-p:Version={app_version}", "-warnaserror", "-o", str(directory), *settings)
    app = project.stem
    config = load(directory / f"{app}.runtimeconfig.json")["runtimeOptions"]
    frameworks = config.get("frameworks", []) + ([config["framework"]] if "framework" in config else [])
    if not (directory / f"{app}.exe").is_file() or not any(
        item["name"] == "Microsoft.WindowsDesktop.App" and item["version"].startswith("10.0.") for item in frameworks
    ) or (directory / "coreclr.dll").exists():
        raise ValueError(f"Incorrect framework-dependent payload: {app} ({runtime})")

    if project.is_relative_to(PUBLIC):
        shutil.copyfile(PUBLIC / "LICENSE", directory / "LICENSE")
    else:
        licenses = directory / "licenses"
        licenses.mkdir(exist_ok=True)
        shutil.copyfile(PUBLIC / "LICENSE", licenses / "Suite.Core-MIT.txt")
    add_package_notices(project, directory)


def add_package_notices(project, directory):
    assets = load(project.parent / "obj" / "project.assets.json")
    for name, library in assets["libraries"].items():
        if library["type"] != "package":
            continue
        for cache in assets["packageFolders"]:
            folder = Path(cache) / library["path"]
            if not folder.is_dir():
                continue
            notices = directory / "licenses"
            package = name.split("/")[0]
            for path in folder.iterdir():
                if path.is_file() and path.stem.lower() in {"license", "thirdpartynotices", "third-party-notices"}:
                    notices.mkdir(exist_ok=True)
                    shutil.copyfile(path, notices / f"{package}-{path.name}")
            break


def zip_package(directory, target):
    with zipfile.ZipFile(target, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for path in sorted(directory.rglob("*")):
            if path.is_file():
                archive.write(path, path.relative_to(directory).as_posix())


def build(scope, runtime, app_version, no_setup, private_base, configuration):
    catalog = load(CATALOG)
    missing = [app["id"] for app in catalog["apps"] if app["tier"] == ("free" if scope == "public" else "pro")
               and not (ROOT / scope / "Apps" / app["id"] / f"{app['id']}.csproj").is_file()]
    if missing:
        raise FileNotFoundError(f"Missing {scope} projects: {', '.join(missing)}")
    output = ARTIFACTS / scope / runtime
    output.mkdir(parents=True, exist_ok=True)
    manifest = {"schema_version": catalog["schema_version"], "apps": []}
    with tempfile.TemporaryDirectory(prefix="traysuite-") as temporary:
        work = Path(temporary)
        for app in catalog["apps"]:
            tier = app["tier"]
            if scope == "pro" and tier != "pro":
                continue
            app = app.copy()
            name = app["id"]
            filename = f"{name}-{runtime}.zip"
            if scope == "public" and tier == "pro":
                package = {"version": app_version, "size_bytes": 0, "sha256": None, "download_url": ""}
            else:
                project = ROOT / scope / "Apps" / name / f"{name}.csproj"
                publish(project, work / name, runtime, app_version, configuration)
                target = output / filename
                zip_package(work / name, target)
                url = f"{private_base.rstrip('/')}/{filename}" if private_base else filename
                package = {"version": app_version, "size_bytes": target.stat().st_size,
                           "sha256": digest(target), "download_url": url}
            app["packages"] = {runtime: package}
            manifest["apps"].append(app)

        if scope == "public":
            hub = work / "Hub"
            publish(PUBLIC / "Hub" / "TraySuite.Hub.csproj", hub, runtime, app_version, configuration)
            if no_setup:
                portable = output / "Hub"
                if portable.is_dir():
                    shutil.rmtree(portable)
                shutil.copytree(hub, portable)
            else:
                build_setup(hub, output, runtime, app_version, work)
    save(output / f"manifest-{runtime}.json", manifest)
    shutil.copyfile(output / f"manifest-{runtime}.json",
                    output / ("manifest.json" if scope == "public" else "manifest-pro.json"))


def build_setup(hub, output, runtime, app_version, work):
    if os.name != "nt":
        raise RuntimeError("Inno Setup needs Windows. Use --no-setup to cross-publish the apps.")
    vs_program_files = Path(os.environ.get("ProgramFiles(x86)", "C:/Program Files (x86)"))
    compiler = os.environ.get("ISCC_EXE") or shutil.which("ISCC.exe") or str(vs_program_files / "Inno Setup 6" / "ISCC.exe")
    with urllib.request.urlopen(RELEASES, timeout=30) as source:
        metadata = json.load(source)
    release = next(item for item in metadata["releases"] if item["release-version"] == metadata["latest-release"])
    desktop = next(item for item in release["windowsdesktop"]["files"]
                   if item["rid"] == runtime and item["name"].endswith(".exe"))
    executable = work / "desktop-runtime.exe"
    with urllib.request.urlopen(desktop["url"], timeout=90) as source, executable.open("wb") as destination:
        shutil.copyfileobj(source, destination)
    if digest(executable, "sha512") != desktop["hash"].lower():
        raise ValueError("Microsoft Desktop Runtime SHA-512 does not match the release metadata.")
    run(compiler, f"/DAppVersion={app_version}", f"/DAppArchitecture={runtime[4:]}",
        f"/DAppFiles={hub}", f"/DRuntimeUrl={desktop['url']}", f"/DRuntimeSha256={digest(executable)}",
        f"/O{output}", str(ROOT / "installer" / "TraySuite.iss"))


def merge(scope, inputs, output, pro_metadata):
    named = {}
    for runtime in RUNTIMES:
        fragment = load(inputs / runtime / f"manifest-{runtime}.json")
        if fragment["schema_version"] != 1:
            raise ValueError("Unsupported manifest schema.")
        for app in fragment["apps"]:
            key = app["id"]
            package = app["packages"][runtime]
            stem = {name: value for name, value in app.items() if name != "packages"}
            current = named.setdefault(key, {**stem, "packages": {}})
            if any(current[name] != value for name, value in stem.items()):
                raise ValueError(f"Inconsistent metadata for {key}.")
            if scope == "public" and app["tier"] == "pro" and (package["download_url"] or package["sha256"]):
                raise ValueError(f"Private metadata leaked for {key}.")
            if scope == "pro" and app["tier"] != "pro":
                raise ValueError(f"Unexpected free app in private manifest: {key}.")
            filename = inputs / runtime / f"{key}-{runtime}.zip"
            if package["download_url"] and (filename.stat().st_size != package["size_bytes"]
                                            or digest(filename) != package["sha256"]):
                raise ValueError(f"Invalid manifest entry for {filename}.")
            current["packages"][runtime] = package
    expected = {app["id"] for app in load(CATALOG)["apps"] if scope == "public" or app["tier"] == "pro"}
    if set(named) != expected or any(
        set(item["packages"]) != set(RUNTIMES) or len({package["version"] for package in item["packages"].values()}) != 1
        for item in named.values()
    ):
        raise ValueError("Release manifest is missing apps, architectures or matching versions.")
    if pro_metadata:
        private = load(pro_metadata)
        if private["schema_version"] != 1:
            raise ValueError("Unsupported Pro manifest schema.")
        for app in private["apps"]:
            if app["id"] not in named or named[app["id"]]["tier"] != "pro" or app["tier"] != "pro":
                raise ValueError(f"Unexpected Pro metadata for {app['id']}.")
            for runtime in RUNTIMES:
                package = app["packages"][runtime]
                if type(package["size_bytes"]) is not int or package["size_bytes"] <= 0 \
                        or not re.fullmatch(r"[0-9]+(?:\.[0-9]+){1,3}", package["version"]):
                    raise ValueError(f"Invalid Pro metadata for {app['id']} ({runtime}).")
                public_package = named[app["id"]]["packages"][runtime]
                public_package.update(version=package["version"], size_bytes=package["size_bytes"])
    name = "manifest.json" if scope == "public" else "manifest-pro.json"
    save(output / name, {"schema_version": 1, "apps": list(named.values())})


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    tasks = parser.add_subparsers(dest="task", required=True)
    build_parser = tasks.add_parser("build")
    build_parser.add_argument("--scope", required=True, choices=("public", "pro"))
    build_parser.add_argument("--runtime", required=True, choices=RUNTIMES)
    build_parser.add_argument("--version", default=version())
    build_parser.add_argument("--configuration", default="Release", choices=("Release", "Debug"))
    build_parser.add_argument("--no-setup", action="store_true")
    build_parser.add_argument("--private-base", default=None)
    merge_parser = tasks.add_parser("merge")
    merge_parser.add_argument("--scope", required=True, choices=("public", "pro"))
    merge_parser.add_argument("--inputs", type=Path, required=True)
    merge_parser.add_argument("--output", type=Path, required=True)
    merge_parser.add_argument("--pro-metadata", type=Path, help="Copy only the Pro version and size into the public manifest.")
    options = parser.parse_args()

    if options.task == "build":
        if not re.fullmatch(r"[0-9]+(?:\.[0-9]+){1,3}", options.version):
            parser.error("--version must contain two to four numeric components.")
        if options.private_base and (options.scope != "pro" or not options.private_base.startswith("https://")):
            parser.error("--private-base is an HTTPS address for the private build.")
        build(options.scope, options.runtime, options.version, options.no_setup, options.private_base, options.configuration)
    else:
        if options.pro_metadata and options.scope != "public":
            parser.error("--pro-metadata applies only to the public manifest.")
        merge(options.scope, options.inputs, options.output, options.pro_metadata)


if __name__ == "__main__":
    main()

#!/usr/bin/env bash
# Builds mac/artifacts/ComicRack CE (MVP).app (M2.0b): self-contained osx-arm64 publish, Info.plist, icon, ad-hoc signature
# with hardened runtime. Usage: mac/tools/make-app.sh   (run from anywhere). Needs dotnet, sips, iconutil, codesign.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
ARTIFACTS="$ROOT/mac/artifacts"
APP_NAME="ComicRack CE (MVP)"
APP="$ARTIFACTS/$APP_NAME.app"
PUBLISH="$ARTIFACTS/publish-app"
ICONSET="$ARTIFACTS/AppIcon.iconset"
EXE="ComicRack.Mac"
# Placeholder: the owner may replace it with the real id before distribution (M2.5).
BUNDLE_ID="${BUNDLE_ID:-com.comicrackce.mvp}"
VERSION="${APP_VERSION:-0.1.0}"
ICON_SRC="$ROOT/ComicRack/Resources/ComicRackApp256.png"   # existing 256 px app icon, read only

FREE_KB=$(df -k "$ROOT" | awk 'NR==2 {print $4}')
if [ "$FREE_KB" -lt 3145728 ]; then
	echo "STOP: less than 3 GB free disk." >&2
	exit 1
fi

# One bundle only: remove the previous one and the publish folder first.
rm -rf "$APP" "$PUBLISH" "$ICONSET"
mkdir -p "$ARTIFACTS"

dotnet publish "$ROOT/mac/ComicRack.Mac/ComicRack.Mac.csproj" -c Release -r osx-arm64 --self-contained \
	-p:PublishReadyToRun=true -p:DebugType=none -p:DebugSymbols=false -o "$PUBLISH"

mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$PUBLISH/." "$APP/Contents/MacOS/"
rm -rf "$PUBLISH"

# P2.6: the payload of ComicRack/Output (Languages, Scripts, Help links, DefaultLists.txt, Icons, Textures) is published by the project next to the
# entry assembly, where the Core's TRDictionary looks for Languages/. The Windows binaries of Resources/ are never part of it.
for payload in Languages/de/Default.xml Scripts/Sample.py "Help/ComicRack Online Manual.ini" DefaultLists.txt Resources/Icons Resources/Textures; do
	if [ ! -e "$APP/Contents/MacOS/$payload" ]; then
		echo "FAIL: the payload item '$payload' is missing from the bundle." >&2
		exit 1
	fi
done
if [ -e "$APP/Contents/MacOS/Resources/x64" ] || [ -e "$APP/Contents/MacOS/Resources/c44.exe" ]; then
	echo "FAIL: Windows binaries from ComicRack/Output/Resources are in the bundle." >&2
	exit 1
fi

# P4.8: the assemblies carry the names scripts use (System.Windows.Forms and System.Drawing.Common are the facades, version 10.0.0.0; ComicRack.Engine, ComicRack.Plugins, cYo.Common and
# cYo.Common.Windows are the Core), and no file of an older name is left. mac/tools/bundle-selftest.sh checks that a script reaches them.
for asm in System.Windows.Forms System.Drawing.Common System.Web.Extensions ComicRack.Engine ComicRack.Plugins cYo.Common cYo.Common.Windows; do
	if [ ! -e "$APP/Contents/MacOS/$asm.dll" ]; then
		echo "FAIL: the assembly $asm.dll is missing from the bundle." >&2
		exit 1
	fi
done
for old in cYo.Compat.Forms cYo.Compat.Drawing ComicRack.Engine.Core ComicRack.Plugins.Core cYo.Common.Core; do
	if [ -e "$APP/Contents/MacOS/$old.dll" ]; then
		echo "FAIL: $old.dll (a name from before the assembly identity mapping) is in the bundle." >&2
		exit 1
	fi
done

# P4.3: IronPython 2.7.12 cannot create a Python class on .NET 9 and later. The tool patches the copy in the bundle (idempotent), the Python standard
# library sits in Contents/MacOS/Lib (package IronPython.StdLib, found next to IronPython.dll), and the Apache-2.0 licence text goes to Contents/Resources.
dotnet build "$ROOT/mac/tools/patch-ironpython" -c Release -v q --nologo >/dev/null
dotnet "$ROOT/mac/tools/patch-ironpython/bin/Release/net10.0/patch-ironpython.dll" "$APP/Contents/MacOS/IronPython.dll"
dotnet "$ROOT/mac/tools/patch-ironpython/bin/Release/net10.0/patch-ironpython.dll" --check "$APP/Contents/MacOS/IronPython.dll" >/dev/null \
	|| { echo "FAIL: IronPython.dll in the bundle is not patched." >&2; exit 1; }
if [ ! -e "$APP/Contents/MacOS/Lib/os.py" ]; then
	echo "FAIL: the Python standard library (Contents/MacOS/Lib) is missing from the bundle." >&2
	exit 1
fi
IRONPYTHON_PKG="${NUGET_PACKAGES:-$HOME/.nuget/packages}/ironpython/2.7.12"
if [ ! -f "$IRONPYTHON_PKG/LICENSE" ]; then
	echo "FAIL: the IronPython licence text was not found at $IRONPYTHON_PKG/LICENSE." >&2
	exit 1
fi
cp "$IRONPYTHON_PKG/LICENSE" "$APP/Contents/Resources/IronPython-License.txt"

# P2.4a: the official 7-Zip console binary (ComicInfo writes, CB7 creation; DD10) sits next to the executable, where ToolLocator.SevenZip looks.
# fetch-7zz.sh does nothing when the pinned, verified copy is already in mac/artifacts/native. Its licence text goes to Contents/Resources.
"$ROOT/mac/native/fetch-7zz.sh"
cp "$ARTIFACTS/native/7zz" "$APP/Contents/MacOS/7zz"
cp "$ARTIFACTS/native/7zz-License.txt" "$APP/Contents/Resources/7zz-License.txt"
codesign --force -s - --options runtime "$APP/Contents/MacOS/7zz"

# Icon: sips resizes the 256 px source (the 512 and 1024 px entries are upscaled; use a larger source for a sharper icon).
mkdir -p "$ICONSET"
for s in 16 32 64 128 256 512 1024; do
	sips -z $s $s "$ICON_SRC" --out "$ICONSET/s$s.png" >/dev/null
done
cd "$ICONSET"
mv s16.png icon_16x16.png;     cp s32.png icon_16x16@2x.png
mv s32.png icon_32x32.png;     mv s64.png icon_32x32@2x.png
mv s128.png icon_128x128.png;  cp s256.png icon_128x128@2x.png
mv s256.png icon_256x256.png;  cp s512.png icon_256x256@2x.png
mv s512.png icon_512x512.png;  mv s1024.png icon_512x512@2x.png
cd "$ROOT"
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"
rm -rf "$ICONSET"

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>CFBundleName</key><string>$APP_NAME</string>
	<key>CFBundleDisplayName</key><string>$APP_NAME</string>
	<key>CFBundleIdentifier</key><string>$BUNDLE_ID</string>
	<key>CFBundleExecutable</key><string>$EXE</string>
	<key>CFBundleIconFile</key><string>AppIcon</string>
	<key>CFBundlePackageType</key><string>APPL</string>
	<key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
	<key>CFBundleShortVersionString</key><string>$VERSION</string>
	<key>CFBundleVersion</key><string>$VERSION</string>
	<key>LSMinimumSystemVersion</key><string>14.0</string>
	<key>NSHighResolutionCapable</key><true/>
	<key>NSPrincipalClass</key><string>NSApplication</string>
	<key>LSApplicationCategoryType</key><string>public.app-category.entertainment</string>
</dict>
</plist>
PLIST

# Ad-hoc signature, hardened runtime. --deep also signs the dylibs and 7zz next to the executable.
codesign --force --deep -s - --options runtime --entitlements "$ROOT/mac/tools/mvp.entitlements" "$APP"
codesign --verify --deep --strict "$APP"
echo "Built: $APP ($(du -sh "$APP" | cut -f1))"

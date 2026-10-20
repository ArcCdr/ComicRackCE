#!/usr/bin/env bash
# Spike S6 (P4.2): the assembly identity experiments. No window server is needed.
#   identity.sh forms   the facade with the literal name System.Windows.Forms (S6 app, --identity): versions, public key tokens, a compiled plug-in
#   identity.sh core    the real Core with the stand-ins and the shadow assemblies renamed (S4 console, unmodified bundled scripts)
#   identity.sh all
# Output: mac/artifacts/s6/identity/<variant>.txt and a summary on stdout. Exit 0 when the run itself worked (the variants record PASS/FAIL lines).
set -uo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
SPIKE="$ROOT/mac/spikes/S6-modal"
ART="$ROOT/mac/artifacts/s6"
OUT="$ART/identity"
RB="$SPIKE/Tools/bin/Release/net10.0/s6-rebind.dll"
PATCH="$ROOT/mac/spikes/S4-patch/bin/Release/net10.0/patch-ironpython.dll"
mkdir -p "$OUT"

build_tools() {
	dotnet build "$SPIKE/Tools" -c Release -v q --nologo >/dev/null || exit 1
	dotnet build "$ROOT/mac/spikes/S4-patch" -c Release -v q --nologo >/dev/null || exit 1
}

rebind() { dotnet "$RB" "$@"; }

patch_ironpython() { # <dir>
	dotnet "$PATCH" "$1/IronPython.dll" "$1/IronPython.patched.dll" >/dev/null && mv "$1/IronPython.patched.dll" "$1/IronPython.dll"
}

summarize() { # <file>
	printf '  %s: %s PASS, %s FAIL, %s INFO\n' "$(basename "$1" .txt)" "$(grep -c '^PASS' "$1")" "$(grep -c '^FAIL' "$1")" "$(grep -c '^INFO\|^RES' "$1")"
}

run_forms_variant() { # <name> <facade version> <facade token or ""> <plugin fake version> <plugin fake token>
	local name=$1 ver=$2 token=$3 pver=$4 ptoken=$5
	local dir="$ART/forms-$name"
	local spec="System.Windows.Forms=System.Windows.Forms:$ver"
	[ -n "$token" ] && spec="$spec:$token"
	rebind "$ART/app" "$dir" --rename "$spec" >/dev/null || return 1
	mkdir -p "$dir/plugins"
	# the plug-in as built against the facade, and as a .NET Framework build would reference it; both rebound to the variant like the rest
	rebind "$SPIKE/Plugin/bin/Debug/net10.0" "$ART/plugin-$name" --rename "$spec" --drop System.Windows.Forms.dll --drop System.Windows.Forms.pdb --drop cYo.Compat.Drawing.dll --drop cYo.Compat.Drawing.pdb >/dev/null || return 1
	cp "$ART/plugin-$name/S6-plugin.dll" "$dir/plugins/S6-plugin-built.dll"
	dotnet "$RB" --fake-ref "$dir/plugins/S6-plugin-built.dll" "$dir/plugins/S6-plugin-fw.dll" "System.Windows.Forms=$pver:$ptoken" >/dev/null || return 1
	{
		echo "# variant $name: facade System.Windows.Forms $ver token '${token:-none}'; plug-in reference as built and as .NET Framework $pver token $ptoken"
		"$dir/S6-modal" --identity --part references --out "$dir/out" 2>&1
		# every plug-in and every way of loading it in a process of its own: nothing has loaded the facade before
		local plugin
		for plugin in S6-plugin-built S6-plugin-fw; do
			"$dir/S6-modal" --identity --part python --out "$dir/out" --plugin "$dir/plugins/$plugin.dll" 2>&1 | grep -v '^RESULT'
			"$dir/S6-modal" --identity --part loadfrom --out "$dir/out" --plugin "$dir/plugins/$plugin.dll" 2>&1 | grep -v '^RESULT'
		done
	} >"$OUT/forms-$name.txt"
	summarize "$OUT/forms-$name.txt"
}

run_forms_hook() { # the facade is not in the app folder: AssemblyLoadContext.Default.Resolving loads it by its simple name
	local dir="$ART/forms-hook"
	rebind "$ART/app" "$dir" --drop System.Windows.Forms.dll --drop System.Windows.Forms.pdb --no-deps >/dev/null || return 1
	mkdir -p "$dir/facade" "$dir/plugins"
	cp "$ART/app/System.Windows.Forms.dll" "$dir/facade/"
	cp "$ART/forms-v4-notoken/plugins/S6-plugin-built.dll" "$ART/forms-v4-notoken/plugins/S6-plugin-fw.dll" "$dir/plugins/"
	{
		echo "# variant hook: facade System.Windows.Forms 4.0.0.0 in a folder that is not on the probing path, loaded by AssemblyLoadContext.Default.Resolving"
		"$dir/S6-modal" --identity --part references --out "$dir/out" --resolver "$dir/facade" 2>&1
		local plugin
		for plugin in S6-plugin-built S6-plugin-fw; do
			"$dir/S6-modal" --identity --part python --out "$dir/out" --resolver "$dir/facade" --plugin "$dir/plugins/$plugin.dll" 2>&1 | grep -v '^RESULT'
			"$dir/S6-modal" --identity --part loadfrom --out "$dir/out" --resolver "$dir/facade" --plugin "$dir/plugins/$plugin.dll" 2>&1 | grep -v '^RESULT'
		done
	} >"$OUT/forms-hook.txt"
	summarize "$OUT/forms-hook.txt"
}

run_forms() {
	echo "== forms: the facade under the literal name System.Windows.Forms"
	[ -x "$ART/app/S6-modal" ] || "$SPIKE/stage.sh" >/dev/null || exit 1
	dotnet build "$SPIKE/Plugin" -c Debug -v q --nologo >/dev/null || exit 1
	# name: facade version, facade token, plug-in reference version, plug-in reference token
	run_forms_variant v4-notoken 4.0.0.0 "" 4.0.0.0 b77a5c561934e089
	run_forms_variant v1-notoken 1.0.0.0 "" 4.0.0.0 b77a5c561934e089
	run_forms_variant v10-notoken 10.0.0.0 "" 4.0.0.0 b77a5c561934e089
	run_forms_variant v4-fwtoken 4.0.0.0 b77a5c561934e089 4.0.0.0 b77a5c561934e089
	run_forms_variant v4-othertoken 4.0.0.0 0123456789abcdef 4.0.0.0 b77a5c561934e089
	run_forms_hook
}

run_core_variant() { # <name> <rename args...>
	local name=$1; shift
	local dir="$ART/core-$name"
	rebind "$S4" "$dir" "$@" >/dev/null || return 1
	patch_ironpython "$dir"
	{
		echo "# variant $name: rebind $*"
		COMICRACK_SCRIPTS_DIR="$ROOT/ComicRack/Output/Scripts" dotnet "$dir/S4-ironpython.dll" "$ROOT/mac/spikes/S6-modal/scripts/s6-identity-core.py" 2>&1
	} >"$OUT/core-$name.txt"
	summarize "$OUT/core-$name.txt"
}

run_core() {
	echo "== core: the real Core and the bundled scripts, unmodified, with renamed assemblies"
	dotnet build "$ROOT/mac/spikes/S4-ironpython" -c Debug -v q --nologo >/dev/null || exit 1
	S4="$ROOT/mac/spikes/S4-ironpython/bin/Debug/net10.0/osx-arm64"
	# baseline: nothing renamed (what P4.1 measured)
	run_core_variant baseline
	# the identity candidate: stand-ins under the framework names, shadow assemblies under the original names, the real System.Drawing.Common removed
	run_core_variant renamed --rename cYo.Compat.Forms=System.Windows.Forms:4.0.0.0 --rename cYo.Compat.Drawing=System.Drawing.Common:4.0.2.0 \
		--rename ComicRack.Engine.Core=ComicRack.Engine --rename ComicRack.Plugins.Core=ComicRack.Plugins --rename cYo.Common.Core=cYo.Common --drop System.Drawing.Common.dll
	# the recommended numbers: both stand-ins at AssemblyVersion 10.0.0.0 (no strong name)
	run_core_variant renamed-v10 --rename cYo.Compat.Forms=System.Windows.Forms:10.0.0.0 --rename cYo.Compat.Drawing=System.Drawing.Common:10.0.0.0 \
		--rename ComicRack.Engine.Core=ComicRack.Engine --rename ComicRack.Plugins.Core=ComicRack.Plugins --rename cYo.Common.Core=cYo.Common --drop System.Drawing.Common.dll
	# the same with the public key token of the real System.Drawing.Common on the stand-in
	run_core_variant renamed-drawingtoken --rename cYo.Compat.Forms=System.Windows.Forms:4.0.0.0 --rename cYo.Compat.Drawing=System.Drawing.Common:4.0.2.0:cc7b13ffcd2ddd51 \
		--rename ComicRack.Engine.Core=ComicRack.Engine --rename ComicRack.Plugins.Core=ComicRack.Plugins --rename cYo.Common.Core=cYo.Common --drop System.Drawing.Common.dll
	# only the Forms stand-in renamed (the Drawing identity left to the real System.Drawing.Common)
	run_core_variant forms-only --rename cYo.Compat.Forms=System.Windows.Forms:4.0.0.0
}

build_tools
case "${1:-all}" in
	forms) run_forms ;;
	core) run_core ;;
	all) run_forms; run_core ;;
	*) echo "usage: identity.sh forms|core|all" >&2; exit 2 ;;
esac

#!/bin/sh
# Downloads the OpenAPI spec attached to a GitHub release of each repo listed
# in a JSON file, ready for openapitowiremock:
#
#   scripts/download-specs.sh repos.json specs
#   openapitowiremock -i specs
#
# repos.json is a JSON array with one object per repo, each with exactly a
# "repo" (owner/repo) and a "version" (the release tag):
#
#   [
#     { "repo": "zackspike/sales-module", "version": "v0.0.0-beta" }
#   ]
#
# The spec is the release asset named openapi*.json, openapi*.yaml or
# openapi*.yml, saved as <output-dir>/<repo>.<ext>, e.g. specs/sales-module.json.
#
# Needs only gh (logged in with "gh auth login", or GH_TOKEN set) and a POSIX shell.
set -eu

if [ $# -lt 1 ] || [ $# -gt 2 ]; then
  echo "Usage: $0 <repos.json> [output-dir (default: specs)]" >&2
  exit 2
fi
list=$1
out=${2:-specs}
tab=$(printf '\t')

if [ ! -f "$list" ]; then
  printf 'error: %s not found\n' "$list" >&2
  exit 1
fi

# Validates the whole list before anything is downloaded, without jq, and
# prints one "owner/repo<TAB>version" line per entry. On the first problem it
# prints only the reason and exits 1.
entries=$(awk '
  function fail(msg) { print msg; exit 1 }
  function ws() { while (i <= n && index(" \t\r\n", substr(buf, i, 1)) > 0) i++ }
  function at() { return substr(buf, i, 1) }
  # readstr reads the JSON string starting at the quote under i.
  function readstr(   s, c) {
    s = ""
    for (i++; i <= n; i++) {
      c = at()
      if (c == "\"") { i++; return s }
      if (c == "\\") {
        i++; c = at()
        if (c != "\"" && c != "\\" && c != "/") fail("entry " k ": unsupported escape \\" c)
      }
      s = s c
    }
    fail("entry " k ": unterminated string")
  }
  function entry(   key, val, repo, version, have, name, c) {
    ws()
    if (at() != "{") fail("entry " k ": must be an object like " example)
    i++; ws()
    while (at() != "}") {
      if (at() != "\"") fail("entry " k ": keys must be quoted, e.g. \"repo\" (not repo)")
      key = readstr(); ws()
      if (at() != ":") fail("entry " k ": expected : after \"" key "\"")
      i++; ws()
      if (at() != "\"") fail("entry " k ": \"" key "\" must be a quoted string")
      val = readstr()
      if (key != "repo" && key != "version") fail("entry " k ": unknown key \"" key "\"; only \"repo\" and \"version\" are allowed")
      if (key in have) fail("entry " k ": \"" key "\" is set twice")
      have[key] = 1
      if (key == "repo") repo = val; else version = val
      ws(); c = at()
      if (c == ",") { i++; ws(); if (at() == "}") fail("entry " k ": trailing comma"); continue }
      if (c != "}") fail("entry " k ": expected , or } after \"" key "\"")
    }
    i++
    if (!("repo" in have)) fail("entry " k ": missing \"repo\"")
    if (!("version" in have)) fail("entry " k ": missing \"version\"")
    if (repo !~ /^[A-Za-z0-9._-]+\/[A-Za-z0-9._-]+$/) fail("entry " k ": repo \"" repo "\" must be owner/repo")
    if (version == "" || version ~ /[ \t\r\n]/) fail("entry " k ": version \"" version "\" must be a release tag such as v1.0.0")
    name = repo; sub(/.*\//, "", name)
    if (name in names) {
      if (names[name] == repo) fail("entry " k ": " repo " is listed twice")
      fail("entry " k ": " repo " and " names[name] " would both be saved as " name)
    }
    names[name] = repo
    out = out repo "\t" version "\n"
  }
  { buf = buf $0 "\n" }
  END {
    example = "{ \"repo\": \"owner/repo\", \"version\": \"v1.0.0\" }"
    n = length(buf); i = 1; k = 0
    ws()
    if (at() != "[") fail("must be a JSON array: [ " example " ]")
    i++; ws()
    if (at() == "]") fail("lists no repos")
    for (k = 1; ; k++) {
      entry(); ws()
      if (at() == "]") break
      if (at() != ",") fail("entry " k ": expected , or ] after it")
      i++; ws()
      if (at() == "]") fail("entry " k ": trailing comma")
    }
    i++; ws()
    if (i <= n) fail("unexpected content after the closing ]")
    printf "%s", out
  }' "$list") || {
  printf 'error: %s: %s\n' "$list" "$entries" >&2
  exit 1
}

if ! command -v gh >/dev/null; then
  echo "error: gh (GitHub CLI) is required: https://cli.github.com" >&2
  exit 1
fi
if ! gh auth status >/dev/null 2>&1; then
  echo "error: gh is not logged in; run 'gh auth login' or set GH_TOKEN" >&2
  exit 1
fi

mkdir -p "$out"
failed=0
# Read entries on fd 3 so gh can't consume the loop's input.
while IFS="$tab" read -r repo version <&3; do
  # Find the spec asset with gh's built-in --jq.
  if ! asset=$(gh release view "$version" -R "$repo" --json assets --jq \
    '[.assets[].name | select(ascii_downcase | test("^openapi.*[.](json|ya?ml)$"))][0] // ""' \
    2>&1 </dev/null); then
    printf 'error: %s %s: %s\n' "$repo" "$version" "$asset" >&2
    failed=1
    continue
  fi
  if [ -z "$asset" ]; then
    printf 'error: %s %s: the release has no openapi*.json/.yaml/.yml asset\n' "$repo" "$version" >&2
    failed=1
    continue
  fi
  ext=$(printf '%s' "${asset##*.}" | tr '[:upper:]' '[:lower:]')
  file="$out/${repo#*/}.$ext"
  if ! err=$(gh release download "$version" -R "$repo" -p "$asset" -O "$file" --clobber 2>&1 </dev/null); then
    printf 'error: %s %s: %s\n' "$repo" "$version" "$err" >&2
    failed=1
    continue
  fi
  printf '%s %s: %s -> %s\n' "$repo" "$version" "$asset" "$file"
done 3<<EOF
$entries
EOF

exit "$failed"

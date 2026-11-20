#!/usr/bin/env bash
# Signs the release artifacts with the key that never leaves this machine, then publishes the
# release. Safe to start right after pushing the tag: it waits for CI and the Release build.
#
#   scripts/sign-release.sh v1.2.3
#
# The key defaults to ~/.config/replay-viewer/release-key.pem; override with
# REPLAY_VIEWER_SIGNING_KEY. The public half is embedded in src/ReplayViewer/Hosting/ReleaseSignature.cs.
set -euo pipefail

tag=${1:-}
if [ -z "$tag" ]; then
    echo "usage: scripts/sign-release.sh v1.2.3" >&2
    exit 2
fi

version=${tag#v}
repo=ddev01/fn-replay-viewer
key=${REPLAY_VIEWER_SIGNING_KEY:-$HOME/.config/replay-viewer/release-key.pem}

if [ ! -f "$key" ]; then
    echo "signing key not found: $key" >&2
    exit 1
fi

work=$(mktemp -d)
pubdir=$(mktemp -d)
trap 'rm -rf "$work" "$pubdir"' EXIT

# The Release workflow no longer runs the tests: refuse to publish a commit CI has not passed.
sha=$(gh api "repos/$repo/commits/$tag" --jq .sha)
ci_id=$(gh run list --repo "$repo" --workflow CI --commit "$sha" --json databaseId --jq '.[0].databaseId // empty')
if [ -z "$ci_id" ]; then
    echo "no CI run found for $tag ($sha); push it to develop and let CI finish first" >&2
    exit 1
fi
if [ "$(gh run view "$ci_id" --repo "$repo" --json conclusion --jq .conclusion)" != "success" ]; then
    echo "waiting for CI run $ci_id"
    gh run watch "$ci_id" --repo "$repo" --exit-status >/dev/null || {
        echo "CI did not succeed for $tag" >&2
        exit 1
    }
fi

# Wait for the Release run to appear and finish, so this can be started right after pushing the tag.
run=""
for _ in $(seq 1 30); do
    run=$(gh run list --repo "$repo" --workflow Release --limit 20 \
        --json databaseId,headBranch \
        --jq "[.[] | select(.headBranch == \"$tag\")][0].databaseId")
    [ -n "$run" ] && break
    sleep 2
done

if [ -z "$run" ]; then
    echo "no Release run found for $tag — push the tag first" >&2
    exit 1
fi

echo "waiting for Release run $run"
gh run watch "$run" --repo "$repo" --exit-status >/dev/null

echo "downloading artifacts from run $run"
gh run download "$run" --repo "$repo" -n release-assets -D "$work"
# upload-artifact keeps the installer/ folder; the release wants the files flat.
find "$work" -mindepth 2 -type f -exec mv -t "$work" {} +
find "$work" -mindepth 1 -type d -empty -delete

pubkey="$pubdir/public.pem"
openssl ec -in "$key" -pubout -out "$pubkey" 2>/dev/null

for artifact in "$work"/*.exe "$work"/*.zip; do
    [ -f "$artifact" ] || continue
    openssl dgst -sha256 -sign "$key" -out "$artifact.sig" "$artifact"
    openssl dgst -sha256 -verify "$pubkey" -signature "$artifact.sig" "$artifact"
done

ls -1 "$work"

gh release create "$tag" "$work"/* --repo "$repo" \
    --title "Replay Viewer $version" \
    --notes "Download **ReplayViewer-Setup-$version.exe** and run it. No unzipping and no extra DLLs.

The installer puts Replay Viewer in your user folder, adds a Start Menu shortcut, starts it at sign-in, and can launch it when setup finishes.

Each asset ships a detached \`.sig\`, signed with the release key; Replay Viewer verifies it before installing an update."

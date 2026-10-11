#!/bin/sh
# Demo data through the public API (ADR-010): a few links, then redirects with different Referer
# headers so the statistics have content. Idempotent: does nothing if the demo links exist.
set -eu

API="${API_URL:-http://api:8080}"

echo "Waiting for $API/health ..."
tries=0
until [ "$(curl -s -o /dev/null -w '%{http_code}' "$API/health")" = "200" ]; do
  tries=$((tries + 1))
  if [ "$tries" -gt 60 ]; then
    echo "The API did not become healthy within 60 s." >&2
    exit 1
  fi
  sleep 1
done

if [ "$(curl -s -o /dev/null -w '%{http_code}' "$API/api/links/demo-docs")" = "200" ]; then
  echo "Demo data already present; nothing to do."
  exit 0
fi

create() { # url alias [expiresAt]
  if [ -n "${3:-}" ]; then
    body="{\"url\":\"$1\",\"customAlias\":\"$2\",\"expiresAt\":\"$3\"}"
  else
    body="{\"url\":\"$1\",\"customAlias\":\"$2\"}"
  fi
  status=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$API/api/links" -H 'Content-Type: application/json' -d "$body")
  echo "  create $2 → $status"
}

follow() { # alias times [referer]
  i=0
  while [ "$i" -lt "$2" ]; do
    if [ -n "${3:-}" ]; then
      curl -s -o /dev/null -H "Referer: $3" "$API/$1"
    else
      curl -s -o /dev/null "$API/$1"
    fi
    i=$((i + 1))
  done
  echo "  follow $1 × $2 ${3:-(direct)}"
}

in_seconds() { date -u -d "@$(( $(date +%s) + $1 ))" '+%Y-%m-%dT%H:%M:%SZ'; }

echo "Creating demo links ..."
create "https://learn.microsoft.com/aspnet/core/" "demo-docs"
create "https://github.com/github/spec-kit" "demo-speckit"
create "https://example.com/launch-week" "demo-launch" "$(in_seconds 604800)"
create "https://example.com/flash-sale" "demo-flash" "$(in_seconds 180)"

echo "Following them ..."
follow demo-docs 6 "https://news.example/today?ref=frontpage"
follow demo-docs 3 "https://blog.example/posts/aspnet"
follow demo-docs 4
follow demo-speckit 5 "https://social.example/feed"
follow demo-speckit 2
follow demo-launch 3 "https://news.example/launch"
follow demo-flash 1

echo "Done. Open http://localhost:8080 and look up demo-docs (demo-flash expires about 3 minutes after seeding)."

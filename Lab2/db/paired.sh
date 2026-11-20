#!/usr/bin/env bash
set -euo pipefail
if [ ! -s "$PGDATA/PG_VERSION" ]; then
  initdb -D "$PGDATA" --auth=trust --no-locale --encoding=UTF8 >/dev/null
  pg_ctl -D "$PGDATA" -o "-c listen_addresses=''" -w start >/dev/null
  psql -X -v ON_ERROR_STOP=1 -d postgres -f "/db/${SERVICE,,}.sql"
  pg_ctl -D "$PGDATA" -m fast -w stop >/dev/null
fi
postgres -D "$PGDATA" -c listen_addresses=127.0.0.1 &
pg=$!
dotnet "$SERVICE.dll" &
app=$!
stop() { kill -TERM "$app" 2>/dev/null || true; kill -INT "$pg" 2>/dev/null || true; wait || true; }
trap 'stop; exit 0' TERM INT
rc=0
wait -n "$app" "$pg" || rc=$?
stop
exit "$rc"

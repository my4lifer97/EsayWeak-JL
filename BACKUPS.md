# Database backups

`.github/workflows/backup-database.yml` dumps the production Postgres every night at 02:30 UTC
(and on demand: Actions -> "Back up production database" -> Run workflow). Each run uploads one
artifact, `esayweek-db-<UTC time>`, kept for 30 days. The file inside is a `pg_dump` custom-format
archive encrypted with AES-256 (`gpg --symmetric`). The repo is public, so the file is useless
without `BACKUP_PASSPHRASE`. Keep that passphrase in a password manager too: GitHub never shows a
secret again, and a lost passphrase means every backup is lost.

A failed run emails the repo owner (GitHub's default for scheduled workflows). Check the Actions tab
now and then anyway: GitHub disables scheduled workflows after 60 days with no repo activity.

## Setup (once)

1. Railway -> Postgres service -> Settings -> Networking -> **Add Public Access**.
   Note: Railway's own `DATABASE_PUBLIC_URL` there is only `host:port`, not a full URL.
2. GitHub repo -> Settings -> Secrets and variables -> Actions -> New repository secret:
   - `DATABASE_PUBLIC_URL` = `postgresql://PGUSER:PGPASSWORD@RAILWAY_TCP_PROXY_DOMAIN:RAILWAY_TCP_PROXY_PORT/PGDATABASE?sslmode=require`
     (values from the Postgres service's Variables). Without opening it on screen:
     ```bash
     eval "$(railway variables -s Postgres --kv | tr -d '\r' | grep -E '^(PGUSER|PGPASSWORD|PGDATABASE|RAILWAY_TCP_PROXY_DOMAIN|RAILWAY_TCP_PROXY_PORT)=' | sed 's/^/export /')"
     printf 'postgresql://%s:%s@%s:%s/%s?sslmode=require' "$PGUSER" "$PGPASSWORD" "$RAILWAY_TCP_PROXY_DOMAIN" "$RAILWAY_TCP_PROXY_PORT" "$PGDATABASE" | gh secret set DATABASE_PUBLIC_URL
     ```
   - `BACKUP_PASSPHRASE` = a long random string (at least 20 characters), e.g. `openssl rand -base64 32`
3. Run the workflow once by hand and check that it is green.

Whenever the Postgres password (or the proxy port) changes, re-run the `DATABASE_PUBLIC_URL` command above.

## Restore

```bash
# 1. Download the artifact zip from the workflow run and unzip it, then decrypt:
gpg --decrypt --output db.dump esayweek-db-2026-11-06T0230Z.dump.gpg     # asks for the passphrase

# 2. Look inside without touching any database:
pg_restore --list db.dump | head

# 3. Restore into a NEW, empty database first and check it there:
createdb -h HOST -p PORT -U postgres restore_check
pg_restore --no-owner --no-privileges -h HOST -p PORT -U postgres -d restore_check db.dump
```

Use a `pg_restore` at least as new as the one that made the dump (PostgreSQL 18 client).
Restoring over the live `railway` database (`--clean --if-exists -d railway`) replaces all
current data with the backup's. Stop the backend first and only do it as a last resort.

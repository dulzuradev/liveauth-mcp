# LiveAuth Infrastructure Safeguards

## Critical Volumes (NEVER DELETE)
- `sqlite_data` - Contains the live database
- `caddy_config` - Caddy TLS certificates and config

## Backup Schedule
- Daily at 3 AM PST
- Retention: 7 days
- Location: `/opt/liveauth/backups/`

## Before Deploying Frontend
1. Build locally first: `npm run build`
2. Test locally: `npm start` (if applicable)
3. Check that index.html exists in dist folder
4. Sync with: `rsync -avz --delete dist/ liveauth@server:/opt/liveauth/LiveAuthWeb/dist/liveauth-web/`
5. Verify site is up: `curl -I https://liveauth.app/`

## Docker Volume Safety
- NEVER run `docker volume rm` without checking
- Check what's mounted: `docker inspect container_name | grep -A10 Mounts`
- Before deleting any volume, check if it contains data

## If Site Goes Down
1. Check Caddy logs: `docker logs liveauth-caddy`
2. Check API logs: `docker logs liveauth-api`
3. Verify volume mounts: `docker inspect liveauth-caddy | grep Mounts`
4. Check disk space: `df -h`

## Emergency Recovery
- Restore DB from backup: `gunzip < backup.db.gz > /tmp/restore.db && docker cp /tmp/restore.db liveauth-api:/data/liveauth.db && docker restart liveauth-api`

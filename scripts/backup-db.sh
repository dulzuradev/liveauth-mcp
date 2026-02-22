#!/bin/bash
# LiveAuth Database Backup Script
# Runs nightly at 3 AM

BACKUP_DIR="/opt/liveauth/backups"
DATE=$(date +%Y-%m-%d_%H%M%S)

# Create backup directory if it doesn't exist
mkdir -p "$BACKUP_DIR"

# Copy the database from the Docker container
docker cp liveauth-api:/data/liveauth.db "$BACKUP_DIR/liveauth_$DATE.db"

if [ -f "$BACKUP_DIR/liveauth_$DATE.db" ]; then
    # Compress the backup
    gzip "$BACKUP_DIR/liveauth_$DATE.db"
    
    echo "Backup created: liveauth_$DATE.db.gz"
    
    # Keep only last 7 backups (7 days)
    cd "$BACKUP_DIR"
    ls -t liveauth_*.db.gz 2>/dev/null | tail -n +8 | xargs -r rm -f
    
    echo "Backup complete. Kept last 7 days."
else
    echo "ERROR: Failed to copy database from container"
    exit 1
fi

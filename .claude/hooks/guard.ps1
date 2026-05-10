# Pre-tool hook: blocks known destructive shell operations
# Triggered by PreToolUse on Bash tool calls. Exits 2 to block, 0 to allow.

param()

$stdin = [Console]::In.ReadToEnd()
if ([string]::IsNullOrWhiteSpace($stdin)) { exit 0 }

try {
    $data = $stdin | ConvertFrom-Json
} catch {
    exit 0
}

if ($data.tool_name -ne "Bash") { exit 0 }

$command = $data.tool_input.command
if ([string]::IsNullOrWhiteSpace($command)) { exit 0 }

$cmd = $command.Trim()

# Each entry: Pattern (case-insensitive .NET regex) + Reason shown to Claude
$rules = @(
    # ── Git force operations ──────────────────────────────────────────────────
    [pscustomobject]@{
        Pattern = '(?i)git\s+push\s+.*--force(?!-with-lease)'
        Reason  = "git push --force rewrites remote history and can cause permanent data loss for collaborators."
    }
    [pscustomobject]@{
        Pattern = '(?i)git\s+push\s+((\S+\s+)|-{0,2}\S+=\S+\s+)*-f(\s|$)'
        Reason  = "git push -f (force) rewrites remote history."
    }
    [pscustomobject]@{
        Pattern = '(?i)git\s+push\s+.*--force-with-lease'
        Reason  = "git push --force-with-lease still force-pushes; requires explicit human approval."
    }
    [pscustomobject]@{
        Pattern = '(?i)git\s+reset\s+--hard'
        Reason  = "git reset --hard permanently discards all uncommitted changes and cannot be undone."
    }
    [pscustomobject]@{
        Pattern = '(?i)git\s+clean\s+-\w*f'
        Reason  = "git clean -f permanently deletes all untracked files from the working tree."
    }
    [pscustomobject]@{
        Pattern = '(?i)git\s+branch\s+(?-i)-D\b'
        Reason  = "git branch -D force-deletes a branch even if it has unmerged commits."
    }
    [pscustomobject]@{
        Pattern = '(?i)git\s+(checkout|restore)\s+(--\s+\.|\.)'
        Reason  = "git checkout/restore . discards all tracked working directory changes permanently."
    }

    # ── SQL destructive DDL / DML ─────────────────────────────────────────────
    [pscustomobject]@{
        Pattern = '(?i)\bdrop\s+database\b'
        Reason  = "DROP DATABASE permanently deletes an entire database and all its data."
    }
    [pscustomobject]@{
        Pattern = '(?i)\bdrop\s+table\b'
        Reason  = "DROP TABLE permanently deletes a table and all its rows."
    }
    [pscustomobject]@{
        Pattern = '(?i)\bdrop\s+schema\b'
        Reason  = "DROP SCHEMA permanently deletes a schema and all contained objects."
    }
    [pscustomobject]@{
        Pattern = '(?i)\btruncate\s+table\b'
        Reason  = "TRUNCATE TABLE irrecoverably deletes all rows without a WHERE clause."
    }
    [pscustomobject]@{
        Pattern = '(?i)\bdelete\s+from\s+\w+\s*;'
        Reason  = "DELETE FROM without a WHERE clause deletes every row in the table."
    }

    # ── MongoDB destructive operations ────────────────────────────────────────
    [pscustomobject]@{
        Pattern = '(?i)\.dropdatabase\s*\('
        Reason  = "dropDatabase() permanently deletes the entire MongoDB database."
    }
    [pscustomobject]@{
        Pattern = '(?i)\bdb\s*\.\s*drop\s*\('
        Reason  = "db.drop() permanently deletes a MongoDB collection."
    }
    [pscustomobject]@{
        Pattern = '(?i)\.deletemany\s*\(\s*\{\s*\}'
        Reason  = "deleteMany({}) with an empty filter deletes every document in the collection."
    }
    [pscustomobject]@{
        Pattern = '(?i)\.remove\s*\(\s*\{\s*\}'
        Reason  = "remove({}) with an empty filter deletes every document in the collection."
    }

    # ── File system destructive operations ────────────────────────────────────
    [pscustomobject]@{
        Pattern = '(?i)\brm\s+-[a-z]*r[a-z]*f\b|\brm\s+-[a-z]*f[a-z]*r\b'
        Reason  = "rm -rf / rm -fr recursively force-deletes files and directories with no recycle bin."
    }
    [pscustomobject]@{
        Pattern = '(?i)\bremove-item\b.+-(recurse|r)\b.+-(force|fo)\b|\bremove-item\b.+-(force|fo)\b.+-(recurse|r)\b'
        Reason  = "Remove-Item -Recurse -Force permanently deletes directory trees without confirmation."
    }
    [pscustomobject]@{
        Pattern = '(?i)\brd\b.*/s\b|\brmdir\b.*/s\b'
        Reason  = "rd /s and rmdir /s silently delete entire directory trees."
    }
    [pscustomobject]@{
        Pattern = '(?i)\bdel\b.*/(f|s|q)'
        Reason  = "del /f /s /q force-deletes files including read-only, silently and recursively."
    }

    # ── Disk / low-level destructive operations ───────────────────────────────
    [pscustomobject]@{
        Pattern = '(?i)^\s*format\s+[a-z]:'
        Reason  = "format wipes an entire drive volume."
    }
    [pscustomobject]@{
        Pattern = '(?i)\bdd\b.*\bof=/dev/'
        Reason  = "dd writing directly to a device node can overwrite a disk or partition."
    }
    [pscustomobject]@{
        Pattern = '(?i)\bdd\b.*\bif=/dev/zero\b'
        Reason  = "dd with if=/dev/zero zeroes out the target, destroying all data."
    }
    [pscustomobject]@{
        Pattern = '(?i)\bmkfs\b'
        Reason  = "mkfs formats a filesystem, permanently destroying all existing data on the target."
    }
    [pscustomobject]@{
        Pattern = '(?i)\bdiskpart\b'
        Reason  = "diskpart can perform irreversible disk partitioning and formatting operations."
    }

    # ── ORM / framework destructive methods ──────────────────────────────────
    [pscustomobject]@{
        Pattern = '(?i)\.ensuredeleted\s*\('
        Reason  = "EnsureDeleted() drops the entire database managed by EF Core."
    }
    [pscustomobject]@{
        Pattern = '(?i)database\.delete\s*\('
        Reason  = "Database.Delete() drops the database managed by the ORM."
    }
)

foreach ($rule in $rules) {
    if ($cmd -match $rule.Pattern) {
        $msg = @"
BLOCKED by .claude/hooks/guard.ps1
Reason : $($rule.Reason)
Command: $cmd

To run this intentionally, execute it directly in your terminal outside of Claude Code.
"@
        [Console]::Error.WriteLine($msg)
        exit 2
    }
}

exit 0

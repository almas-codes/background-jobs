-- Run this once before switching to PostgreSQL storage.
-- The partial index on (queue, status, scheduled_at) makes polling extremely fast.

CREATE TABLE IF NOT EXISTS background_jobs (
    id                     TEXT PRIMARY KEY,
    type_name              TEXT NOT NULL,
    method_name            TEXT NOT NULL,
    parameter_type_names   TEXT NOT NULL,
    arguments_json         TEXT NOT NULL,
    queue                  TEXT NOT NULL DEFAULT 'default',
    priority               INT  NOT NULL DEFAULT 1,
    status                 INT  NOT NULL DEFAULT 0,
    created_at             TIMESTAMPTZ NOT NULL DEFAULT now(),
    scheduled_at           TIMESTAMPTZ NOT NULL DEFAULT now(),
    started_at             TIMESTAMPTZ,
    completed_at           TIMESTAMPTZ,
    retry_count            INT NOT NULL DEFAULT 0,
    max_retries            INT NOT NULL DEFAULT 3,
    last_error             TEXT,
    recurring_job_id       TEXT,
    idempotency_key        TEXT UNIQUE,
    claimed_by             TEXT,
    lease_expires_at       TIMESTAMPTZ
);

-- Partial index: only indexes rows the worker actually cares about.
-- Makes polling O(log n) even with millions of completed jobs in the table.
CREATE INDEX IF NOT EXISTS idx_bg_jobs_poll
    ON background_jobs (queue, status, scheduled_at)
    WHERE status IN (0, 4);

CREATE TABLE IF NOT EXISTS background_jobs_recurring (
    id                     TEXT PRIMARY KEY,
    type_name              TEXT NOT NULL,
    method_name            TEXT NOT NULL,
    parameter_type_names   TEXT NOT NULL,
    arguments_json         TEXT NOT NULL,
    cron_expression        TEXT NOT NULL,
    timezone_id            TEXT NOT NULL DEFAULT 'UTC',
    queue                  TEXT NOT NULL DEFAULT 'default',
    next_run_at            TIMESTAMPTZ,
    last_run_at            TIMESTAMPTZ
);

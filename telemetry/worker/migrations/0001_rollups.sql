CREATE TABLE daily_budget (
    day TEXT PRIMARY KEY,
    used INTEGER NOT NULL
) WITHOUT ROWID;

CREATE TABLE rollups (
    day TEXT NOT NULL,
    kind TEXT NOT NULL,
    version TEXT NOT NULL,
    os TEXT NOT NULL,
    protocol TEXT NOT NULL,
    metric TEXT NOT NULL,
    total INTEGER NOT NULL,
    PRIMARY KEY (day, kind, version, os, protocol, metric)
) WITHOUT ROWID;

-- Each metric written consumes one unit, including UPSERTs on existing rows.
-- A batched report is discarded atomically if it exhausts the free-tier budget.
CREATE TRIGGER rollups_budget BEFORE INSERT ON rollups
BEGIN
    INSERT INTO daily_budget(day, used) VALUES (NEW.day, 1)
    ON CONFLICT(day) DO UPDATE SET used = used + 1;
    SELECT CASE WHEN (SELECT used FROM daily_budget WHERE day = NEW.day) > 5000
        THEN RAISE(ABORT, 'apptunnel-daily-budget') END;
END;

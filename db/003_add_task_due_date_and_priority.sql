ALTER TABLE tasks
    ADD COLUMN due_date date,
    ADD COLUMN priority text NOT NULL DEFAULT 'Medium'
        CHECK (priority IN ('Low', 'Medium', 'High'));

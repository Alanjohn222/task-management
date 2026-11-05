CREATE INDEX tasks_owner_created_id_idx
    ON tasks (owner_id, created_at DESC, id DESC);

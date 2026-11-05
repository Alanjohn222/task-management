ALTER TABLE users
    ADD COLUMN name text
    CHECK (name IS NULL OR (length(btrim(name)) > 0 AND length(name) <= 100));

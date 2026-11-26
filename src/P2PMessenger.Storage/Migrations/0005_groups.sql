ALTER TABLE group_key ADD COLUMN record_enc BLOB NOT NULL DEFAULT x'' CHECK (length(record_enc) >= 40);

ALTER TABLE group_member ADD COLUMN joined INTEGER NOT NULL DEFAULT 0 CHECK (joined IN (0, 1, 2));

ALTER TABLE group_member ADD COLUMN invitation_state INTEGER NOT NULL DEFAULT 0 CHECK (invitation_state IN (0, 1, 2));

ALTER TABLE group_member ADD COLUMN join_state INTEGER NOT NULL DEFAULT 0 CHECK (join_state IN (0, 1, 2));

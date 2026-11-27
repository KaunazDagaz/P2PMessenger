ALTER TABLE chat_group ADD COLUMN authority_enc BLOB CHECK (authority_enc IS NULL OR length(authority_enc) >= 40);

ALTER TABLE group_member ADD COLUMN update_state INTEGER NOT NULL DEFAULT 0 CHECK (update_state IN (0, 1, 2));

ALTER TABLE group_member ADD COLUMN leave_state INTEGER NOT NULL DEFAULT 0 CHECK (leave_state IN (0, 1, 2));

ALTER TABLE group_member ADD COLUMN removed_state INTEGER NOT NULL DEFAULT 0 CHECK (removed_state IN (0, 1, 2));

ALTER TABLE group_member ADD COLUMN removed_epoch INTEGER NOT NULL DEFAULT 0 CHECK (removed_epoch >= 0);

CREATE TABLE group_leave_proof (
    group_id INTEGER NOT NULL REFERENCES chat_group (id) ON DELETE CASCADE,
    member_person_id INTEGER NOT NULL REFERENCES person (id),
    proof_enc BLOB NOT NULL CHECK (length(proof_enc) >= 40),
    PRIMARY KEY (group_id, member_person_id)
) STRICT, WITHOUT ROWID;

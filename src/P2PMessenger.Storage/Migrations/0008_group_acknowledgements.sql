CREATE TABLE group_acknowledgement (
    message_id INTEGER NOT NULL REFERENCES message (id) ON DELETE CASCADE,
    recipient_person_id INTEGER NOT NULL REFERENCES person (id),
    ack_enc BLOB NOT NULL CHECK (length(ack_enc) >= 40),
    PRIMARY KEY (message_id, recipient_person_id)
) STRICT, WITHOUT ROWID;

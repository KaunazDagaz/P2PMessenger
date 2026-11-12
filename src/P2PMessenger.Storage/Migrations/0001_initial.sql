CREATE TABLE data_key (
    id INTEGER PRIMARY KEY CHECK (id = 1),
    protection INTEGER NOT NULL CHECK (protection IN (1, 2, 3, 4)),
    wrapped_key BLOB NOT NULL CHECK (length(wrapped_key) > 0),
    kdf_salt BLOB CHECK (kdf_salt IS NULL OR length(kdf_salt) = 16),
    kdf_memory_kib INTEGER CHECK (kdf_memory_kib IS NULL OR kdf_memory_kib > 0),
    kdf_passes INTEGER CHECK (kdf_passes IS NULL OR kdf_passes > 0),
    CHECK (
        (protection = 1 AND kdf_salt IS NOT NULL AND kdf_memory_kib IS NOT NULL AND kdf_passes IS NOT NULL)
        OR (protection <> 1 AND kdf_salt IS NULL AND kdf_memory_kib IS NULL AND kdf_passes IS NULL)
    )
) STRICT;

CREATE TABLE person (
    id INTEGER PRIMARY KEY,
    identity_key_mac BLOB NOT NULL UNIQUE CHECK (length(identity_key_mac) = 32),
    identity_key_enc BLOB NOT NULL CHECK (length(identity_key_enc) >= 40),
    display_name_enc BLOB NOT NULL CHECK (length(display_name_enc) >= 40)
) STRICT;

CREATE TABLE identity (
    id INTEGER PRIMARY KEY CHECK (id = 1),
    person_id INTEGER NOT NULL UNIQUE REFERENCES person (id),
    key_set_enc BLOB NOT NULL CHECK (length(key_set_enc) >= 40)
) STRICT;

CREATE TABLE contact (
    person_id INTEGER PRIMARY KEY REFERENCES person (id) ON DELETE CASCADE,
    code_version INTEGER NOT NULL CHECK (code_version = 1),
    static_key_mac BLOB NOT NULL UNIQUE CHECK (length(static_key_mac) = 32),
    static_key_enc BLOB NOT NULL CHECK (length(static_key_enc) >= 40),
    onion_key_enc BLOB NOT NULL CHECK (length(onion_key_enc) >= 40),
    code_signature_enc BLOB NOT NULL CHECK (length(code_signature_enc) >= 40),
    added_via INTEGER NOT NULL CHECK (added_via IN (1, 2)),
    status INTEGER NOT NULL CHECK (status IN (1, 2)),
    key_checked INTEGER NOT NULL DEFAULT 0 CHECK (key_checked IN (0, 1))
) STRICT;

CREATE TABLE chat_group (
    id INTEGER PRIMARY KEY,
    shared_id_mac BLOB NOT NULL UNIQUE CHECK (length(shared_id_mac) = 32),
    shared_id_enc BLOB NOT NULL CHECK (length(shared_id_enc) >= 40),
    name_enc BLOB NOT NULL CHECK (length(name_enc) >= 40)
) STRICT;

CREATE TABLE group_member (
    group_id INTEGER NOT NULL REFERENCES chat_group (id) ON DELETE CASCADE,
    person_id INTEGER NOT NULL REFERENCES person (id),
    state INTEGER NOT NULL CHECK (state IN (1, 2, 3)),
    PRIMARY KEY (group_id, person_id)
) STRICT, WITHOUT ROWID;

CREATE INDEX ix_group_member_person ON group_member (person_id);

CREATE TABLE group_key (
    group_id INTEGER NOT NULL REFERENCES chat_group (id) ON DELETE CASCADE,
    epoch INTEGER NOT NULL CHECK (epoch >= 1),
    key_enc BLOB NOT NULL CHECK (length(key_enc) >= 40),
    PRIMARY KEY (group_id, epoch)
) STRICT, WITHOUT ROWID;

CREATE TABLE conversation (
    id INTEGER PRIMARY KEY,
    contact_person_id INTEGER UNIQUE REFERENCES contact (person_id) ON DELETE CASCADE,
    group_id INTEGER UNIQUE REFERENCES chat_group (id) ON DELETE CASCADE,
    CHECK ((contact_person_id IS NULL) <> (group_id IS NULL))
) STRICT;

CREATE TABLE message (
    id INTEGER PRIMARY KEY,
    conversation_id INTEGER NOT NULL REFERENCES conversation (id) ON DELETE CASCADE,
    author_person_id INTEGER NOT NULL REFERENCES person (id),
    message_id_mac BLOB NOT NULL UNIQUE CHECK (length(message_id_mac) = 32),
    sender_sequence INTEGER NOT NULL CHECK (sender_sequence >= 1),
    text_enc BLOB NOT NULL CHECK (length(text_enc) >= 40),
    written_at_enc BLOB NOT NULL CHECK (length(written_at_enc) >= 40),
    group_envelope_enc BLOB CHECK (group_envelope_enc IS NULL OR length(group_envelope_enc) >= 40),
    UNIQUE (conversation_id, author_person_id, sender_sequence)
) STRICT;

CREATE INDEX ix_message_conversation ON message (conversation_id, id);

CREATE INDEX ix_message_author ON message (author_person_id);

CREATE TABLE message_delivery (
    message_id INTEGER NOT NULL REFERENCES message (id) ON DELETE CASCADE,
    recipient_person_id INTEGER NOT NULL REFERENCES person (id),
    state INTEGER NOT NULL CHECK (state IN (1, 2)),
    PRIMARY KEY (message_id, recipient_person_id)
) STRICT, WITHOUT ROWID;

CREATE INDEX ix_message_delivery_waiting ON message_delivery (recipient_person_id) WHERE state = 1;

CREATE TABLE seen_message (
    message_id_mac BLOB PRIMARY KEY CHECK (length(message_id_mac) = 32)
) STRICT, WITHOUT ROWID;

CREATE TABLE copied_conversation (
    id INTEGER PRIMARY KEY,
    origin_mac BLOB NOT NULL UNIQUE CHECK (length(origin_mac) = 32),
    title_enc BLOB NOT NULL CHECK (length(title_enc) >= 40),
    copied_at_enc BLOB NOT NULL CHECK (length(copied_at_enc) >= 40)
) STRICT;

CREATE TABLE copied_message (
    copied_conversation_id INTEGER NOT NULL REFERENCES copied_conversation (id) ON DELETE CASCADE,
    position INTEGER NOT NULL CHECK (position >= 1),
    written_by_owner INTEGER NOT NULL CHECK (written_by_owner IN (0, 1)),
    author_name_enc BLOB,
    text_enc BLOB NOT NULL CHECK (length(text_enc) >= 40),
    written_at_enc BLOB NOT NULL CHECK (length(written_at_enc) >= 40),
    PRIMARY KEY (copied_conversation_id, position),
    CHECK (
        (written_by_owner = 1 AND author_name_enc IS NULL)
        OR (written_by_owner = 0 AND author_name_enc IS NOT NULL AND length(author_name_enc) >= 40)
    )
) STRICT, WITHOUT ROWID;

CREATE VIEW outbox AS
SELECT
    delivery.message_id,
    delivery.recipient_person_id,
    message.conversation_id
FROM message_delivery AS delivery
JOIN message ON message.id = delivery.message_id
WHERE delivery.state = 1;

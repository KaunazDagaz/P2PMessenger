ALTER TABLE message ADD COLUMN message_id_enc BLOB NOT NULL DEFAULT x'' CHECK (length(message_id_enc) >= 40);

ALTER TABLE message ADD COLUMN recorded_at_enc BLOB NOT NULL DEFAULT x'' CHECK (length(recorded_at_enc) >= 40);

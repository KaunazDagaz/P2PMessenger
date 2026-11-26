ALTER TABLE copied_conversation ADD COLUMN origin_enc BLOB NOT NULL DEFAULT x'' CHECK (length(origin_enc) >= 40);

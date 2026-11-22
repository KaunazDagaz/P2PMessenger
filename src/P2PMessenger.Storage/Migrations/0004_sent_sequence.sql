ALTER TABLE conversation ADD COLUMN sent_sequence_enc BLOB CHECK (sent_sequence_enc IS NULL OR length(sent_sequence_enc) >= 40);

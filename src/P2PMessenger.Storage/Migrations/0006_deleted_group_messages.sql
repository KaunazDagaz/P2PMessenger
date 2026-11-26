ALTER TABLE conversation ADD COLUMN deleted_ranges_enc BLOB CHECK (deleted_ranges_enc IS NULL OR length(deleted_ranges_enc) >= 40);

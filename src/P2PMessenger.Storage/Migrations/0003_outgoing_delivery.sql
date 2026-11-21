INSERT INTO message_delivery (message_id, recipient_person_id, state)
SELECT message.id, conversation.contact_person_id, 1
FROM message
JOIN conversation ON conversation.id = message.conversation_id
JOIN identity ON identity.person_id = message.author_person_id
WHERE conversation.contact_person_id IS NOT NULL
    AND NOT EXISTS (SELECT 1 FROM message_delivery AS delivery WHERE delivery.message_id = message.id);

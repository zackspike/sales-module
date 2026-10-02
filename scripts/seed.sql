INSERT INTO events ("Id", "Name", "Artist", "VenueId", "VenueName", "Date", "TotalSeats") 
VALUES ('11111111-1111-1111-1111-111111111111', 'Rock Fest 2026', 'The Rockers', '22222222-2222-2222-2222-222222222222', 'Estadio Nacional', '2026-11-20 20:00:00+00', 50) 
ON CONFLICT ("Id") DO NOTHING;

INSERT INTO tickets ("Id", "EventId", "SeatNumber", "Status", "FullName", "Email", "TicketCode", "IdempotencyKey", "CreatedAtUtc", "PurchasedAtUtc")
SELECT 
  gen_random_uuid(),
  '11111111-1111-1111-1111-111111111111',
  'A-' || i,
  'Available',
  '',
  '',
  '',
  '00000000-0000-0000-0000-000000000000',
  NOW() AT TIME ZONE 'UTC',
  NULL
FROM generate_series(1, 50) AS i
ON CONFLICT ("EventId", "SeatNumber") DO NOTHING;

-- Seeds the agreed test event (SP-03 / ALIGN-01) with one zone and 50 available seats A-1..A-50.
-- Ids are deterministic (md5-based UUIDs) so re-running the script never duplicates rows.

INSERT INTO events (event_id, event_name, artist_name, venue_id, venue_name, event_date_utc, total_seat_count)
VALUES ('11111111-1111-1111-1111-111111111111', 'Rock Fest 2026', 'The Rockers',
        '22222222-2222-2222-2222-222222222222', 'Estadio Nacional', '2026-11-20T20:00:00Z', 50)
ON CONFLICT (event_id) DO NOTHING;

INSERT INTO event_zones (zone_id, event_id, zone_price)
VALUES ('33333333-3333-3333-3333-333333333333', '11111111-1111-1111-1111-111111111111', 0)
ON CONFLICT (zone_id) DO NOTHING;

INSERT INTO zone_seats (seat_id, zone_id, seat_number)
SELECT md5('rock-fest-2026-seat-' || seat_index)::uuid,
       '33333333-3333-3333-3333-333333333333',
       'A-' || seat_index
FROM generate_series(1, 50) AS seat_index
ON CONFLICT (seat_id) DO NOTHING;

INSERT INTO tickets (ticket_id, seat_id, is_ticket_available, ticket_created_at_utc)
SELECT md5('rock-fest-2026-ticket-' || seat_index)::uuid,
       md5('rock-fest-2026-seat-' || seat_index)::uuid,
       TRUE,
       NOW()
FROM generate_series(1, 50) AS seat_index
ON CONFLICT (ticket_id) DO NOTHING;

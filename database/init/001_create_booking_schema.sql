-- BookingService relational schema (Event 1-N Zone 1-N Seat 1-1 Ticket N-1 User).
-- This script is the single source of truth for the database structure: it runs on the
-- first start of the docker compose database and in the integration test containers.

CREATE TABLE IF NOT EXISTS events
(
    event_id         UUID PRIMARY KEY,
    event_name       VARCHAR(200) NOT NULL,
    artist_name      VARCHAR(200) NOT NULL,
    venue_id         UUID         NOT NULL,
    venue_name       VARCHAR(200) NOT NULL,
    event_date_utc   TIMESTAMPTZ  NOT NULL,
    total_seat_count INTEGER      NOT NULL,
    CONSTRAINT ck_events_total_seat_count_positive CHECK (total_seat_count > 0)
);

CREATE TABLE IF NOT EXISTS event_zones
(
    zone_id    UUID PRIMARY KEY,
    event_id   UUID           NOT NULL,
    zone_price NUMERIC(12, 2) NOT NULL,
    CONSTRAINT fk_event_zones_events FOREIGN KEY (event_id) REFERENCES events (event_id) ON DELETE CASCADE,
    CONSTRAINT ck_event_zones_zone_price_not_negative CHECK (zone_price >= 0)
);

CREATE INDEX IF NOT EXISTS ix_event_zones_event_id ON event_zones (event_id);

CREATE TABLE IF NOT EXISTS zone_seats
(
    seat_id     UUID PRIMARY KEY,
    zone_id     UUID        NOT NULL,
    seat_number VARCHAR(20) NOT NULL,
    CONSTRAINT fk_zone_seats_event_zones FOREIGN KEY (zone_id) REFERENCES event_zones (zone_id) ON DELETE CASCADE,
    CONSTRAINT uq_zone_seats_zone_id_seat_number UNIQUE (zone_id, seat_number)
);

CREATE TABLE IF NOT EXISTS users
(
    user_id            UUID PRIMARY KEY,
    user_full_name     VARCHAR(200) NOT NULL,
    user_email_address VARCHAR(254) NOT NULL,
    CONSTRAINT uq_users_user_email_address UNIQUE (user_email_address)
);

CREATE TABLE IF NOT EXISTS tickets
(
    ticket_id                UUID PRIMARY KEY,
    seat_id                  UUID        NOT NULL,
    user_id                  UUID        NULL,
    ticket_code              VARCHAR(40) NULL,
    is_ticket_available      BOOLEAN     NOT NULL DEFAULT TRUE,
    ticket_created_at_utc    TIMESTAMPTZ NOT NULL,
    ticket_purchased_at_utc  TIMESTAMPTZ NULL,
    purchase_idempotency_key UUID        NULL,
    CONSTRAINT fk_tickets_zone_seats FOREIGN KEY (seat_id) REFERENCES zone_seats (seat_id) ON DELETE CASCADE,
    CONSTRAINT fk_tickets_users FOREIGN KEY (user_id) REFERENCES users (user_id),
    CONSTRAINT uq_tickets_seat_id UNIQUE (seat_id),
    CONSTRAINT uq_tickets_ticket_code UNIQUE (ticket_code),
    CONSTRAINT uq_tickets_purchase_idempotency_key UNIQUE (purchase_idempotency_key),
    -- An available ticket has no buyer; a sold ticket has a buyer, a code, a purchase date and a key.
    CONSTRAINT ck_tickets_sale_data_matches_availability CHECK (
        (is_ticket_available AND user_id IS NULL AND ticket_code IS NULL
            AND ticket_purchased_at_utc IS NULL AND purchase_idempotency_key IS NULL)
        OR
        (NOT is_ticket_available AND user_id IS NOT NULL AND ticket_code IS NOT NULL
            AND ticket_purchased_at_utc IS NOT NULL AND purchase_idempotency_key IS NOT NULL)
    )
);

CREATE INDEX IF NOT EXISTS ix_tickets_user_id ON tickets (user_id);

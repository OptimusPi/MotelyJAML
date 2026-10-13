-- Filters the server has run, and the seeds they found. One row per (seed, filter): a seed found
-- again keeps its latest score and tallies. Tallies are {"<column label>": <count>} as the CSV
-- would print them.

create table if not exists filters (
    slug        text primary key,
    name        text,
    jaml        text not null,
    author      text,
    created_at  timestamptz not null default now(),
    updated_at  timestamptz not null default now()
);

create table if not exists seeds (
    seed        text not null,
    filter_slug text not null references filters (slug) on delete cascade,
    score       integer not null,
    tallies     jsonb not null default '{}'::jsonb,
    found_by    text,
    found_at    timestamptz not null default now(),
    primary key (seed, filter_slug)
);

create index if not exists seeds_by_filter_score on seeds (filter_slug, score desc, found_at);

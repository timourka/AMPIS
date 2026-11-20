create table outbox(id bigint generated always as identity primary key, body text not null);
create table users(id uuid primary key, login text unique not null, password text not null, name text not null);

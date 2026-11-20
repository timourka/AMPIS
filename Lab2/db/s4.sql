create table users(id uuid primary key, name text not null);
create table files(id uuid primary key, owner_id uuid not null, name text not null);
create table downloads(id uuid primary key, file_id uuid not null, user_id uuid);
create index on downloads(file_id);
create table inbox(id uuid primary key);

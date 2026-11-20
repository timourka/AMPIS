create table outbox(id bigint generated always as identity primary key, body text not null);
create table downloads(id uuid primary key, file_id uuid not null, user_id uuid, downloaded_at timestamptz not null default now());
create database files_read;
\connect files_read
create table files(id uuid primary key, owner_id uuid not null, name text not null, content bytea not null);
create role filereader login password 'filereader';
grant connect on database files_read to filereader;
grant select on files to filereader;
create subscription lab_files connection 'host=b2 dbname=lab user=replicator password=replicator' publication lab_files;

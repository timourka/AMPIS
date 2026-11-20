create table outbox(id bigint generated always as identity primary key, body text not null);
create table files(id uuid primary key, owner_id uuid not null, name text not null, content bytea not null check(octet_length(content)<=1048576));
create publication lab_files for table files;
create role replicator login replication password 'replicator';
grant select on files to replicator;

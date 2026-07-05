-- Set up Storage Buckets
insert into storage.buckets (id, name, public)
values 
  ('avatars', 'avatars', true),
  ('documents', 'documents', false)
on conflict (id) do nothing;

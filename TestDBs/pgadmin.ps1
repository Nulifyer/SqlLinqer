docker rm -f custom_pgadmin
docker run --name custom_pgadmin `
  -e PGADMIN_DEFAULT_EMAIL=admin@example.com `
  -e PGADMIN_DEFAULT_PASSWORD=adminpass `
  -p 5433:80 `
  -d dpage/pgadmin4
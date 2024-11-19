docker rm -f custom_postgres
docker run --name custom_postgres `
  -e POSTGRES_PASSWORD=dbpass `
  -e POSTGRES_DB=employees `
  -e POSTGRES_USER=dbuser `
  -v ${PWD}/setup_postgres.sql:/docker-entrypoint-initdb.d/setup.sql `
  -v ${PWD}/postgres.conf:/etc/postgresql/postgresql.conf `
  -p 5432:5432 `
  -d postgres:14-alpine `
  -c 'config_file=/etc/postgresql/postgresql.conf'
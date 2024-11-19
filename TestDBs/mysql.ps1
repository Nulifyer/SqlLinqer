docker rm -f custom_mysql
docker run --name custom_mysql `
  -e MYSQL_ROOT_PASSWORD=asdf `
  -e MYSQL_DATABASE=employees `
  -e MYSQL_USER=dbuser `
  -e MYSQL_PASSWORD=dbpass `
  -v ${PWD}/mysql.conf:/etc/mysql/conf.d/my.conf `
  -v ${PWD}/setup_mysql.sql:/docker-entrypoint-initdb.d/setup.sql `
  -p 3306:3306 `
  -d mysql:8.0
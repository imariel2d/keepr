# Local dev shortcuts.
#   make up    - backend stack (API + Postgres + MinIO) in Docker, hot-reloading
#   make web   - Angular SPA on the host with hot reload
#   make down  - stop the backend stack
# Run `make up` and `make web` in two terminals, then open http://localhost:4200.

COMPOSE := docker compose -f docker-compose.yml -f docker-compose.api.yml

.PHONY: up down logs restart web infra ps clean help

## Start the backend stack (API + Postgres + MinIO) in Docker, building as needed.
up:
	$(COMPOSE) up -d --build

## Stop the backend stack (keeps Postgres/MinIO data).
down:
	$(COMPOSE) down

## Follow the API logs.
logs:
	$(COMPOSE) logs -f api

## Restart just the API container.
restart:
	$(COMPOSE) restart api

## Run the Angular SPA on the host (hot reload) -> http://localhost:4200.
web:
	npm start --prefix src/ClientApp

## Start only Postgres + MinIO (when running the API on the host instead).
infra:
	docker compose up -d

## Show container status.
ps:
	$(COMPOSE) ps

## Stop the stack and wipe all data volumes (Postgres, MinIO, NuGet cache).
clean:
	$(COMPOSE) down -v

## List targets.
help:
	@grep -B1 -E '^[a-z-]+:' $(MAKEFILE_LIST) | grep -E '^##|^[a-z-]+:' | \
		sed -e 's/^## //' -e 'N;s/\n\([a-z-]*\):.*/ -> \1/' | \
		awk '{printf "  %s\n", $$0}'
